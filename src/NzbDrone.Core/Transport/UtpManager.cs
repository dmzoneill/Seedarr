using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Transport;

public interface IUtpManager
{
    IUtpConnection CreateConnection();
    int ActiveConnections { get; }
}

public class UtpManager : BackgroundService, IUtpManager
{
    private const int ListenPort = 6881;
    private const int MaxConnections = 100;

    private readonly ConcurrentDictionary<ushort, UtpConnection> _connections = new();
    private readonly Logger _logger;

    public int ActiveConnections => _connections.Count;

    public UtpManager()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public IUtpConnection CreateConnection()
    {
        if (_connections.Count >= MaxConnections)
        {
            throw new InvalidOperationException("Maximum uTP connections reached");
        }

        return new UtpConnection();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var listener = new UdpClient(ListenPort);
        _logger.Info("uTP manager listening on port {0}", ListenPort);

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
