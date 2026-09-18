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
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Transport;

public interface IUtpManager
{
    IUtpConnection CreateConnection();
    bool IsEnabled { get; }
    bool TcpFallbackEnabled { get; }
    int ActiveConnections { get; }
    event Action<IUtpConnection> OnConnectionAccepted;
}

public class UtpManager : BackgroundService, IUtpManager
{
    private readonly IConfigService _configService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, IUtpConnection> _activeConnections = new();
    private UdpClient _listener;

    public event Action<IUtpConnection> OnConnectionAccepted;

    public UdpClient Listener
    {
        get => _listener;
        set => _listener = value;
    }

    public int ActiveConnections => _activeConnections.Values.Distinct().Count(c => c.IsConnected);
    public bool IsEnabled => _configService.UtpEnabled;
    public bool TcpFallbackEnabled => _configService.TcpFallback;

    public UtpManager(IConfigService configService)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public IUtpConnection CreateConnection()
    {
        if (!_configService.UtpEnabled)
        {
            throw new InvalidOperationException("uTP is disabled");
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

            return;
        }

        using (listener)
        {
            _listener = listener;
            _logger.Info("uTP manager listening on port {0}", listenPort);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = await listener.ReceiveAsync(stoppingToken);
                        HandleIncoming(result.Buffer, result.RemoteEndPoint);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "uTP receive error");
                    }
                }
            }
            finally
            {
                _listener = null;
            }
        }
    }

    private void HandleIncoming(byte[] data, IPEndPoint sender)
    {
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
