using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    private const string MulticastAddressV6 = "ff15::efc0:988f";
    private const int MulticastPort = 6771;
    private const int PeerPort = 6881;

    private static readonly Regex InfoHashRegex = new("^[0-9a-fA-F]{40}$|^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    protected virtual int AnnounceIntervalSeconds => 300;
    protected virtual int RetryDelayMs => 5000;

    private int? _interAnnounceDelayMs;

    protected internal virtual int InterAnnounceDelayMs
    {
        get => _interAnnounceDelayMs ?? 50;
        set => _interAnnounceDelayMs = value;
    }

    private readonly IConfigService _configService;
    private readonly ITorrentService _torrentService;
    private readonly IPeerDiscoveryService _peerDiscovery;
    private readonly Logger _logger;
    private readonly object _stateLock = new();

    private string _clientCookie = RandomNumberGenerator.GetHexString(8).ToLowerInvariant();
    private UdpClient _client;
    private UdpClient _clientV6;
    private CancellationTokenSource _workerCts;
    private Task _workerTask;
    private bool _wasEnabled;
    private CancellationToken _stoppingToken;

    public bool IsRunning => _client != null || _clientV6 != null;

    internal string ClientCookie
    {
        get => _clientCookie;
        set => _clientCookie = value;
    }

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

        if (!_configService.EnableLpd)
        {
            _logger.Info("Local Peer Discovery disabled via configuration");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_configService.EnableLpd)
                {
                    lock (_stateLock)
                    {
                        if (_client == null && _clientV6 == null && (_workerTask == null || _workerTask.IsCompleted))
                        {
                            StartLpd();
                        }
                    }
                }

                if ((_client != null || _clientV6 != null) && _workerTask != null)
                {
                    await Task.WhenAny(_workerTask, Task.Delay(Timeout.Infinite, stoppingToken));
                    if (_workerTask.IsCompleted)
                    {
                        StopLpd();
                    }
                }
                else
                {
                    await Task.Delay(RetryDelayMs, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Local Peer Discovery loop error");
                try
                {
                    await Task.Delay(RetryDelayMs, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

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

    private void StartLpd()
    {
        lock (_stateLock)
        {
            if (_workerTask != null && !_workerTask.IsCompleted)
            {
                return;
            }

            UdpClient client = null;
            Socket socket = null;

            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
                client = new UdpClient { Client = socket };
            }
            catch (SocketException ex)
            {
                socket?.Dispose();
                _logger.Warn(ex, "Local Peer Discovery failed to bind multicast port {0}, skipping", MulticastPort);
            }
            catch (Exception ex)
            {
                socket?.Dispose();
                _logger.Warn(ex, "Local Peer Discovery unexpected error binding multicast port {0}", MulticastPort);
            }

            if (client != null)
            {
                var multicastAddress = IPAddress.Parse(MulticastAddress);
                var joinedAny = false;

                try
                {
                    client.JoinMulticastGroup(multicastAddress);
                    joinedAny = true;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Local Peer Discovery failed to join default multicast group");
                }

                var activeInterfaces = GetActiveNetworkInterfaces();
                foreach (var nic in activeInterfaces)
                {
                    var ipProps = nic.GetIPProperties();
                    if (ipProps == null)
                    {
                        continue;
                    }

                    foreach (var unicast in ipProps.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            try
                            {
                                client.JoinMulticastGroup(multicastAddress, unicast.Address);
                                joinedAny = true;
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug(ex, "Local Peer Discovery failed to join multicast group on {0} ({1})", nic.Name, unicast.Address);
                            }
                        }
                    }
                }

                if (!joinedAny)
                {
                    _logger.Warn("Local Peer Discovery failed to join multicast group on any interface, skipping");
                    try
                    {
                        client.Close();
                        client.Dispose();
                    }
                    catch (Exception)
                    {
                    }

                    client = null;
                }
            }

            UdpClient clientV6 = null;
            if (Socket.OSSupportsIPv6)
            {
                Socket socketV6 = null;
                try
                {
                    socketV6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                    socketV6.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    try
                    {
                        socketV6.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
                    }
                    catch
                    {
                    }

                    socketV6.Bind(new IPEndPoint(IPAddress.IPv6Any, MulticastPort));
                    clientV6 = new UdpClient { Client = socketV6 };

                    var multicastAddressV6 = IPAddress.Parse(MulticastAddressV6);
                    var joinedAnyV6 = false;

                    try
                    {
                        clientV6.JoinMulticastGroup(multicastAddressV6);
                        joinedAnyV6 = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Local Peer Discovery failed to join default IPv6 multicast group");
                    }

                    var activeInterfaces = GetActiveNetworkInterfaces();
                    foreach (var nic in activeInterfaces)
                    {
                        try
                        {
                            var ipProps = nic.GetIPProperties();
                            var ipv6Props = ipProps?.GetIPv6Properties();
                            if (ipv6Props != null)
                            {
                                clientV6.JoinMulticastGroup(ipv6Props.Index, multicastAddressV6);
                                joinedAnyV6 = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Local Peer Discovery failed to join IPv6 multicast group on {0}", nic.Name);
                        }
                    }

                    if (!joinedAnyV6)
                    {
                        _logger.Debug("Local Peer Discovery failed to join IPv6 multicast group on any interface");
                        try
                        {
                            clientV6.Close();
                            clientV6.Dispose();
                        }
                        catch (Exception)
                        {
                        }

                        clientV6 = null;
                    }
                }
                catch (Exception ex)
                {
                    socketV6?.Dispose();
                    _logger.Debug(ex, "Local Peer Discovery IPv6 multicast setup not available or failed");
                    if (clientV6 != null)
                    {
                        try
                        {
                            clientV6.Close();
                            clientV6.Dispose();
                        }
                        catch (Exception)
                        {
                        }

                        clientV6 = null;
                    }
                }
            }

            if (client == null && clientV6 == null)
            {
                return;
            }

            _client = client;
            _clientV6 = clientV6;
            _wasEnabled = true;
            _logger.Info("Local Peer Discovery (BEP 14) started on {0}:{1}", MulticastAddress, MulticastPort);

            _workerCts = _stoppingToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken)
                : new CancellationTokenSource();

            var token = _workerCts.Token;
            _workerTask = Task.Run(async () =>
            {
                var tasks = new List<Task>();
                if (client != null)
                {
                    tasks.Add(ListenForPeers(client, token));
                }

                if (clientV6 != null)
                {
                    tasks.Add(ListenForPeers(clientV6, token));
                }

                tasks.Add(AnnounceLoop(token));

                await Task.WhenAny(tasks);

                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
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
            }

            if (_clientV6 != null)
            {
                try
                {
                    _clientV6.DropMulticastGroup(IPAddress.Parse(MulticastAddressV6));
                }
                catch (Exception)
                {
                }

                try
                {
                    _clientV6.Close();
                    _clientV6.Dispose();
                }
                catch (Exception)
                {
                }

                _clientV6 = null;
            }

            _workerTask = null;
            _logger.Info("Local Peer Discovery stopped");
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

                var activeTorrents = (torrents ?? Enumerable.Empty<Torrent>())
                    .Where(t => t != null && !t.IsPrivate && !string.IsNullOrEmpty(t.InfoHash) && (t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding))
                    .ToList();

                for (var i = 0; i < activeTorrents.Count; i++)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    var torrent = activeTorrents[i];
                    var port = _configService.ListeningPort > 0 ? _configService.ListeningPort : PeerPort;
                    var data = BuildAnnouncement(torrent.InfoHash, port, _clientCookie);
                    await SendAnnouncementAsync(sender, data, endpoint, stoppingToken);
                    _logger.Debug("LPD: announced {0}", torrent.InfoHash);

                    if (i < activeTorrents.Count - 1 && InterAnnounceDelayMs > 0)
                    {
                        await Task.Delay(InterAnnounceDelayMs, stoppingToken);
                    }
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

    public static byte[] BuildAnnouncement(string infoHash, int port, string cookie = null)
    {
        return BuildAnnouncement(infoHash, port, cookie, null);
    }

    public static byte[] BuildAnnouncement(string infoHash, int port, string cookie, string host)
    {
        var cookieValue = cookie ?? RandomNumberGenerator.GetHexString(8).ToLowerInvariant();
        var hostValue = string.IsNullOrEmpty(host) ? $"{MulticastAddress}:{MulticastPort}" : host;
        var message = $"BT-SEARCH * HTTP/1.1\r\nHost: {hostValue}\r\nPort: {port}\r\nInfohash: {infoHash}\r\ncookie: {cookieValue}\r\n\r\n\r\n";
        return Encoding.ASCII.GetBytes(message);
    }

    protected virtual async Task SendAnnouncementAsync(UdpClient client, byte[] data, IPEndPoint endpoint, CancellationToken stoppingToken)
    {
        var sentAny = false;
        var activeInterfaces = GetActiveNetworkInterfaces();

        foreach (var nic in activeInterfaces)
        {
            var ipProps = nic.GetIPProperties();
            if (ipProps == null)
            {
                continue;
            }

            foreach (var unicast in ipProps.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                try
                {
                    using var ifSender = new UdpClient(new IPEndPoint(unicast.Address, 0));
                    ifSender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                    try
                    {
                        ifSender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, unicast.Address.GetAddressBytes());
                    }
                    catch
                    {
                    }

                    await ifSender.SendAsync(data, endpoint, stoppingToken);
                    sentAny = true;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "LPD: failed to send announcement on interface {0} ({1})", nic.Name, unicast.Address);
                }
            }
        }

        if (!sentAny)
        {
            await client.SendAsync(data, endpoint, stoppingToken);
        }
    }

    private void ParseAnnouncement(string message, IPEndPoint sender)
    {
        if (sender?.Address == null || string.IsNullOrWhiteSpace(message) || !message.StartsWith("BT-SEARCH"))
        {
            return;
        }

        string infoHash = null;
        var port = 0;
        string cookie = null;

        foreach (var line in message.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("Infohash:", StringComparison.OrdinalIgnoreCase))
            {
                infoHash = trimmedLine[9..].Trim();
            }
            else if (trimmedLine.StartsWith("Port:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(trimmedLine[5..].Trim(), out port);
            }
            else if (trimmedLine.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase))
            {
                cookie = trimmedLine[7..].Trim();
            }
        }

        if (!string.IsNullOrEmpty(cookie) && string.Equals(cookie, ClientCookie, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrEmpty(infoHash) || !InfoHashRegex.IsMatch(infoHash))
        {
            return;
        }

        if (port <= 0 || port > 65535)
        {
            return;
        }

        if (port < 1024)
        {
            _logger.Debug("LPD: rejected announcement from {0} with invalid or privileged port {1}", sender.Address, port);
            return;
        }

        var listeningPort = _configService?.ListeningPort ?? 0;
        if (listeningPort > 0 && port == listeningPort && (IPAddress.IsLoopback(sender.Address) || IsLocalAddress(sender.Address)))
        {
            _logger.Debug("LPD: rejected self-announcement from {0}:{1}", sender.Address, port);
            return;
        }

        if (!IsPrivateSubnet(sender.Address))
        {
            _logger.Debug("LPD: rejected announcement from non-private IP {0}", sender.Address);
            return;
        }

        var torrent = _torrentService?.FindByInfoHash(infoHash);
        if (torrent == null || torrent.IsPrivate)
        {
            _logger.Debug("LPD: Discarding announcement for unknown or private torrent: {0}", infoHash);
            return;
        }

        _logger.Debug("LPD: peer {0}:{1} for {2}", sender.Address, port, infoHash);
        _peerDiscovery.AddPeers(infoHash, new[] { new TrackerPeer { Ip = sender.Address.ToString(), Port = port } }, "lpd");
    }

    internal static bool IsPrivateSubnet(IPAddress address)
    {
        if (address == null)
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 169.254.0.0/16 (IPv4 link-local)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fe80::/10 (link-local)
            if (address.IsIPv6LinkLocal)
            {
                return true;
            }

            // fc00::/7 (unique local: fc00::/7 covering fc00:: to fdff:ffff:...)
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return true;
            }

            return false;
        }

        return false;
    }

    internal static bool IsLocalAddress(IPAddress address)
    {
        if (address == null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var nic in nics)
            {
                var ipProps = nic.GetIPProperties();
                if (ipProps?.UnicastAddresses == null)
                {
                    continue;
                }

                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.Equals(address))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Safe fallback if querying network interfaces throws.
        }

        return false;
    }

    internal static List<NetworkInterface> GetActiveNetworkInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();
        }
        catch
        {
            return new List<NetworkInterface>();
        }
    }
}
