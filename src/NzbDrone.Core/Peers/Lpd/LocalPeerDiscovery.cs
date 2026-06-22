using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Peers.Lpd;

public class LocalPeerDiscovery : BackgroundService
{
    private const string MulticastAddress = "239.192.152.143";
    private const int MulticastPort = 6771;
    private const int AnnounceIntervalSeconds = 300;

    private readonly Logger _logger;

    public LocalPeerDiscovery()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UdpClient client;

        try
        {
            client = new UdpClient();
            client.JoinMulticastGroup(IPAddress.Parse(MulticastAddress));
        }
        catch (SocketException ex)
        {
            _logger.Warn(ex, "Local Peer Discovery failed to join multicast group, skipping");
            return;
        }

        using (client)
        {
            _logger.Info("Local Peer Discovery (BEP 14) started on {0}:{1}", MulticastAddress, MulticastPort);

            var listenTask = ListenForPeers(client, stoppingToken);
            var announceTask = AnnounceLoop(stoppingToken);

            await Task.WhenAny(listenTask, announceTask);
        }
    }

    private async Task ListenForPeers(UdpClient client, CancellationToken stoppingToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Any, MulticastPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(stoppingToken);
                var message = Encoding.ASCII.GetString(result.Buffer);
                ParseAnnouncement(message, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "LPD receive error");
            }
        }
    }

    private async Task AnnounceLoop(CancellationToken stoppingToken)
    {
        using var sender = new UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(AnnounceIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public static byte[] BuildAnnouncement(string infoHash, int port)
    {
        var message = $"BT-SEARCH * HTTP/1.1\r\n" +
                      $"Host: {MulticastAddress}:{MulticastPort}\r\n" +
                      $"Port: {port}\r\n" +
                      $"Infohash: {infoHash}\r\n" +
                      $"\r\n\r\n";
        return Encoding.ASCII.GetBytes(message);
    }

    private void ParseAnnouncement(string message, IPEndPoint sender)
    {
        if (!message.StartsWith("BT-SEARCH"))
        {
            return;
        }

        string infoHash = null;
        var port = 0;

        foreach (var line in message.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Infohash:", StringComparison.OrdinalIgnoreCase))
            {
                infoHash = line[9..].Trim();
            }
            else if (line.StartsWith("Port:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line[5..].Trim(), out port);
            }
        }

        if (infoHash != null && port > 0)
        {
            _logger.Debug("LPD: peer {0}:{1} for {2}", sender.Address, port, infoHash);
        }
    }
}
