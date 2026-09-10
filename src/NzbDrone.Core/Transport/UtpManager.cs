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

    public int ActiveConnections => _activeConnections.Values.Count(c => c.IsConnected);
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
        var connection = new UtpConnection(timeoutSeconds);
        var key = Guid.NewGuid().ToString();
        _activeConnections[key] = connection;
        connection.OnClosed = _ => _activeConnections.TryRemove(key, out _);

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
            listener = new UdpClient(listenPort);
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
            _logger.Info("uTP manager listening on port {0}", listenPort);

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
            var key = $"{sender}_{connectionId}";
            var conn = new UtpConnection(null, (ushort)(connectionId + 1), sender, _configService.TransportConnectionTimeoutSeconds);
            _activeConnections[key] = conn;
            conn.OnClosed = _ => _activeConnections.TryRemove(key, out _);
            conn.HandleIncomingPacket(data, sender);
            return;
        }

        var matchKey = $"{sender}_{connectionId}";
        if (_activeConnections.TryGetValue(matchKey, out var activeConn) && activeConn is UtpConnection matchedUtp)
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
