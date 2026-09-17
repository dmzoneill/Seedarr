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
}

public class UtpManager : BackgroundService, IUtpManager
{
    private readonly IConfigService _configService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, IUtpConnection> _activeConnections = new();
    private UdpClient _listener;

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
        var recvKey = connection.ReceiveId.ToString();
        _activeConnections[recvKey] = connection;

        connection.OnConnecting = (conn, endpoint) =>
        {
            var endpointKey = $"{endpoint}_{connection.ReceiveId}";
            _activeConnections[endpointKey] = connection;
        };

        connection.OnClosed = _ =>
        {
            _activeConnections.TryRemove(recvKey, out _);
            if (connection.RemoteEndPoint != null)
            {
                _activeConnections.TryRemove($"{connection.RemoteEndPoint}_{connection.ReceiveId}", out _);
            }
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

            if (_activeConnections.TryGetValue(synKey, out var existingConn) && existingConn is UtpConnection existingUtp)
            {
                existingUtp.HandleIncomingPacket(data, sender);
                return;
            }

            var conn = new UtpConnection(_listener, sendId, sender, _configService.TransportConnectionTimeoutSeconds);
            _activeConnections[synKey] = conn;
            _activeConnections[dataKey] = conn;
            conn.OnClosed = _ =>
            {
                _activeConnections.TryRemove(synKey, out _);
                _activeConnections.TryRemove(dataKey, out _);
            };

            conn.HandleIncomingPacket(data, sender);
            return;
        }

        var matchKey = $"{sender}_{connectionId}";
        if (!_activeConnections.TryGetValue(matchKey, out var activeConn))
        {
            _activeConnections.TryGetValue(connectionId.ToString(), out activeConn);
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
