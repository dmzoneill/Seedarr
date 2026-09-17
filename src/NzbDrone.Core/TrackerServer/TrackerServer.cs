using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer.Users;

namespace NzbDrone.Core.TrackerServer;

public class TrackerServer : BackgroundService, IHandle<ConfigSavedEvent>
{
    private static readonly byte[] MissingParametersResponse = Encoding.ASCII.GetBytes("d14:failure reason25:Missing required parameterse");
    private static readonly byte[] InvalidParametersResponse = Encoding.ASCII.GetBytes("d14:failure reason18:Invalid parameterse");
    private static readonly byte[] MissingPasskeyResponse = Encoding.ASCII.GetBytes("d14:failure reason25:Missing announce passkeye");
    private static readonly byte[] InvalidPasskeyResponse = Encoding.ASCII.GetBytes("d14:failure reason27:Invalid or revoked passkeye");
    private static readonly byte[] UnregisteredTorrentResponse = Encoding.ASCII.GetBytes("d14:failure reason35:torrent not registered with trackere");

    private readonly IPeerDatabase _peerDatabase;
    private readonly IConfigService _configService;
    private readonly IScrapeCache _scrapeCache;
    private readonly ITrackerUserService _trackerUserService;
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, RateLimitEntry> _rateLimits = new();
    private readonly ConcurrentDictionary<string, (long Uploaded, long Downloaded)> _peerTraffic = new();
    private readonly object _listenerLock = new();

    private TcpListener _listener;
    private CancellationTokenSource _listenerCts;
    private bool _wasEnabled;

    public TrackerServer(IPeerDatabase peerDatabase, IConfigService configService)
        : this(peerDatabase, configService, new ScrapeCache(), null, null)
    {
    }

    public TrackerServer(IPeerDatabase peerDatabase, IConfigService configService, IScrapeCache scrapeCache)
        : this(peerDatabase, configService, scrapeCache, null, null)
    {
    }

    public TrackerServer(IPeerDatabase peerDatabase, IConfigService configService, ITrackerUserService trackerUserService)
        : this(peerDatabase, configService, new ScrapeCache(), trackerUserService, null)
    {
    }

    public TrackerServer(
        IPeerDatabase peerDatabase,
        IConfigService configService,
        IScrapeCache scrapeCache,
        ITrackerUserService trackerUserService)
        : this(peerDatabase, configService, scrapeCache, trackerUserService, null)
    {
    }

    public TrackerServer(
        IPeerDatabase peerDatabase,
        IConfigService configService,
        ITorrentService torrentService)
        : this(peerDatabase, configService, new ScrapeCache(), null, torrentService)
    {
    }

    public TrackerServer(
        IPeerDatabase peerDatabase,
        IConfigService configService,
        IScrapeCache scrapeCache,
        ITrackerUserService trackerUserService,
        ITorrentService torrentService)
    {
        _peerDatabase = peerDatabase;
        _configService = configService;
        _scrapeCache = scrapeCache ?? new ScrapeCache();
        _trackerUserService = trackerUserService;
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ConfigSavedEvent message)
    {
        var isEnabled = _configService.TrackerServerEnabled && _configService.TrackerHttpEnabled;

        lock (_listenerLock)
        {
            if (isEnabled)
            {
                var currentPort = (_listener?.LocalEndpoint as IPEndPoint)?.Port;
                if (_listener != null && currentPort != _configService.TrackerHttpPort)
                {
                    StopListener();
                }

                if (_listener == null)
                {
                    _logger.Info("Tracker server starting listener");
                    StartListener();
                }
            }
            else if (_wasEnabled)
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
        var cleanupTimer = new Timer(
            _ =>
            {
                PurgeExpiredRateLimits();
                _scrapeCache.PurgeExpired();
            },
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

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

            using var networkStream = client.GetStream();
            using var stream = new BufferedStream(networkStream, 4096);

            var requestLine = ReadBoundedLine(stream, 2048);
            if (string.IsNullOrEmpty(requestLine))
            {
                return;
            }

            var parts = requestLine.Split(' ');
            if (parts.Length < 2 || parts[0] != "GET")
            {
                return;
            }

            var path = parts[1];
            var infoHash = ExtractInfoHash(path);

            // Drain remaining headers to prevent connection reset
            string line;
            while (!string.IsNullOrEmpty(line = ReadBoundedLine(stream, 1024)))
            {
                // Discard header lines
            }

            if (IsRateLimited(clientIp, infoHash))
            {
                var rateLimitHeaders = "HTTP/1.1 429 Too Many Requests\r\nContent-Type: text/plain\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                var rateLimitHeaderBytes = Encoding.ASCII.GetBytes(rateLimitHeaders);
                stream.Write(rateLimitHeaderBytes, 0, rateLimitHeaderBytes.Length);
                stream.Flush();
                return;
            }

            byte[] bodyBytes;

            if (path.StartsWith("/announce", StringComparison.OrdinalIgnoreCase))
            {
                bodyBytes = HandleAnnounce(path, remoteEndpoint);
            }
            else if (path.StartsWith("/scrape", StringComparison.OrdinalIgnoreCase))
            {
                if (!_configService.TrackerEnableScrape)
                {
                    bodyBytes = Encoding.ASCII.GetBytes("d14:failure reason15:Scrape disablede");
                }
                else
                {
                    bodyBytes = HandleScrape(path);
                }
            }
            else
            {
                bodyBytes = Encoding.ASCII.GetBytes("d14:failure reason13:Invalid requeste");
            }

            var httpHeaders = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(httpHeaders);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
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

    internal static string ReadBoundedLine(Stream stream, int maxLength)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(maxLength);
        try
        {
            var position = 0;

            while (position < maxLength)
            {
                var b = stream.ReadByte();

                if (b == -1)
                {
                    return position > 0 ? Encoding.Latin1.GetString(buffer, 0, position) : null;
                }

                if (b == '\n')
                {
                    return Encoding.Latin1.GetString(buffer, 0, position).TrimEnd('\r');
                }

                buffer[position++] = (byte)b;
            }

            return null; // Line too long, reject
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string ExtractInfoHash(string path)
    {
        var queryIndex = path.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }

        var parameters = ParseQueryString(path[(queryIndex + 1)..]);
        return parameters.GetValueOrDefault("info_hash");
    }

    internal static string ExtractPasskey(string path, Dictionary<string, string> parameters = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var queryIndex = path.IndexOf('?');
        var pathPart = queryIndex >= 0 ? path[..queryIndex] : path;

        if (pathPart.StartsWith("/announce/", StringComparison.OrdinalIgnoreCase))
        {
            var subPath = pathPart["/announce/".Length..];
            var segments = subPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0 && !string.IsNullOrWhiteSpace(segments[0]))
            {
                try
                {
                    return Uri.UnescapeDataString(segments[0].Trim());
                }
                catch (UriFormatException)
                {
                    return segments[0].Trim();
                }
            }
        }

        if (parameters != null)
        {
            if (parameters.TryGetValue("passkey", out var queryPasskey) && !string.IsNullOrWhiteSpace(queryPasskey))
            {
                return queryPasskey.Trim();
            }
        }
        else if (queryIndex >= 0 && queryIndex < path.Length - 1)
        {
            var query = path[(queryIndex + 1)..];
            var parsed = ParseQueryString(query);
            if (parsed.TryGetValue("passkey", out var queryPasskey) && !string.IsNullOrWhiteSpace(queryPasskey))
            {
                return queryPasskey.Trim();
            }
        }

        return null;
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

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        foreach (var pair in query.Split('&'))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex > 0)
            {
                string key;
                try
                {
                    key = Uri.UnescapeDataString(pair[..eqIndex]);
                }
                catch (UriFormatException)
                {
                    key = pair[..eqIndex];
                }

                var rawValue = pair[(eqIndex + 1)..];

                if (string.Equals(key, "info_hash", StringComparison.OrdinalIgnoreCase))
                {
                    var rawBytes = DecodeUrlBytes(rawValue);
                    result[key] = NormalizeInfoHashToHex(rawBytes);
                }
                else if (string.Equals(key, "peer_id", StringComparison.OrdinalIgnoreCase))
                {
                    var rawBytes = DecodeUrlBytes(rawValue);
                    result[key] = Encoding.Latin1.GetString(rawBytes);
                }
                else
                {
                    try
                    {
                        result[key] = Uri.UnescapeDataString(rawValue);
                    }
                    catch (UriFormatException)
                    {
                        result[key] = rawValue;
                    }
                }
            }
        }

        return result;
    }

    private static byte[] DecodeUrlBytes(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
        {
            return Array.Empty<byte>();
        }

        var result = new List<byte>(encoded.Length);
        for (var i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] == '%' && i + 2 < encoded.Length)
            {
                var h1 = GetHexValue(encoded[i + 1]);
                var h2 = GetHexValue(encoded[i + 2]);
                if (h1 >= 0 && h2 >= 0)
                {
                    result.Add((byte)((h1 << 4) | h2));
                    i += 2;
                    continue;
                }
            }

            result.Add((byte)encoded[i]);
        }

        return result.ToArray();
    }

    private static int GetHexValue(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1
        };
    }

    private static string NormalizeInfoHashToHex(byte[] rawBytes)
    {
        if (rawBytes.Length == 20)
        {
            return Convert.ToHexString(rawBytes).ToLowerInvariant();
        }

        if (rawBytes.Length == 40 && IsHexBytes(rawBytes))
        {
            return Encoding.ASCII.GetString(rawBytes).ToLowerInvariant();
        }

        return Convert.ToHexString(rawBytes).ToLowerInvariant();
    }

    private static bool IsHexBytes(byte[] bytes)
    {
        foreach (var b in bytes)
        {
            if (!((b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    private byte[] HandleAnnounce(string path, IPEndPoint remoteEndpoint)
    {
        var (parameters, error) = ParseRequest(path);
        var passkey = ExtractPasskey(path, parameters);

        if (_configService.TrackerPasskeyAuthEnabled)
        {
            if (string.IsNullOrWhiteSpace(passkey))
            {
                return MissingPasskeyResponse;
            }

            var user = _trackerUserService?.GetByPasskey(passkey);
            if (user == null || !user.IsEnabled || user.IsBanned)
            {
                return InvalidPasskeyResponse;
            }
        }

        if (error != null)
        {
            return Encoding.ASCII.GetBytes(error);
        }

        if (!parameters.TryGetValue("info_hash", out var infoHash) ||
            !parameters.TryGetValue("peer_id", out var peerId) ||
            !parameters.TryGetValue("port", out var portStr) ||
            !parameters.TryGetValue("uploaded", out var uploadedStr) ||
            !parameters.TryGetValue("downloaded", out var downloadedStr) ||
            !parameters.TryGetValue("left", out var leftStr))
        {
            return MissingParametersResponse;
        }

        if (string.IsNullOrEmpty(infoHash) ||
            string.IsNullOrEmpty(peerId) ||
            peerId.Length != 20 ||
            !int.TryParse(portStr, out var port) || port < 1 || port > 65535 ||
            !long.TryParse(uploadedStr, out var uploaded) || uploaded < 0 ||
            !long.TryParse(downloadedStr, out var downloaded) || downloaded < 0 ||
            !long.TryParse(leftStr, out var left) || left < 0)
        {
            return InvalidParametersResponse;
        }

        if (_configService.TrackerPrivateMode)
        {
            var isRegistered = _torrentService != null && _torrentService.ExistsByInfoHash(infoHash);
            if (!isRegistered)
            {
                return UnregisteredTorrentResponse;
            }
        }

        var peerIp = remoteEndpoint.Address.ToString();
        var eventType = parameters.GetValueOrDefault("event", "");

        if (eventType == "stopped")
        {
            _peerDatabase.RemovePeer(infoHash, peerIp, port);
        }
        else
        {
            _peerDatabase.AddPeer(infoHash, peerIp, port, peerId);
        }

        _peerDatabase.IncrementAnnounces();

        if (!string.IsNullOrWhiteSpace(passkey) && _trackerUserService != null)
        {
            var trafficKey = $"{passkey}:{infoHash}:{peerId}";
            long uploadedDelta;
            long downloadedDelta;

            if (_peerTraffic.TryGetValue(trafficKey, out var prevTraffic))
            {
                uploadedDelta = uploaded >= prevTraffic.Uploaded ? uploaded - prevTraffic.Uploaded : uploaded;
                downloadedDelta = downloaded >= prevTraffic.Downloaded ? downloaded - prevTraffic.Downloaded : downloaded;
            }
            else
            {
                uploadedDelta = uploaded;
                downloadedDelta = downloaded;
            }

            _peerTraffic[trafficKey] = (uploaded, downloaded);

            if (eventType == "stopped")
            {
                _peerTraffic.TryRemove(trafficKey, out _);
            }

            _trackerUserService.RecordAnnounce(passkey, uploadedDelta, downloadedDelta);
        }

        var peers = _peerDatabase.GetPeers(infoHash);
        var interval = _configService.TrackerAnnounceInterval;
        var maxPeers = _configService.TrackerMaxPeersPerAnnounce;

        IBObject peersObject;
        var compact = parameters.GetValueOrDefault("compact");
        if (compact == "0")
        {
            peersObject = BuildDictionaryPeers(peers, peerIp, port, maxPeers);
        }
        else
        {
            var compactPeers = BuildCompactPeers(peers, peerIp, port, maxPeers);
            peersObject = new BString(compactPeers);
        }

        if (_configService.TrackerLogAnnounces)
        {
            var returningPeersCount = peersObject is BList bList ? bList.Count : ((BString)peersObject).Value.Length / 6;
            _logger.Info(
                "HTTP announce for {0} from {1}:{2}, event={3}, returning {4} peers",
                infoHash,
                peerIp,
                port,
                eventType,
                returningPeersCount);
        }

        var minInterval = _configService.MinAnnounceIntervalSeconds;
        var dict = new BDictionary
        {
            ["interval"] = new BNumber(interval),
            ["min interval"] = new BNumber(minInterval),
            ["peers"] = peersObject,
        };

        if (_configService.TrackerPrivateMode)
        {
            dict["private"] = new BNumber(1);
        }

        return dict.EncodeAsBytes();
    }

    private byte[] HandleScrape(string path)
    {
        var queryIndex = path.IndexOf('?');
        var isFullScrape = queryIndex < 0 || queryIndex == path.Length - 1;

        if (!isFullScrape)
        {
            var (parameters, error) = ParseRequest(path);
            if (error != null)
            {
                return Encoding.ASCII.GetBytes(error);
            }

            if (!parameters.TryGetValue("info_hash", out var infoHash))
            {
                return Encoding.ASCII.GetBytes("d14:failure reason18:Missing info_hashe");
            }

            _peerDatabase.IncrementScrapes();

            return _scrapeCache.GetOrCreate(infoHash, () =>
            {
                var stats = _peerDatabase.GetStats(infoHash) ?? new ScrapeStats();
                return BuildSingleScrapeResponse(infoHash, stats);
            });
        }

        _peerDatabase.IncrementScrapes();

        return _scrapeCache.GetOrCreateFullScrape(() =>
        {
            var allStats = _peerDatabase.GetAllStats();
            return BuildFullScrapeResponse(allStats);
        });
    }

    private byte[] BuildSingleScrapeResponse(string infoHash, ScrapeStats stats)
    {
        var scrapeInterval = _configService.ScrapeIntervalSeconds;

        var fileDict = new BDictionary
        {
            ["complete"] = new BNumber(stats?.Complete ?? 0),
            ["downloaded"] = new BNumber(stats?.Downloaded ?? 0),
            ["incomplete"] = new BNumber(stats?.Incomplete ?? 0),
        };

        var files = new BDictionary();
        byte[] hashKeyBytes;
        try
        {
            hashKeyBytes = Convert.FromHexString(infoHash);
        }
        catch (FormatException)
        {
            hashKeyBytes = Encoding.Latin1.GetBytes(infoHash);
        }

        files.Add(new BString(hashKeyBytes), fileDict);

        var response = new BDictionary
        {
            ["files"] = files,
            ["min_request_interval"] = new BNumber(scrapeInterval),
        };

        return response.EncodeAsBytes();
    }

    private byte[] BuildFullScrapeResponse(Dictionary<string, ScrapeStats> allStats)
    {
        var scrapeInterval = _configService.ScrapeIntervalSeconds;
        var files = new BDictionary();

        if (allStats != null)
        {
            foreach (var (infoHash, stats) in allStats)
            {
                var fileDict = new BDictionary
                {
                    ["complete"] = new BNumber(stats?.Complete ?? 0),
                    ["downloaded"] = new BNumber(stats?.Downloaded ?? 0),
                    ["incomplete"] = new BNumber(stats?.Incomplete ?? 0),
                };

                byte[] hashKeyBytes;
                try
                {
                    hashKeyBytes = Convert.FromHexString(infoHash);
                }
                catch (FormatException)
                {
                    hashKeyBytes = Encoding.Latin1.GetBytes(infoHash);
                }

                files.Add(new BString(hashKeyBytes), fileDict);
            }
        }

        var response = new BDictionary
        {
            ["files"] = files,
            ["min_request_interval"] = new BNumber(scrapeInterval),
        };

        return response.EncodeAsBytes();
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

    private static BList BuildDictionaryPeers(List<TrackerPeerEntry> peers, string excludeIp, int excludePort, int maxPeers)
    {
        var filtered = peers.Where(p => p.Ip != excludeIp || p.Port != excludePort).Take(maxPeers).ToList();
        var list = new BList();
        foreach (var peer in filtered)
        {
            var peerDict = new BDictionary
            {
                ["ip"] = new BString(peer.Ip),
                ["port"] = new BNumber(peer.Port),
                ["peer id"] = new BString(Encoding.Latin1.GetBytes(peer.PeerId ?? string.Empty))
            };
            list.Add(peerDict);
        }

        return list;
    }

    private bool IsRateLimited(string ip) => IsRateLimited(ip, null);

    private bool IsRateLimited(string ip, string infoHash)
    {
        var rateLimit = _configService.TrackerRateLimitPerMinute;

        if (rateLimit <= 0)
        {
            return false;
        }

        var key = string.IsNullOrEmpty(infoHash) ? ip : $"{ip}:{infoHash.ToLowerInvariant()}";
        var now = DateTime.UtcNow;
        var entry = _rateLimits.AddOrUpdate(
            key,
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
