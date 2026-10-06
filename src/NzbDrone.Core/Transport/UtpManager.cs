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
            if (_listenerCts != null && !_listenerCts.IsCancellationRequested)
            {
                try
                {
                    _listenerCts.Cancel();
                }
                catch (Exception ex)
                {
                    _logger.Trace(ex, "Failed to cancel uTP listener CTS");
                }
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

        RegisterKey(connection.ReceiveId.ToString());
        RegisterKey(connection.SendId.ToString());
        RegisterKey(((ushort)(connection.ReceiveId + 1)).ToString());

        var prevConnecting = connection.OnConnecting;
        connection.OnConnecting = (conn, endpoint) =>
        {
            RegisterKey($"{endpoint}_{connection.ReceiveId}");
            RegisterKey($"{endpoint}_{connection.SendId}");
            RegisterKey($"{endpoint}_{(ushort)(connection.ReceiveId + 1)}");
            prevConnecting?.Invoke(conn, endpoint);
        };

        var prevConnected = connection.OnConnected;
        connection.OnConnected = conn =>
        {
            if (connection.RemoteEndPoint != null)
            {
                RegisterKey($"{connection.RemoteEndPoint}_{connection.ReceiveId}");
                RegisterKey($"{connection.RemoteEndPoint}_{connection.SendId}");
                RegisterKey($"{connection.RemoteEndPoint}_{(ushort)(connection.ReceiveId + 1)}");
            }

            RegisterKey(connection.ReceiveId.ToString());
            RegisterKey(connection.SendId.ToString());
            RegisterKey(((ushort)(connection.ReceiveId + 1)).ToString());
            prevConnected?.Invoke(conn);
        };

        var prevClosed = connection.OnClosed;
        connection.OnClosed = c =>
        {
            foreach (var key in registeredKeys)
            {
                _activeConnections.TryRemove(key, out _);
            }

            _activeConnections.TryRemove(connection.ReceiveId.ToString(), out _);
            _activeConnections.TryRemove(connection.SendId.ToString(), out _);
            _activeConnections.TryRemove(((ushort)(connection.ReceiveId + 1)).ToString(), out _);
            if (connection.RemoteEndPoint != null)
            {
                _activeConnections.TryRemove($"{connection.RemoteEndPoint}_{connection.ReceiveId}", out _);
                _activeConnections.TryRemove($"{connection.RemoteEndPoint}_{connection.SendId}", out _);
                _activeConnections.TryRemove($"{connection.RemoteEndPoint}_{(ushort)(connection.ReceiveId + 1)}", out _);
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
            }
            catch (SocketException ex)
            {
                _logger.Warn(ex, "uTP manager failed to bind port {0}, skipping", listenPort);

                if (_configService.TcpFallback)
                {
                    _logger.Info("TCP fallback is enabled, continuing without uTP");
                }

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
                    try { listener.Dispose(); } catch { }
                    continue;
                }

                _listener = listener;
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
                        _listener = null;
                    }

                    try
                    {
                        listener.Dispose();
                    }
                    catch
                    {
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
            var synKey = $"{sender}_{sendId}";
            var dataKey = $"{sender}_{receiveId}";
            var sendIdKey = sendId.ToString();
            var receiveIdKey = receiveId.ToString();

            if ((_activeConnections.TryGetValue(synKey, out var existingConn) ||
                _activeConnections.TryGetValue(dataKey, out existingConn)) &&
                existingConn is UtpConnection existingUtp)
            {
                existingUtp.HandleIncomingPacket(data, sender);
                return;
            }

            var conn = new UtpConnection(_listener, sendId, sender, _configService.TransportConnectionTimeoutSeconds, _configService.BindInterface);
            _activeConnections[synKey] = conn;
            _activeConnections[dataKey] = conn;
            _activeConnections[sendIdKey] = conn;
            _activeConnections[receiveIdKey] = conn;
            var prevClosed = conn.OnClosed;
            conn.OnClosed = c =>
            {
                _activeConnections.TryRemove(synKey, out _);
                _activeConnections.TryRemove(dataKey, out _);
                _activeConnections.TryRemove(sendIdKey, out _);
                _activeConnections.TryRemove(receiveIdKey, out _);
                prevClosed?.Invoke(c);
            };

            conn.HandleIncomingPacket(data, sender);

            try
            {
                OnConnectionAccepted?.Invoke(conn);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error dispatching accepted uTP connection from {0}", sender);
            }

            return;
        }

        var matchKey = $"{sender}_{connectionId}";
        if (!_activeConnections.TryGetValue(matchKey, out var activeConn))
        {
            if (!_activeConnections.TryGetValue($"{sender}_{(ushort)(connectionId - 1)}", out activeConn))
            {
                if (!_activeConnections.TryGetValue($"{sender}_{(ushort)(connectionId + 1)}", out activeConn))
                {
                    if (!_activeConnections.TryGetValue(connectionId.ToString(), out activeConn))
                    {
                        if (!_activeConnections.TryGetValue(((ushort)(connectionId - 1)).ToString(), out activeConn))
                        {
                            _activeConnections.TryGetValue(((ushort)(connectionId + 1)).ToString(), out activeConn);
                        }
                    }
                }
            }
        }

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
}
