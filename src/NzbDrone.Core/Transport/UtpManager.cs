using System;
using System.Collections.Concurrent;
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
    private const int MaxConnections = 100;

    private readonly IConfigService _configService;
    private readonly ConcurrentDictionary<ushort, UtpConnection> _connections = new();
    private readonly Logger _logger;

    public int ActiveConnections => _connections.Count;
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

        if (_connections.Count >= MaxConnections)
        {
            throw new InvalidOperationException("Maximum uTP connections reached");
        }

        var timeoutSeconds = _configService.TransportConnectionTimeoutSeconds;
        return new UtpConnection(timeoutSeconds);
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

        var type = (UtpPacketType)(data[0] >> 4);
        var connectionId = (ushort)((data[2] << 8) | data[3]);

        if (type == UtpPacketType.Syn)
        {
            _logger.Debug("uTP SYN from {0}, connection {1}", sender, connectionId);
        }
    }
}
