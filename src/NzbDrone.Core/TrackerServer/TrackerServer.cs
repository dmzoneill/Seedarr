using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.TrackerServer;

public class TrackerServer : BackgroundService, IHandle<ConfigSavedEvent>
{
    private readonly IPeerDatabase _peerDatabase;
    private readonly IConfigService _configService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, RateLimitEntry> _rateLimits = new();
    private readonly object _listenerLock = new();

    private TcpListener _listener;
    private CancellationTokenSource _listenerCts;
    private bool _wasEnabled;

    public TrackerServer(IPeerDatabase peerDatabase, IConfigService configService)
    {
        _peerDatabase = peerDatabase;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ConfigSavedEvent message)
    {
        var isEnabled = _configService.TrackerServerEnabled && _configService.TrackerHttpEnabled;

        lock (_listenerLock)
        {
            if (isEnabled && !_wasEnabled)
            {
                _logger.Info("Tracker server enabled via config change, starting listener");
                StartListener();
            }
            else if (!isEnabled && _wasEnabled)
            {
                _logger.Info("Tracker server disabled via config change, stopping listener");
                StopListener();
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isEnabled = _configService.TrackerServerEnabled && _configService.TrackerHttpEnabled;

        lock (_listenerLock)
        {
            _wasEnabled = isEnabled;
        }

        if (isEnabled)
        {
            StartListener();
        }
        else
        {
            _logger.Debug("Built-in tracker is disabled, waiting for config change");
        }

        // Keep the BackgroundService alive until application shutdown
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            StopListener();
        }
    }

    private void StartListener()
    {
        lock (_listenerLock)
        {
            if (_listener != null)
            {
                return;
            }

            var port = _configService.TrackerHttpPort;
            var bindAddress = IPAddress.Parse(_configService.TrackerBindAddress);
            var listener = new TcpListener(bindAddress, port);

            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                _logger.Warn(ex, "Built-in HTTP tracker failed to bind {0}:{1}, skipping", bindAddress, port);
                return;
            }

            _listener = listener;
            _wasEnabled = true;
            _listenerCts = new CancellationTokenSource();
            _logger.Info("Built-in HTTP tracker listening on {0}:{1}", bindAddress, port);

            _ = Task.Run(() => AcceptLoop(_listener, _listenerCts.Token));
        }
    }

    private void StopListener()
    {
        lock (_listenerLock)
        {
            _wasEnabled = false;

            if (_listenerCts != null)
            {
                _listenerCts.Cancel();
                _listenerCts.Dispose();
                _listenerCts = null;
            }

            if (_listener != null)
            {
                _listener.Stop();
                _listener = null;
                _logger.Info("Built-in HTTP tracker stopped");
            }
        }
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken ct)
    {
        var cleanupTimer = new Timer(_ => PurgeExpiredRateLimits(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleRequest(client), ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            await cleanupTimer.DisposeAsync();
        }
    }

    private void HandleRequest(TcpClient client)
    {
        try
        {
            client.Client.ReceiveTimeout = 10000;
            var remoteEndpoint = (IPEndPoint)client.Client.RemoteEndPoint;
            var clientIp = remoteEndpoint.Address.ToString();

            using var stream = client.GetStream();

            if (IsRateLimited(clientIp))
            {
                var rateLimitResponse = "HTTP/1.1 429 Too Many Requests\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                var rateLimitBytes = Encoding.ASCII.GetBytes(rateLimitResponse);
                stream.Write(rateLimitBytes, 0, rateLimitBytes.Length);
                return;
            }

            var requestLine = ReadBoundedLine(stream, 8192);
            if (requestLine == null)
            {
                return;
            }

            var parts = requestLine.Split(' ');
            if (parts.Length < 2 || parts[0] != "GET")
            {
                return;
            }

            var path = parts[1];

            byte[] bodyBytes;

            if (path.StartsWith("/announce"))
            {
                bodyBytes = HandleAnnounce(path, remoteEndpoint);
            }
            else if (path.StartsWith("/scrape"))
            {
                string responseBody;
                if (!_configService.TrackerEnableScrape)
                {
                    responseBody = "d14:failure reason15:Scrape disablede";
                }
                else
                {
                    responseBody = HandleScrape(path);
                }

                bodyBytes = Encoding.ASCII.GetBytes(responseBody);
            }
            else
            {
                bodyBytes = Encoding.ASCII.GetBytes("d14:failure reason13:Invalid requeste");
            }

            var httpHeaders = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(httpHeaders);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Tracker request error");
        }
        finally
        {
            client.Dispose();
        }
    }

    private static string ReadBoundedLine(NetworkStream stream, int maxLength)
    {
        var buffer = new byte[maxLength];
        var position = 0;

        while (position < maxLength)
        {
            var b = stream.ReadByte();

            if (b == -1)
            {
                return position > 0 ? Encoding.ASCII.GetString(buffer, 0, position) : null;
            }

            if (b == '\n')
            {
                return Encoding.ASCII.GetString(buffer, 0, position).TrimEnd('\r');
            }

            buffer[position++] = (byte)b;
        }

        return null; // Line too long, reject
    }

    private (Dictionary<string, string> Parameters, string Error) ParseRequest(string path)
    {
        var queryIndex = path.IndexOf('?');
        if (queryIndex < 0)
        {
            return (null, "d14:failure reason20:Missing query stringe");
        }

        var query = path[(queryIndex + 1)..];
        return (ParseQueryString(query), null);
    }

    private byte[] HandleAnnounce(string path, IPEndPoint remoteEndpoint)
    {
        var (parameters, error) = ParseRequest(path);
        if (error != null)
        {
            return Encoding.ASCII.GetBytes(error);
        }

        if (!parameters.TryGetValue("info_hash", out var infoHash) ||
            !parameters.TryGetValue("port", out var portStr))
        {
            return Encoding.ASCII.GetBytes("d14:failure reason25:Missing required parameterse");
        }

        if (!int.TryParse(portStr, out var port) || port < 1 || port > 65535)
        {
            return Encoding.ASCII.GetBytes("d14:failure reason12:invalid porte");
        }

        var peerIp = remoteEndpoint.Address.ToString();

        parameters.TryGetValue("peer_id", out var peerId);
        parameters.TryGetValue("event", out var eventType);

        if (eventType == "stopped")
        {
            _peerDatabase.RemovePeer(infoHash, peerIp, port);
        }
        else
        {
            _peerDatabase.AddPeer(infoHash, peerIp, port, peerId ?? "");
        }

        var peers = _peerDatabase.GetPeers(infoHash);
        var interval = _configService.TrackerAnnounceInterval;
        var maxPeers = _configService.TrackerMaxPeersPerAnnounce;
        var compactPeers = BuildCompactPeers(peers, peerIp, port, maxPeers);

        if (_configService.TrackerLogAnnounces)
        {
            _logger.Info(
                "HTTP announce for {0} from {1}:{2}, event={3}, returning {4} peers",
                infoHash,
                peerIp,
                port,
                eventType ?? "",
                compactPeers.Length / 6);
        }

        var minInterval = _configService.MinAnnounceIntervalSeconds;
        var dict = new BDictionary
        {
            ["interval"] = new BNumber(interval),
            ["min interval"] = new BNumber(minInterval),
            ["peers"] = new BString(compactPeers),
        };

        if (_configService.TrackerPrivateMode)
        {
            dict["private"] = new BNumber(1);
        }

        return dict.EncodeAsBytes();
    }

    private string HandleScrape(string path)
    {
        var (parameters, error) = ParseRequest(path);
        if (error != null)
        {
            return error;
        }

        if (!parameters.TryGetValue("info_hash", out var infoHash))
        {
            return "d14:failure reason18:Missing info_hashe";
        }

        var stats = _peerDatabase.GetStats(infoHash);
        var scrapeInterval = _configService.ScrapeIntervalSeconds;
        return $"d5:filesd{infoHash.Length}:{infoHash}d8:completei{stats.Complete}e10:downloadedi{stats.Downloaded}e10:incompletei{stats.Incomplete}eee20:min_request_intervali{scrapeInterval}ee";
    }

    private static byte[] BuildCompactPeers(List<TrackerPeerEntry> peers, string excludeIp, int excludePort, int maxPeers)
    {
        var filtered = peers.Where(p => p.Ip != excludeIp || p.Port != excludePort).Take(maxPeers).ToList();
        var chunks = new List<byte>(filtered.Count * 6);
        foreach (var peer in filtered)
        {
            if (!IPAddress.TryParse(peer.Ip, out var addr) || addr.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            var ipBytes = addr.GetAddressBytes();
            chunks.AddRange(ipBytes);
            chunks.Add((byte)(peer.Port >> 8));
            chunks.Add((byte)peer.Port);
        }

        return chunks.ToArray();
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&'))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = Uri.UnescapeDataString(pair[..eqIndex]);
                var value = Uri.UnescapeDataString(pair[(eqIndex + 1)..]);
                result[key] = value;
            }
        }

        return result;
    }

    private bool IsRateLimited(string ip)
    {
        var rateLimit = _configService.TrackerRateLimitPerMinute;

        if (rateLimit <= 0)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var entry = _rateLimits.AddOrUpdate(
            ip,
            _ => new RateLimitEntry { Count = 1, WindowStart = now },
            (_, existing) =>
            {
                if ((now - existing.WindowStart).TotalMinutes >= 1)
                {
                    return new RateLimitEntry { Count = 1, WindowStart = now };
                }

                return new RateLimitEntry { Count = existing.Count + 1, WindowStart = existing.WindowStart };
            });

        return entry.Count > rateLimit;
    }

    private void PurgeExpiredRateLimits()
    {
        var now = DateTime.UtcNow;
        var expired = _rateLimits
            .Where(kvp => (now - kvp.Value.WindowStart).TotalMinutes >= 2)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
        {
            _rateLimits.TryRemove(key, out _);
        }
    }

    private sealed class RateLimitEntry
    {
        public int Count { get; init; }
        public DateTime WindowStart { get; init; }
    }
}
