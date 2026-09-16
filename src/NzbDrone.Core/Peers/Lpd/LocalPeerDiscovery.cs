using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Peers.Lpd;

public class LocalPeerDiscovery : BackgroundService, IHandle<ConfigSavedEvent>
{
    private const string MulticastAddress = "239.192.152.143";
    private const int MulticastPort = 6771;
    private const int PeerPort = 6881;

    protected virtual int AnnounceIntervalSeconds => 300;

    private readonly IConfigService _configService;
    private readonly ITorrentService _torrentService;
    private readonly IPeerDiscoveryService _peerDiscovery;
    private readonly Logger _logger;
    private readonly object _stateLock = new();

    private UdpClient _client;
    private CancellationTokenSource _workerCts;
    private Task _workerTask;
    private bool _wasEnabled;
    private CancellationToken _stoppingToken;

    public bool IsRunning => _client != null;

    public LocalPeerDiscovery(IConfigService configService, ITorrentService torrentService, IPeerDiscoveryService peerDiscovery)
    {
        _configService = configService;
        _torrentService = torrentService;
        _peerDiscovery = peerDiscovery;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ConfigSavedEvent message)
    {
        lock (_stateLock)
        {
            var isEnabled = _configService.EnableLpd;

            if (isEnabled)
            {
                if (_workerTask == null || _workerTask.IsCompleted)
                {
                    _logger.Info("Local Peer Discovery enabled via configuration change, starting service");
                    StartLpd();
                }
            }
            else if (_wasEnabled || _workerTask != null)
            {
                _logger.Info("Local Peer Discovery disabled via configuration change, stopping service");
                StopLpd();
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        lock (_stateLock)
        {
            _wasEnabled = _configService.EnableLpd;
        }

        if (_configService.EnableLpd)
        {
            StartLpd();
        }
        else
        {
            _logger.Info("Local Peer Discovery disabled via configuration");
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            StopLpd();
            if (_workerTask != null)
            {
                try
                {
                    await _workerTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private void StartLpd()
    {
        lock (_stateLock)
        {
            if (_workerTask != null && !_workerTask.IsCompleted)
            {
                return;
            }

            UdpClient client;

            try
            {
                client = new UdpClient(MulticastPort);
                client.JoinMulticastGroup(IPAddress.Parse(MulticastAddress));
            }
            catch (SocketException ex)
            {
                _logger.Warn(ex, "Local Peer Discovery failed to join multicast group, skipping");
                return;
            }

            _client = client;
            _wasEnabled = true;
            _logger.Info("Local Peer Discovery (BEP 14) started on {0}:{1}", MulticastAddress, MulticastPort);

            _workerCts = _stoppingToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken)
                : new CancellationTokenSource();

            var token = _workerCts.Token;
            _workerTask = Task.Run(async () =>
            {
                var listenTask = ListenForPeers(client, token);
                var announceTask = AnnounceLoop(token);

                await Task.WhenAny(listenTask, announceTask);

                try
                {
                    await Task.WhenAll(listenTask, announceTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            });
        }
    }

    private void StopLpd()
    {
        lock (_stateLock)
        {
            _wasEnabled = false;

            if (_workerCts != null)
            {
                try
                {
                    _workerCts.Cancel();
                }
                catch (Exception)
                {
                }

                _workerCts.Dispose();
                _workerCts = null;
            }

            if (_client != null)
            {
                try
                {
                    _client.DropMulticastGroup(IPAddress.Parse(MulticastAddress));
                }
                catch (Exception)
                {
                }

                try
                {
                    _client.Close();
                    _client.Dispose();
                }
                catch (Exception)
                {
                }

                _client = null;
                _logger.Info("Local Peer Discovery stopped");
            }

            _workerTask = null;
        }
    }

    public override void Dispose()
    {
        StopLpd();
        base.Dispose();
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
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.Debug(ex, "LPD receive error");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task AnnounceLoop(CancellationToken stoppingToken)
    {
        using var sender = new UdpClient();
        sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
        var endpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(AnnounceIntervalSeconds), stoppingToken);

                List<Torrent> torrents;
                try
                {
                    torrents = _torrentService.GetAll();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "LPD: failed to retrieve torrents");
                    continue;
                }

                foreach (var torrent in torrents)
                {
                    if (string.IsNullOrEmpty(torrent.InfoHash))
                    {
                        continue;
                    }

                    var port = _configService.ListeningPort > 0 ? _configService.ListeningPort : PeerPort;
                    var data = BuildAnnouncement(torrent.InfoHash, port);
                    await sender.SendAsync(data, endpoint, stoppingToken);
                    _logger.Debug("LPD: announced {0}", torrent.InfoHash);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "LPD announce error");
            }
        }
    }

    public static byte[] BuildAnnouncement(string infoHash, int port)
    {
        var message = $"BT-SEARCH * HTTP/1.1\r\nHost: {MulticastAddress}:{MulticastPort}\r\nPort: {port}\r\nInfohash: {infoHash}\r\n\r\n\r\n";
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
            _peerDiscovery.AddPeers(infoHash, new[] { new TrackerPeer { Ip = sender.Address.ToString(), Port = port } }, "lpd");
        }
    }
}
