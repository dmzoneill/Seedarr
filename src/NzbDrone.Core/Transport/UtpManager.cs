using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Transport;

public interface IUtpManager
{
    IUtpConnection CreateConnection();
    bool IsEnabled { get; }
    bool TcpFallbackEnabled { get; }
    int ActiveConnections { get; }
    event Action<IUtpConnection> OnConnectionAccepted;
    void TeardownActiveConnections();
    void StopListener();
}

public class UtpManager : BackgroundService, IUtpManager, IHandle<VpnKillSwitchTriggeredEvent>, IHandle<VpnInterfaceRestoredEvent>, IHandle<VpnRestoredEvent>
{
    private readonly IConfigService _configService;
    private readonly IVpnKillSwitchService _vpnKillSwitchService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, IUtpConnection> _activeConnections = new();
    private readonly object _listenerLock = new();
    private readonly SemaphoreSlim _rebindSignal = new(0, 1);
    private long _lifetimeSequence;
    private CancellationTokenSource _listenerCts;
    private UdpClient _listener;
    private bool _isVpnDropped;

    public event Action<IUtpConnection> OnConnectionAccepted;

    public UdpClient Listener
    {
        get
        {
            lock (_listenerLock)
            {
                return _listener;
            }
        }
        set
        {
            lock (_listenerLock)
            {
                _listener = value;
            }
        }
    }

    public int ActiveConnections => _activeConnections.Values.Distinct().Count(c => c.IsConnected);
    public bool IsEnabled => _configService.UtpEnabled;
    public bool TcpFallbackEnabled => _configService.TcpFallback;

    public UtpManager(
        IConfigService configService,
        IVpnKillSwitchService vpnKillSwitchService = null)
    {
        _configService = configService;
        _vpnKillSwitchService = vpnKillSwitchService;
        _logger = LogManager.GetCurrentClassLogger();

        if (_vpnKillSwitchService != null)
        {
            _vpnKillSwitchService.VpnDropped += OnVpnDropped;
            _vpnKillSwitchService.VpnRestored += OnVpnRestored;
        }
    }

    public bool IsVpnFailClosed()
    {
        if (_vpnKillSwitchService != null && _vpnKillSwitchService.IsFailClosedActive)
        {
            return true;
        }

        return _isVpnDropped;
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        OnVpnDropped(message?.InterfaceName);
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        OnVpnRestored(message?.InterfaceName);
    }

    public void Handle(VpnRestoredEvent message)
    {
        OnVpnRestored(message?.InterfaceName);
    }

    private void OnVpnDropped(string iface)
    {
        _logger.Warn("VPN kill switch engaged (interface '{0}' dropped). Stopping uTP listener and terminating active uTP streams.", iface);
        _isVpnDropped = true;

        TeardownActiveConnections();
        StopListener();
    }

    private void OnVpnRestored(string iface)
    {
        if (!_isVpnDropped && (_vpnKillSwitchService == null || !_vpnKillSwitchService.IsFailClosedActive))
        {
            return;
        }

        _logger.Info("VPN interface '{0}' restored. Resuming uTP listener.", iface);
        _isVpnDropped = false;
        TriggerRebind();
    }

    public void TeardownActiveConnections()
    {
        _logger.Warn("Tearing down all active uTP streams.");
        var connections = _activeConnections.Values.Distinct().ToList();
        _activeConnections.Clear();
        foreach (var conn in connections)
        {
            try
            {
                conn.Close();
                conn.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error closing active uTP connection during VPN outage");
            }
        }
    }

    public void StopListener()
    {
        lock (_listenerLock)
        {
            if (_listenerCts != null)
            {
                try
                {
                    if (!_listenerCts.IsCancellationRequested)
                    {
                        _listenerCts.Cancel();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Trace(ex, "Failed to cancel uTP listener CTS");
                }

                _listenerCts.Dispose();
                _listenerCts = null;
            }

            if (_listener != null)
            {
                try
                {
                    _listener.Close();
                    _listener.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Trace(ex, "Failed to close uTP listener");
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
        catch (Exception ex)
        {
            _logger.Trace(ex, "Failed to release uTP rebind signal");
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

    public override void Dispose()
    {
        if (_vpnKillSwitchService != null)
        {
            _vpnKillSwitchService.VpnDropped -= OnVpnDropped;
            _vpnKillSwitchService.VpnRestored -= OnVpnRestored;
        }

        StopListener();
        _rebindSignal?.Dispose();
        base.Dispose();
    }

    public IUtpConnection CreateConnection()
    {
        if (!_configService.UtpEnabled)
        {
            throw new InvalidOperationException("uTP is disabled");
        }

        if (IsVpnFailClosed())
        {
            throw new InvalidOperationException("uTP connection creation blocked: VPN kill switch fail-closed state active");
        }

        var timeoutSeconds = _configService.TransportConnectionTimeoutSeconds;
        var connection = new UtpConnection(timeoutSeconds, _configService.BindInterface);
        var registeredKeys = new ConcurrentBag<string>();

        void RegisterKey(string key)
        {
            _activeConnections[key] = connection;
            registeredKeys.Add(key);
        }

        RegisterKey(NextLifetimeKey());

        var prevConnecting = connection.OnConnecting;
        connection.OnConnecting = (conn, endpoint) =>
        {
            RegisterEndpointKeys(RegisterKey, endpoint, connection);
            prevConnecting?.Invoke(conn, endpoint);
        };

        var prevConnected = connection.OnConnected;
        connection.OnConnected = conn =>
        {
            RegisterEndpointKeys(RegisterKey, connection.RemoteEndPoint, connection);
            prevConnected?.Invoke(conn);
        };

        var prevClosed = connection.OnClosed;
        connection.OnClosed = c =>
        {
            foreach (var key in registeredKeys)
            {
                _activeConnections.TryRemove(key, out _);
            }

            prevClosed?.Invoke(c);
        };

        return connection;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configService.UtpEnabled)
        {
            _logger.Info("uTP is disabled, skipping listener");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (IsVpnFailClosed())
            {
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

            var listenPort = _configService.ListeningPort;
            UdpClient listener;

            try
            {
                listener = new UdpClient();
                listener.Client.BindToNetworkInterface(_configService.BindInterface, IPAddress.Any, listenPort);
                if (!listener.Client.IsBound)
                {
                    listener.Dispose();
                    throw new SocketException((int)SocketError.AddressAlreadyInUse);
                }
            }
            catch (SocketException ex)
            {
                _logger.Warn(ex, "uTP manager failed to bind port {0}, skipping", listenPort);

                if (_configService.TcpFallback)
                {
                    _logger.Info("TCP fallback is enabled, continuing without uTP");
                }

                return;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "uTP manager failed to initialize listener on port {0}", listenPort);
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

            CancellationToken linkedToken;
            lock (_listenerLock)
            {
                if (IsVpnFailClosed() || stoppingToken.IsCancellationRequested)
                {
                    try { listener.Dispose(); } catch { /* best-effort dispose while fail-closed / stopping */ }
                    continue;
                }

                _listener = listener;
                _listenerCts?.Dispose();
                _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                linkedToken = _listenerCts.Token;
            }

            _logger.Info("uTP manager listening on port {0}", listenPort);

            try
            {
                while (!linkedToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = await listener.ReceiveAsync(linkedToken);

                        if (IsVpnFailClosed())
                        {
                            break;
                        }

                        HandleIncoming(result.Buffer, result.RemoteEndPoint);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (SocketException) when (linkedToken.IsCancellationRequested || IsVpnFailClosed())
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (linkedToken.IsCancellationRequested || IsVpnFailClosed())
                        {
                            break;
                        }

                        _logger.Debug(ex, "uTP receive error");
                    }
                }
            }
            finally
            {
                lock (_listenerLock)
                {
                    if (ReferenceEquals(_listener, listener))
                    {
                        _listenerCts?.Dispose();
                        _listenerCts = null;
                        _listener = null;
                    }

                    try
                    {
                        listener.Dispose();
                    }
                    catch
                    {
                        // Best-effort dispose after listener teardown; ignore secondary failures.
                    }
                }
            }
        }
    }

    private void HandleIncoming(byte[] data, IPEndPoint sender)
    {
        if (IsVpnFailClosed())
        {
            return;
        }

        if (data.Length < 20)
        {
            return;
        }

        var version = (byte)(data[0] & 0x0F);
        var type = (UtpPacketType)(data[0] >> 4);

        if (version != 1 || (byte)type > 4)
        {
            return;
        }

        var connectionId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2, 2));

        if (type == UtpPacketType.Syn)
        {
            _logger.Debug("uTP SYN from {0}, connection {1}", sender, connectionId);
            var sendId = connectionId;
            var receiveId = (ushort)(connectionId + 1);
            var synKey = DemuxKey(sender, sendId);
            var dataKey = DemuxKey(sender, receiveId);

            if ((_activeConnections.TryGetValue(synKey, out var existingConn) ||
                _activeConnections.TryGetValue(dataKey, out existingConn)) &&
                existingConn is UtpConnection existingUtp)
            {
                existingUtp.HandleIncomingPacket(data, sender);
                return;
            }

            var handler = OnConnectionAccepted;
            if (handler == null)
            {
                _logger.Debug("Ignoring uTP SYN from {0}; no consumer is registered", sender);
                return;
            }

            var conn = new UtpConnection(_listener, sendId, sender, _configService.TransportConnectionTimeoutSeconds, _configService.BindInterface);
            var lifetimeKey = NextLifetimeKey();
            _activeConnections[lifetimeKey] = conn;
            _activeConnections[synKey] = conn;
            _activeConnections[dataKey] = conn;
            var prevClosed = conn.OnClosed;
            conn.OnClosed = c =>
            {
                _activeConnections.TryRemove(lifetimeKey, out _);
                _activeConnections.TryRemove(synKey, out _);
                _activeConnections.TryRemove(dataKey, out _);
                prevClosed?.Invoke(c);
            };

            conn.HandleIncomingPacket(data, sender);

            try
            {
                handler(conn);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error dispatching accepted uTP connection from {0}", sender);
                conn.Abort();
            }

            return;
        }

        var activeConn = FindConnection(sender, connectionId);

        if (activeConn is UtpConnection matchedUtp)
        {
            matchedUtp.HandleIncomingPacket(data, sender);
            return;
        }

        if (type == UtpPacketType.Data)
        {
            _logger.Debug("uTP DATA from {0}, connection {1}", sender, connectionId);
        }
        else if (type == UtpPacketType.Fin)
        {
            _logger.Debug("uTP FIN from {0}, connection {1}", sender, connectionId);
        }
        else if (type == UtpPacketType.State)
        {
            _logger.Debug("uTP STATE from {0}, connection {1}", sender, connectionId);
        }
        else if (type == UtpPacketType.Reset)
        {
            _logger.Debug("uTP RESET from {0}, connection {1}", sender, connectionId);
        }
    }

    private string NextLifetimeKey()
    {
        var id = Interlocked.Increment(ref _lifetimeSequence);
        return "lifetime:" + id;
    }

    private static string DemuxKey(IPEndPoint endpoint, ushort connectionId)
    {
        return endpoint + "_" + connectionId;
    }

    private static void RegisterEndpointKeys(Action<string> registerKey, IPEndPoint endpoint, UtpConnection connection)
    {
        if (registerKey == null || endpoint == null || connection == null)
        {
            return;
        }

        registerKey(DemuxKey(endpoint, connection.ReceiveId));
        registerKey(DemuxKey(endpoint, connection.SendId));
        registerKey(DemuxKey(endpoint, (ushort)(connection.ReceiveId + 1)));
    }

    private IUtpConnection FindConnection(IPEndPoint sender, ushort connectionId)
    {
        if (sender == null)
        {
            return null;
        }

        if (_activeConnections.TryGetValue(DemuxKey(sender, connectionId), out var activeConn))
        {
            return activeConn;
        }

        if (_activeConnections.TryGetValue(DemuxKey(sender, (ushort)(connectionId - 1)), out activeConn))
        {
            return activeConn;
        }

        if (_activeConnections.TryGetValue(DemuxKey(sender, (ushort)(connectionId + 1)), out activeConn))
        {
            return activeConn;
        }

        return null;
    }
}
