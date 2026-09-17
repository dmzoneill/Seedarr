using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public class PeerServer : BackgroundService, IHandle<VpnInterfaceRestoredEvent>, IHandle<TorrentUpdatedEvent>, IHandle<TorrentDeletedEvent>
{
    private const int OutgoingConnectTimeoutMs = 5000;
    private const int UnauthenticatedHandshakeTimeoutMs = 5000;
    private readonly IConfigService _configService;
    private readonly ITorrentService _torrentService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IConnectionManager _connectionManager;
    private readonly IPeerDiscoveryService _peerDiscovery;
    private readonly Trackers.MultiTracker.IMultiTrackerManager _multiTracker;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly Trackers.Metrics.ITrackerMetricService _trackerMetricService;
    private readonly Trackers.ITrackerAnnounceService _trackerAnnounceService;
    private readonly IClientBehaviorSimulator _clientBehaviorSimulator;
    private readonly IRandomNumberGenerator _random;
    private readonly Transport.IUtpManager _utpManager;
    private readonly IVpnKillSwitchService _vpnKillSwitchService;
    private readonly Extensions.IFastExtensionHandler _fastExtensionHandler;
    private readonly Extensions.IExtensionManager _extensionManager;
    private readonly IChokeManager _chokeManager;
    private readonly Network.IProxySettingsProvider _proxySettingsProvider;
    private readonly SemaphoreSlim _connectionSemaphore;
    private readonly SemaphoreSlim _halfOpenSemaphore;
    private readonly ConcurrentDictionary<string, int> _connectionsPerIp = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _inFlightOutgoingEndpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Torrent> _torrentCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Logger _logger;
    private readonly object _listenerLock = new();
    private readonly SemaphoreSlim _rebindSignal = new(0, 1);
    private TcpListener _listener;
    private CancellationTokenSource _listenerCts;

    public Socket ListenerSocket => _listener?.Server;

    internal bool IsOutgoingEndpointInFlight(string ip, int port) => _inFlightOutgoingEndpoints.ContainsKey($"{ip}:{port}");

    public PeerServer(
        IConfigService configService,
        ITorrentService torrentService,
        IConnectionManager connectionManager,
        IPeerDiscoveryService peerDiscovery,
        Trackers.MultiTracker.IMultiTrackerManager multiTracker,
        ITrackerEntryService trackerEntryService = null,
        ITorrentEventLogService eventLogService = null,
        Trackers.Metrics.ITrackerMetricService trackerMetricService = null,
        Trackers.ITrackerAnnounceService trackerAnnounceService = null,
        IRandomNumberGenerator random = null,
        Transport.IUtpManager utpManager = null,
        IClientBehaviorSimulator clientBehaviorSimulator = null,
        IVpnKillSwitchService vpnKillSwitchService = null,
        Extensions.IFastExtensionHandler fastExtensionHandler = null,
        Extensions.IExtensionManager extensionManager = null,
        IChokeManager chokeManager = null,
        Network.IProxySettingsProvider proxySettingsProvider = null)
    {
        _configService = configService;
        _torrentService = torrentService;
        _connectionManager = connectionManager;
        _peerDiscovery = peerDiscovery;
        _multiTracker = multiTracker;
        _trackerEntryService = trackerEntryService;
        _eventLogService = eventLogService;
        _trackerMetricService = trackerMetricService;
        _fastExtensionHandler = fastExtensionHandler;
        _extensionManager = extensionManager;
        _chokeManager = chokeManager;
        _proxySettingsProvider = proxySettingsProvider;
        _trackerAnnounceService = trackerAnnounceService ??
            (trackerEntryService != null && multiTracker != null && peerDiscovery != null && eventLogService != null && configService != null
                ? new Trackers.TrackerAnnounceService(trackerEntryService, multiTracker, peerDiscovery, eventLogService, configService, trackerMetricService)
                : null);
        _clientBehaviorSimulator = clientBehaviorSimulator;
        _random = random ?? new RandomNumberGenerator();
        _utpManager = utpManager;
        _vpnKillSwitchService = vpnKillSwitchService;

        var maxGlobal = configService.MaxGlobalConnections > 0 ? configService.MaxGlobalConnections : 200;
        var maxHalfOpen = configService.MaximumHalfOpenConnections > 0 ? configService.MaximumHalfOpenConnections : 50;

        _connectionSemaphore = new SemaphoreSlim(maxGlobal);
        _halfOpenSemaphore = new SemaphoreSlim(maxHalfOpen);
        _logger = LogManager.GetCurrentClassLogger();

        if (_vpnKillSwitchService != null)
        {
            _vpnKillSwitchService.VpnDropped += OnVpnDropped;
            _vpnKillSwitchService.VpnRestored += OnVpnRestored;
        }
    }

    private void OnVpnDropped(string iface)
    {
        _logger.Warn("VPN kill switch engaged (interface '{0}' dropped). Terminating all active peer connections.", iface);
        try
        {
            _connectionManager?.DisconnectAll();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error disconnecting peers after VPN kill switch triggered");
        }

        if (HasDedicatedBindInterface())
        {
            StopListener();
        }
    }

    private void OnVpnRestored(string iface)
    {
        _logger.Info("VPN interface '{0}' restored. Rebinding peer listener socket.", iface);
        TriggerRebind();
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        OnVpnRestored(message?.InterfaceName);
    }

    public void Handle(TorrentUpdatedEvent message)
    {
        if (!string.IsNullOrEmpty(message?.Torrent?.InfoHash))
        {
            _torrentCache[message.Torrent.InfoHash] = message.Torrent;
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        var infoHash = message?.Torrent?.InfoHash;
        if (!string.IsNullOrEmpty(infoHash))
        {
            _torrentCache.TryRemove(infoHash, out _);
            _peerDiscovery?.RemoveTorrent(infoHash);
        }
        else if (message?.TorrentId > 0)
        {
            foreach (var kvp in _torrentCache)
            {
                if (kvp.Value?.Id == message.TorrentId)
                {
                    _torrentCache.TryRemove(kvp.Key, out _);
                    _peerDiscovery?.RemoveTorrent(kvp.Key);
                    break;
                }
            }
        }
    }

    public override void Dispose()
    {
        if (_vpnKillSwitchService != null)
        {
            _vpnKillSwitchService.VpnDropped -= OnVpnDropped;
            _vpnKillSwitchService.VpnRestored -= OnVpnRestored;
        }

        StopListener();
        _connectionsPerIp.Clear();
        _rebindSignal?.Dispose();
        _connectionSemaphore?.Dispose();
        _halfOpenSemaphore?.Dispose();
        base.Dispose();
    }

    private void StopListener()
    {
        lock (_listenerLock)
        {
            if (_listenerCts != null && !_listenerCts.IsCancellationRequested)
            {
                try
                {
                    _listenerCts.Cancel();
                }
                catch
                {
                }
            }

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch
                {
                }

                _listener = null;
            }
        }
    }

    private void TriggerRebind()
    {
        StopListener();

        try
        {
            if (_rebindSignal.CurrentCount == 0)
            {
                _rebindSignal.Release();
            }
        }
        catch
        {
        }
    }

    private async Task WaitUntilRestoredOrCancelledAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _rebindSignal.WaitAsync(TimeSpan.FromSeconds(2), stoppingToken);
        }
        catch (ObjectDisposedException)
        {
            stoppingToken.ThrowIfCancellationRequested();
        }
    }

    private bool HasDedicatedBindInterface()
    {
        var bindIface = _configService.BindInterface?.Trim();
        return !string.IsNullOrWhiteSpace(bindIface) &&
               !bindIface.Equals("Any", StringComparison.OrdinalIgnoreCase) &&
               !bindIface.Equals("all", StringComparison.OrdinalIgnoreCase) &&
               !bindIface.Equals("*", StringComparison.OrdinalIgnoreCase) &&
               !bindIface.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) &&
               !bindIface.Equals("::", StringComparison.OrdinalIgnoreCase);
    }

    private IPAddress ResolveDedicatedBindIp()
    {
        if (_vpnKillSwitchService != null)
        {
            if (_vpnKillSwitchService.IsFailClosedActive)
            {
                return null;
            }

            if (_configService.EnableIPv6)
            {
                var ip6 = _vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetworkV6);
                if (ip6 != null)
                {
                    return ip6;
                }
            }

            var ip4 = _vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork);
            if (ip4 != null)
            {
                return ip4;
            }
        }

        var bindIface = _configService.BindInterface?.Trim();
        if (string.IsNullOrWhiteSpace(bindIface))
        {
            return null;
        }

        if (IPAddress.TryParse(bindIface, out var parsed))
        {
            if (!parsed.Equals(IPAddress.Any) && !parsed.Equals(IPAddress.IPv6Any))
            {
                return parsed;
            }

            return null;
        }

        return ResolveInterfaceIpFromName(bindIface);
    }

    private IPAddress ResolveInterfaceIpFromName(string interfaceName)
    {
        try
        {
            var family = _configService.EnableIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            var nic = nics.FirstOrDefault(n =>
                string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));

            if (nic == null || nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
            {
                return null;
            }

            var unicast = nic.GetIPProperties()?.UnicastAddresses;
            if (unicast == null)
            {
                return null;
            }

            if (_configService.EnableIPv6)
            {
                var ip6 = unicast.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                    !IPAddress.IsLoopback(a.Address) &&
                    !a.Address.Equals(IPAddress.IPv6Any) &&
                    !a.Address.Equals(IPAddress.IPv6None) &&
                    !a.Address.IsIPv6LinkLocal &&
                    !a.Address.IsIPv6SiteLocal &&
                    !a.Address.IsIPv6Multicast)?.Address;

                if (ip6 != null)
                {
                    return ip6;
                }
            }

            var ip4 = unicast.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(a.Address) &&
                !a.Address.Equals(IPAddress.Any) &&
                !a.Address.Equals(IPAddress.None))?.Address;

            return ip4;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to resolve IP for interface '{0}'", interfaceName);
            return null;
        }
    }

    private IPAddress GetBindAddress()
    {
        if (HasDedicatedBindInterface())
        {
            return ResolveDedicatedBindIp();
        }

        return _configService.EnableIPv6 ? IPAddress.IPv6Any : IPAddress.Any;
    }

    private IPAddress GetListenAddress() => GetBindAddress();

    private EncryptionMode GetEncryptionMode()
    {
        return _configService.EncryptionMode?.ToLowerInvariant() switch
        {
            "required" or "forced" => EncryptionMode.RequireEncrypted,
            "disabled" or "plain" or "none" => EncryptionMode.PreferPlainText,
            _ => EncryptionMode.PreferEncrypted
        };
    }

    private void ApplySocketQos(Socket socket)
    {
        if (socket == null)
        {
            return;
        }

        try
        {
            if (_configService.PeerDscp > 0)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, _configService.PeerDscp << 2);
            }
            else if (_configService.PeerTos > 0)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, _configService.PeerTos);
            }
        }
        catch (Exception ex)
        {
            _logger.Trace(ex, "Failed to apply DSCP/TOS socket options");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listenerTask = RunListenerAsync(stoppingToken);
        var contactTask = RunPeerContactLoopAsync(stoppingToken);
        await Task.WhenAll(listenerTask, contactTask);
    }

    private async Task RunListenerAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var bindAddress = GetBindAddress();
            if (bindAddress == null)
            {
                var configuredIface = _configService.BindInterface?.Trim();
                _logger.Warn(
                    "Configured bind interface '{0}' is not available or unplumbed. Deferring peer listener binding.",
                    configuredIface);

                try
                {
                    await WaitUntilRestoredOrCancelledAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            var listeningPort = _configService.ListeningPort;
            TcpListener listener;

            try
            {
                listener = new TcpListener(bindAddress, listeningPort);
                if (bindAddress.Equals(IPAddress.IPv6Any))
                {
                    listener.Server.DualMode = true;
                }

                listener.Start();
            }
            catch (SocketException ex)
            {
                if (HasDedicatedBindInterface())
                {
                    _logger.Warn(
                        ex,
                        "Peer server failed to bind configured interface {0}:{1}. Deferring listener binding.",
                        bindAddress,
                        listeningPort);

                    try
                    {
                        await WaitUntilRestoredOrCancelledAsync(stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }

                _logger.Warn(
                    ex,
                    "Peer server failed to bind {0}:{1}, attempting fallback to IPv4 Any",
                    bindAddress,
                    listeningPort);

                try
                {
                    listener = new TcpListener(IPAddress.Any, listeningPort);
                    listener.Start();
                }
                catch (Exception fallbackEx)
                {
                    _logger.Warn(
                        fallbackEx,
                        "Peer server failed fallback bind on port {0}, skipping",
                        listeningPort);
                    return;
                }
            }

            _logger.Info("Peer server listening on {0}:{1}", bindAddress, listeningPort);

            CancellationToken linkedToken;
            lock (_listenerLock)
            {
                _listener = listener;
                _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                linkedToken = _listenerCts.Token;
            }

            try
            {
                while (!linkedToken.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(linkedToken);

                    if (_vpnKillSwitchService?.IsFailClosedActive == true)
                    {
                        _logger.Debug("VPN fail-closed engaged; rejecting incoming peer connection");
                        client.Dispose();
                        continue;
                    }

                    _ = Task.Run(
                        async () =>
                        {
                            await ProcessIncomingClientAsync(client, stoppingToken);
                        },
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stoppingToken is cancelled or _listenerCts cancelled for rebind
            }
            catch (SocketException)
            {
                // Expected when listener is stopped/closed
            }
            catch (ObjectDisposedException)
            {
                // Expected when listener is stopped/disposed
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.Debug(ex, "Exception in peer server listener loop");
            }
            finally
            {
                lock (_listenerLock)
                {
                    try
                    {
                        listener.Stop();
                    }
                    catch
                    {
                    }

                    if (_listener == listener)
                    {
                        _listener = null;
                    }

                    _listenerCts?.Dispose();
                    _listenerCts = null;
                }
            }
        }
    }

    private async Task ProcessIncomingClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        string clientIp = null;
        var ipReserved = false;
        var acquiredConnectionSemaphore = false;
        var acquiredHalfOpenSemaphore = false;

        try
        {
            if (client?.Client?.RemoteEndPoint is not IPEndPoint endpoint)
            {
                client?.Dispose();
                return;
            }

            clientIp = endpoint.Address.ToString();
            var maxPerIp = _configService.MaxConnectionsPerIp > 0 ? _configService.MaxConnectionsPerIp : 5;
            var currentCount = _connectionsPerIp.AddOrUpdate(clientIp, 1, (_, count) => count + 1);
            ipReserved = true;

            if (currentCount > maxPerIp)
            {
                client.Dispose();
                return;
            }

            ApplySocketQos(client.Client);

            try
            {
                if (client.Client != null)
                {
                    client.Client.ReceiveTimeout = UnauthenticatedHandshakeTimeoutMs;
                }
            }
            catch
            {
            }

            try
            {
                if (!await _halfOpenSemaphore.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken))
                {
                    client.Dispose();
                    return;
                }

                acquiredHalfOpenSemaphore = true;
            }
            catch
            {
                client.Dispose();
                return;
            }

            try
            {
                if (!await _connectionSemaphore.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken))
                {
                    client.Dispose();
                    return;
                }

                acquiredConnectionSemaphore = true;
            }
            catch
            {
                client.Dispose();
                return;
            }

            var halfOpenReleased = 0;
            void ReleaseHalfOpen()
            {
                if (Interlocked.Exchange(ref halfOpenReleased, 1) == 0)
                {
                    if (acquiredHalfOpenSemaphore)
                    {
                        _halfOpenSemaphore.Release();
                        acquiredHalfOpenSemaphore = false;
                    }
                }
            }

            try
            {
                HandleConnection(client, stoppingToken, ReleaseHalfOpen);
            }
            finally
            {
                ReleaseHalfOpen();
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to handle incoming peer connection from {0}", clientIp ?? "unknown");
            client?.Dispose();
        }
        finally
        {
            if (acquiredHalfOpenSemaphore)
            {
                _halfOpenSemaphore.Release();
            }

            if (acquiredConnectionSemaphore)
            {
                _connectionSemaphore.Release();
            }

            if (ipReserved && clientIp != null)
            {
                DecrementConnectionCount(clientIp);
            }
        }
    }

    private async Task RunPeerContactLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            var startupDelay = Math.Max(1, _configService.PeerContactIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(startupDelay), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_vpnKillSwitchService?.IsFailClosedActive == true)
                {
                    _logger.Debug("VPN fail-closed active, suppressing peer contact loop");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                var intervalSeconds = _configService.PeerContactIntervalSeconds;

                var torrents = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading)
                    .Where(t => !string.IsNullOrEmpty(t.InfoHash))
                    .ToList();

                _logger.Debug("Peer contact cycle: {0} active torrents", torrents.Count);

                foreach (var torrent in torrents)
                {
                    if (stoppingToken.IsCancellationRequested || _vpnKillSwitchService?.IsFailClosedActive == true)
                    {
                        break;
                    }

                    DiscoverPeersFromTracker(torrent);
                    ConnectToDiscoveredPeers(torrent, stoppingToken);
                }

                _peerDiscovery?.PruneStalePeers();

                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DiscoverPeersFromTracker(Torrent torrent)
    {
        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return;
        }

        try
        {
            _trackerAnnounceService?.AnnounceTorrent(torrent, force: false);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Tracker announce failed for {0}", torrent.Name);
        }
    }

    private void ConnectToDiscoveredPeers(Torrent torrent, CancellationToken stoppingToken)
    {
        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return;
        }

        if (!_connectionManager.CanAddConnectionForTorrent(torrent.InfoHash))
        {
            return;
        }

        var candidates = _peerDiscovery.GetPeers(torrent.InfoHash, 5);
        if (candidates.Count == 0)
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            if (stoppingToken.IsCancellationRequested ||
                _vpnKillSwitchService?.IsFailClosedActive == true ||
                !_connectionManager.CanAddConnectionForTorrent(torrent.InfoHash))
            {
                break;
            }

            var endpointKey = $"{candidate.Ip}:{candidate.Port}";
            if (!_inFlightOutgoingEndpoints.TryAdd(endpointKey, 0))
            {
                continue;
            }

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await ConnectToPeerAsync(torrent, candidate, stoppingToken);
                    }
                    finally
                    {
                        _inFlightOutgoingEndpoints.TryRemove(endpointKey, out _);
                    }
                },
                stoppingToken);
        }
    }

    private Task ConnectToPeer(Torrent torrent, DiscoveredPeer candidate)
    {
        return ConnectToPeerAsync(torrent, candidate, CancellationToken.None);
    }

    private async Task ConnectToPeerAsync(Torrent torrent, DiscoveredPeer candidate, CancellationToken stoppingToken)
    {
        if (_vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return;
        }

        if (HasDedicatedBindInterface() && GetBindAddress() == null)
        {
            _logger.Debug(
                "Dedicated bind interface '{0}' is unplumbed or unavailable; suppressing outgoing connection to {1}:{2}",
                _configService.BindInterface,
                candidate.Ip,
                candidate.Port);
            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
            return;
        }

        if (!_configService.EnableIPv6 &&
            IPAddress.TryParse(candidate.Ip, out var candIp) &&
            candIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _logger.Debug("IPv6 disabled, skipping IPv6 peer {0}:{1}", candidate.Ip, candidate.Port);
            return;
        }

        var maxPerIp = _configService.MaxConnectionsPerIp > 0 ? _configService.MaxConnectionsPerIp : 5;
        var currentCount = _connectionsPerIp.AddOrUpdate(candidate.Ip, 1, (_, count) => count + 1);
        if (currentCount > maxPerIp)
        {
            DecrementConnectionCount(candidate.Ip);
            return;
        }

        var acquiredHalfOpen = false;
        PeerConnection connection = null;
        try
        {
            try
            {
                if (!await _halfOpenSemaphore.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken))
                {
                    return;
                }

                acquiredHalfOpen = true;
            }
            catch
            {
                return;
            }

            _logger.Debug("Connecting to peer {0}:{1} for {2}", candidate.Ip, candidate.Port, torrent.Name);
            _eventLogService?.Debug(torrent.Id, "Peers", $"Attempting connection to peer {candidate.Ip}:{candidate.Port} (source: {candidate.Source})");

            var localBind = _vpnKillSwitchService?.GetVpnInterfaceIpAddress();
            if (localBind == null && HasDedicatedBindInterface())
            {
                localBind = ResolveDedicatedBindIp();
            }

            if (HasDedicatedBindInterface() && localBind == null)
            {
                _logger.Warn(
                    "Dedicated bind interface '{0}' is configured but unplumbed/unavailable. Failing closed on outgoing connection to {1}:{2}",
                    _configService.BindInterface,
                    candidate.Ip,
                    candidate.Port);
                _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                return;
            }

            if (_utpManager != null && _utpManager.IsEnabled)
            {
                try
                {
                    var utp = _utpManager.CreateConnection();
                    var endpoint = new IPEndPoint(IPAddress.Parse(candidate.Ip), candidate.Port);
                    utp.Connect(endpoint);
                    if (utp.IsConnected)
                    {
                        connection = new PeerConnection(utp.GetStream(), candidate.Ip, candidate.Port);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "uTP connection attempt to {0}:{1} failed", candidate.Ip, candidate.Port);
                }

                if (connection == null && _utpManager.TcpFallbackEnabled)
                {
                    _logger.Debug("Falling back to TCP for peer {0}:{1}", candidate.Ip, candidate.Port);
                    connection = new PeerConnection(candidate.Ip, candidate.Port, localBind, _configService.PeerDscp, _configService.PeerTos, _proxySettingsProvider);
                }
            }
            else
            {
                connection = new PeerConnection(candidate.Ip, candidate.Port, localBind, _configService.PeerDscp, _configService.PeerTos, _proxySettingsProvider);
            }

            if (connection == null)
            {
                _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                return;
            }

            connection.HandshakeTimeoutMs = Math.Min(_configService.HandshakeTimeoutSeconds * 1000, OutgoingConnectTimeoutMs);
            connection.MessageReadTimeoutMs = _configService.MessageReadTimeoutSeconds * 1000;
            connection.KeepAliveIntervalSeconds = _configService.KeepAliveIntervalSeconds;
            connection.MaxPipelinedRequests = _configService.PeerRequestCount;
            connection.IdleChance = _clientBehaviorSimulator != null
                ? _clientBehaviorSimulator.GetEffectiveIdleChance(_configService.PeerIdleChance)
                : _configService.PeerIdleChance;

            if (!connection.NegotiateEncryptionOutgoing(torrent.InfoHash, GetEncryptionMode()))
            {
                _logger.Debug("Outgoing encryption failed to {0}:{1}", candidate.Ip, candidate.Port);
                _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                _eventLogService?.Debug(torrent.Id, "Peers", $"Encryption negotiation rejected by peer {candidate.Ip}:{candidate.Port}");
                connection.Dispose();
                return;
            }

            var session = (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
                ? _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate)
                : null;
            var profile = session?.Profile ?? ((_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
                ? _clientBehaviorSimulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate)
                : null);
            var peerId = session?.PeerId ?? profile?.GeneratePeerId() ?? "-SD1000-000000000000";
            connection.SendHandshake(torrent.InfoHash, peerId, torrent.IsPrivate, profile);

            if (!connection.ReceiveHandshake())
            {
                _logger.Debug("Outgoing handshake failed from {0}:{1}", candidate.Ip, candidate.Port);
                _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
                _eventLogService?.Debug(torrent.Id, "Peers", $"BitTorrent handshake rejected/timed out from {candidate.Ip}:{candidate.Port}");
                connection.Dispose();
                return;
            }

            if (connection.SupportsFastExtension && _fastExtensionHandler != null && !string.IsNullOrEmpty(torrent.InfoHash))
            {
                _fastExtensionHandler.RegisterFastPeer(connection, Convert.FromHexString(torrent.InfoHash), torrent.PieceCount, 10);
            }

            if (connection.SupportsExtensionProtocol && _extensionManager != null)
            {
                var extHandshake = _extensionManager.BuildExtensionHandshake(torrent.IsPrivate, profile);
                var payload = new byte[extHandshake.Length + 1];
                payload[0] = 0;
                Array.Copy(extHandshake, 0, payload, 1, extHandshake.Length);
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Extended, Payload = payload });
            }

            if (_fastExtensionHandler != null && connection.SupportsFastExtension)
            {
                _fastExtensionHandler.SendHaveAllOrBitfield(connection, torrent.PieceCount, true);
            }
            else
            {
                connection.SendBitfield(torrent.PieceCount);
            }

            _chokeManager?.PeerConnected(connection);
            if (_chokeManager != null)
            {
                if (_chokeManager.CanUnchoke(connection))
                {
                    connection.AmChoking = false;
                    connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                }
            }
            else
            {
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                connection.AmChoking = false;
            }

            _connectionManager.Add(connection);
            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, true);

            _logger.Info(
                "Outgoing peer connected: {0}:{1} for {2} (encrypted: {3})",
                candidate.Ip,
                candidate.Port,
                torrent.Name,
                connection.IsEncrypted);

            _eventLogService?.Info(
                torrent.Id,
                "Peers",
                $"Peer connected & active: {candidate.Ip}:{candidate.Port} (encrypted: {connection.IsEncrypted})");

            HandlePeerSession(connection, torrent);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to connect to peer {0}:{1}", candidate.Ip, candidate.Port);
            _peerDiscovery.MarkAttempted(torrent.InfoHash, candidate.Ip, candidate.Port, false);
            _eventLogService?.Debug(torrent.Id, "Peers", $"Failed to connect to peer {candidate.Ip}:{candidate.Port}: {ex.Message}");
            connection?.Dispose();
        }
        finally
        {
            if (acquiredHalfOpen)
            {
                _halfOpenSemaphore.Release();
            }

            DecrementConnectionCount(candidate.Ip);
        }
    }

    private void HandlePeerSession(PeerConnection connection, Torrent torrent)
    {
        try
        {
            if (torrent != null && !string.IsNullOrEmpty(torrent.InfoHash))
            {
                _torrentCache[torrent.InfoHash] = torrent;
            }

            while (connection.IsConnected)
            {
                if (_vpnKillSwitchService?.IsFailClosedActive == true)
                {
                    _logger.Debug("VPN fail-closed engaged; terminating session with {0}:{1}", connection.RemoteIp, connection.RemotePort);
                    break;
                }

                var message = connection.ReceiveMessage();
                if (message == null)
                {
                    if (!connection.IsConnected)
                    {
                        break;
                    }

                    var elapsed = DateTime.UtcNow - connection.LastActivity;
                    if (elapsed.TotalSeconds >= connection.KeepAliveIntervalSeconds)
                    {
                        connection.SendKeepAlive();
                    }

                    continue;
                }

                HandleMessage(connection, message, torrent);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Peer session ended with {0}:{1}", connection.RemoteIp, connection.RemotePort);
        }
        finally
        {
            _fastExtensionHandler?.UnregisterPeer(connection);
            _chokeManager?.PeerDisconnected(connection);
            _connectionManager.Remove(connection);
            connection.PendingRequestCount = 0;
            connection.Dispose();
        }
    }

    private void HandleConnection(TcpClient client, CancellationToken stoppingToken)
    {
        HandleConnection(client, stoppingToken, null);
    }

    private void HandleConnection(TcpClient client, CancellationToken stoppingToken, Action onHandshakeSuccess)
    {
        using var connection = new PeerConnection(client);
        connection.HandshakeTimeoutMs = UnauthenticatedHandshakeTimeoutMs;
        connection.MessageReadTimeoutMs = _configService.MessageReadTimeoutSeconds * 1000;
        connection.KeepAliveIntervalSeconds = _configService.KeepAliveIntervalSeconds;
        connection.MaxPipelinedRequests = _configService.PeerRequestCount;
        connection.IdleChance = _clientBehaviorSimulator != null
            ? _clientBehaviorSimulator.GetEffectiveIdleChance(_configService.PeerIdleChance)
            : _configService.PeerIdleChance;

        if (client.Client != null)
        {
            try
            {
                client.Client.ReceiveTimeout = UnauthenticatedHandshakeTimeoutMs;
            }
            catch
            {
            }
        }

        _logger.Debug("Incoming peer: {0}:{1}", connection.RemoteIp, connection.RemotePort);

        try
        {
            var negotiated = connection.NegotiateEncryptionIncoming(ValidateInfoHash, GetEncryptionMode());
            if (!negotiated)
            {
                _logger.Debug("Encryption negotiation failed from {0}", connection.RemoteIp);
                return;
            }

            if (!connection.ReceiveHandshake())
            {
                _logger.Debug("Invalid handshake from {0}", connection.RemoteIp);
                return;
            }

            var torrent = GetCachedTorrent(connection.InfoHash);

            if (torrent == null)
            {
                _logger.Debug("Unknown info hash from {0}: {1}", connection.RemoteIp, connection.InfoHash);
                return;
            }

            onHandshakeSuccess?.Invoke();

            if (client.Client != null)
            {
                try
                {
                    client.Client.ReceiveTimeout = connection.MessageReadTimeoutMs > 0 ? connection.MessageReadTimeoutMs : 0;
                }
                catch
                {
                }
            }

            var session = (_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
                ? _clientBehaviorSimulator.GetOrCreateSession(torrent.InfoHash, torrent.IsPrivate)
                : null;
            var profile = session?.Profile ?? ((_clientBehaviorSimulator != null && _configService.ClientBehaviorEngineEnabled && !_configService.AnonymousMode)
                ? _clientBehaviorSimulator.GetProfileForTorrent(torrent.InfoHash, torrent.IsPrivate)
                : null);
            var peerId = session?.PeerId ?? profile?.GeneratePeerId() ?? "-SD1000-000000000000";
            connection.SendHandshake(torrent.InfoHash, peerId, torrent.IsPrivate, profile);

            _logger.Debug(
                "Peer {0} connected (encrypted: {1}, method: {2})",
                connection.RemoteIp,
                connection.IsEncrypted,
                connection.EncryptionMethod);

            if (connection.SupportsFastExtension && _fastExtensionHandler != null && !string.IsNullOrEmpty(torrent.InfoHash))
            {
                _fastExtensionHandler.RegisterFastPeer(connection, Convert.FromHexString(torrent.InfoHash), torrent.PieceCount, 10);
            }

            if (connection.SupportsExtensionProtocol && _extensionManager != null)
            {
                var extHandshake = _extensionManager.BuildExtensionHandshake(torrent.IsPrivate, profile);
                var payload = new byte[extHandshake.Length + 1];
                payload[0] = 0;
                Array.Copy(extHandshake, 0, payload, 1, extHandshake.Length);
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Extended, Payload = payload });
            }

            if (_fastExtensionHandler != null && connection.SupportsFastExtension)
            {
                _fastExtensionHandler.SendHaveAllOrBitfield(connection, torrent.PieceCount, true);
            }
            else
            {
                connection.SendBitfield(torrent.PieceCount);
            }

            _chokeManager?.PeerConnected(connection);
            if (_chokeManager != null)
            {
                if (_chokeManager.CanUnchoke(connection))
                {
                    connection.AmChoking = false;
                    connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                }
            }
            else
            {
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                connection.AmChoking = false;
            }

            _connectionManager.Add(connection);

            while (connection.IsConnected && !stoppingToken.IsCancellationRequested)
            {
                if (_vpnKillSwitchService?.IsFailClosedActive == true)
                {
                    _logger.Debug("VPN fail-closed engaged; terminating incoming session with {0}", connection.RemoteIp);
                    break;
                }

                var message = connection.ReceiveMessage();
                if (message == null)
                {
                    if (!connection.IsConnected)
                    {
                        break;
                    }

                    var elapsed = DateTime.UtcNow - connection.LastActivity;
                    if (elapsed.TotalSeconds >= connection.KeepAliveIntervalSeconds)
                    {
                        connection.SendKeepAlive();
                    }

                    continue;
                }

                HandleMessage(connection, message, torrent);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Peer connection error: {0}", connection.RemoteIp);
        }
        finally
        {
            _fastExtensionHandler?.UnregisterPeer(connection);
            _chokeManager?.PeerDisconnected(connection);
            _connectionManager.Remove(connection);
            connection.PendingRequestCount = 0;
        }
    }

    private bool ValidateInfoHash(byte[] skeyHash)
    {
        var torrents = _torrentService.GetAll();
        return torrents.Where(t => !string.IsNullOrEmpty(t.InfoHash)).Any(t =>
        {
            var infoHashBytes = Convert.FromHexString(t.InfoHash);
            var expected = MseKeyDerivation.DeriveKey(infoHashBytes, Encoding.ASCII.GetBytes("req2"));
            return expected.AsSpan().SequenceEqual(skeyHash);
        });
    }

    private void DecrementConnectionCount(string clientIp)
    {
        if (string.IsNullOrEmpty(clientIp))
        {
            return;
        }

        _connectionsPerIp.AddOrUpdate(clientIp, 0, (_, count) => Math.Max(0, count - 1));
        if (_connectionsPerIp.TryGetValue(clientIp, out var remaining) && remaining <= 0)
        {
            _connectionsPerIp.TryRemove(new KeyValuePair<string, int>(clientIp, 0));
        }
    }

    private Torrent GetCachedTorrent(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash) || _torrentService == null)
        {
            return null;
        }

        if (_torrentCache.TryGetValue(infoHash, out var cachedTorrent))
        {
            return cachedTorrent;
        }

        var torrent = _torrentService.GetByInfoHash(infoHash);
        if (torrent != null)
        {
            _torrentCache[infoHash] = torrent;
        }

        return torrent;
    }

    private void HandleMessage(PeerConnection connection, PeerMessage message, Torrent torrent = null)
    {
        torrent ??= GetCachedTorrent(connection?.InfoHash);

        switch (message.Type)
        {
            case PeerMessageType.Choke:
                connection.PeerChoking = true;
                connection.PendingRequestCount = 0;
                break;

            case PeerMessageType.Unchoke:
                connection.PeerChoking = false;
                break;

            case PeerMessageType.Interested:
                connection.PeerInterested = true;
                if (_chokeManager != null)
                {
                    _chokeManager.PeerInterestedChanged(connection);
                }
                else if (connection.AmChoking)
                {
                    connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                    connection.AmChoking = false;
                }

                break;

            case PeerMessageType.NotInterested:
                connection.PeerInterested = false;
                _chokeManager?.PeerInterestedChanged(connection);
                break;

            case PeerMessageType.Have:
                if (message.Payload != null && message.Payload.Length >= 4)
                {
                    var pieceIndex = (int)(((uint)message.Payload[0] << 24) | ((uint)message.Payload[1] << 16) | ((uint)message.Payload[2] << 8) | message.Payload[3]);
                    if (torrent != null && torrent.PieceCount > 0)
                    {
                        if (connection.PeerPieces == null)
                        {
                            connection.PeerPieces = new bool[torrent.PieceCount];
                        }

                        if (pieceIndex >= 0 && pieceIndex < connection.PeerPieces.Length)
                        {
                            connection.PeerPieces[pieceIndex] = true;
                            var haveCount = connection.PeerPieces.Count(b => b);
                            connection.Progress = (double)haveCount / torrent.PieceCount;
                            if (connection.IsSeed)
                            {
                                _chokeManager?.PeerBecameSeed(connection);
                            }
                        }
                    }
                }

                break;

            case PeerMessageType.Bitfield:
                if (message.Payload != null && torrent != null && torrent.PieceCount > 0)
                {
                    if (connection.PeerPieces == null)
                    {
                        connection.PeerPieces = new bool[torrent.PieceCount];
                    }

                    for (var i = 0; i < torrent.PieceCount; i++)
                    {
                        var byteIndex = i / 8;
                        var bitIndex = 7 - (i % 8);
                        if (byteIndex < message.Payload.Length)
                        {
                            connection.PeerPieces[i] = ((message.Payload[byteIndex] >> bitIndex) & 1) != 0;
                        }
                    }

                    var haveCount = connection.PeerPieces.Count(b => b);
                    connection.Progress = (double)haveCount / torrent.PieceCount;
                    if (connection.IsSeed)
                    {
                        _chokeManager?.PeerBecameSeed(connection);
                    }
                }

                break;

            case PeerMessageType.Request:
                _chokeManager?.UpdatePeerActivity(connection);

                if (_random.NextDouble() < connection.IdleChance)
                {
                    connection.SendKeepAlive();
                    break;
                }

                if (connection.PendingRequestCount >= connection.MaxPipelinedRequests)
                {
                    _logger.Trace(
                        "Request pipeline full ({0}) from {1}, rejecting",
                        connection.MaxPipelinedRequests,
                        connection.RemoteIp);

                    if (connection.SupportsFastExtension && _fastExtensionHandler != null && message.Payload?.Length >= 12)
                    {
                        var rejectMsg = _fastExtensionHandler.BuildRejectForRequest(message.Payload);
                        if (rejectMsg != null)
                        {
                            connection.SendMessage(rejectMsg);
                        }
                    }

                    break;
                }

                if (message.Payload != null && message.Payload.Length >= 12)
                {
                    var pieceIndex = (int)(((uint)message.Payload[0] << 24) | ((uint)message.Payload[1] << 16) | ((uint)message.Payload[2] << 8) | message.Payload[3]);
                    var isAllowedFast = connection.SupportsFastExtension && _fastExtensionHandler != null && _fastExtensionHandler.GetAllowedFastSet(connection).Contains(pieceIndex);

                    if (!connection.AmChoking || isAllowedFast || _chokeManager == null)
                    {
                        connection.PendingRequestCount++;
                        HandlePieceRequest(connection, message.Payload);
                    }
                    else
                    {
                        if (connection.SupportsFastExtension && _fastExtensionHandler != null)
                        {
                            var rejectMsg = _fastExtensionHandler.BuildRejectForRequest(message.Payload);
                            if (rejectMsg != null)
                            {
                                connection.SendMessage(rejectMsg);
                            }
                        }
                    }
                }

                break;

            case PeerMessageType.Cancel:
                if (connection.PendingRequestCount > 0)
                {
                    connection.PendingRequestCount--;
                }

                break;

            case PeerMessageType.SuggestPiece:
            case PeerMessageType.HaveAll:
            case PeerMessageType.HaveNone:
            case PeerMessageType.AllowedFast:
                if (connection.SupportsFastExtension && _fastExtensionHandler != null)
                {
                    _fastExtensionHandler.HandleMessage(connection, message, torrent?.PieceCount ?? 0);
                    if (message.Type == PeerMessageType.HaveAll && torrent != null && torrent.PieceCount > 0)
                    {
                        connection.PeerPieces = new bool[torrent.PieceCount];
                        Array.Fill(connection.PeerPieces, true);
                        connection.Progress = 1.0;
                        _chokeManager?.PeerBecameSeed(connection);
                    }
                    else if (message.Type == PeerMessageType.HaveNone && torrent != null && torrent.PieceCount > 0)
                    {
                        connection.PeerPieces = new bool[torrent.PieceCount];
                        connection.Progress = 0.0;
                    }
                }

                break;

            case PeerMessageType.RejectRequest:
                if (connection.PendingRequestCount > 0)
                {
                    connection.PendingRequestCount--;
                }

                if (connection.SupportsFastExtension && _fastExtensionHandler != null)
                {
                    _fastExtensionHandler.HandleMessage(connection, message, torrent?.PieceCount ?? 0);
                }

                break;

            case PeerMessageType.Extended:
                break;

            default:
                _logger.Trace("Ignoring message type {0} from {1}", message.Type, connection.RemoteIp);
                break;
        }
    }

    private static void HandlePieceRequest(PeerConnection connection, byte[] payload)
    {
        try
        {
            var index = (int)(((uint)payload[0] << 24) | ((uint)payload[1] << 16) | ((uint)payload[2] << 8) | payload[3]);
            var begin = (int)(((uint)payload[4] << 24) | ((uint)payload[5] << 16) | ((uint)payload[6] << 8) | payload[7]);
            var length = (int)(((uint)payload[8] << 24) | ((uint)payload[9] << 16) | ((uint)payload[10] << 8) | payload[11]);

            const int MaxBlockSize = 32768;
            if (length <= 0 || length > MaxBlockSize)
            {
                return;
            }

            if (index < 0 || begin < 0)
            {
                return;
            }

            var payloadSize = 8 + length;
            var piecePayload = ArrayPool<byte>.Shared.Rent(payloadSize);
            try
            {
                Array.Clear(piecePayload, 0, payloadSize);
                piecePayload[0] = (byte)(index >> 24);
                piecePayload[1] = (byte)(index >> 16);
                piecePayload[2] = (byte)(index >> 8);
                piecePayload[3] = (byte)index;
                piecePayload[4] = (byte)(begin >> 24);
                piecePayload[5] = (byte)(begin >> 16);
                piecePayload[6] = (byte)(begin >> 8);
                piecePayload[7] = (byte)begin;

                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Piece, Payload = piecePayload, PayloadLength = payloadSize });
                connection.BytesUploaded += length;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(piecePayload);
            }
        }
        finally
        {
            if (connection.PendingRequestCount > 0)
            {
                connection.PendingRequestCount--;
            }
        }
    }
}
