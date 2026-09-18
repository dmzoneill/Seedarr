using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;
using Polly;

namespace NzbDrone.Core.Trackers.Http;

public class HttpTrackerProvider : ITrackerProvider, IHandle<ConfigSavedEvent>
{
    public ResiliencePipeline ResiliencePipeline { get; set; } = ResiliencePolicies.GetTrackerPolicy();

    private readonly object _syncLock = new();
    private readonly IConfigService _configService;
    private readonly IProxySettingsProvider _proxySettingsProvider;
    private readonly Logger _logger;

    private HttpClient _client;
    private HttpMessageHandler _handler;
    private ProxyConfigState _lastProxyState;

    internal HttpMessageHandler Handler
    {
        get
        {
            EnsureClientUpdated();
            return _handler;
        }
    }

    internal HttpClient Client
    {
        get
        {
            EnsureClientUpdated();
            return _client;
        }
    }

    public string Name => "HTTP";

    public HttpTrackerProvider(IConfigService configService, IProxySettingsProvider proxySettingsProvider = null)
    {
        _configService = configService;
        _proxySettingsProvider = proxySettingsProvider;
        _logger = LogManager.GetCurrentClassLogger();

        RebuildClient();
    }

    public void Handle(ConfigSavedEvent message)
    {
        _logger.Debug("ConfigSavedEvent received, updating HTTP tracker client");
        RebuildClient();
    }

    internal void ReconfigureClient()
    {
        RebuildClient();
    }

    private void EnsureClientUpdated()
    {
        if (_proxySettingsProvider == null)
        {
            return;
        }

        var currentState = new ProxyConfigState(_proxySettingsProvider, _configService);
        if (!currentState.Equals(_lastProxyState))
        {
            lock (_syncLock)
            {
                if (!currentState.Equals(_lastProxyState))
                {
                    RebuildClient();
                }
            }
        }
    }

    private void RebuildClient()
    {
        lock (_syncLock)
        {
            _handler = CreateHandler();
            var timeoutSeconds = _configService?.HttpTrackerTimeoutSeconds ?? 10;
            if (timeoutSeconds <= 0)
            {
                timeoutSeconds = 10;
            }

            _client = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            _lastProxyState = new ProxyConfigState(_proxySettingsProvider, _configService);
            _logger.Debug("Rebuilt HTTP tracker client (proxy enabled: {0})", _proxySettingsProvider?.IsEnabled == true);
        }
    }

    private HttpMessageHandler CreateHandler()
    {
        try
        {
            if (_proxySettingsProvider != null && _proxySettingsProvider.IsEnabled)
            {
                var handler = _proxySettingsProvider.CreateHandler();
                if (handler != null)
                {
                    return handler;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create proxy handler; falling back to direct connection");
        }

        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
    }

    private HttpClient GetClient()
    {
        EnsureClientUpdated();
        return _client;
    }

    public TrackerAnnounceResponse Announce(TrackerAnnounceRequest request)
    {
        try
        {
            var url = BuildAnnounceUrl(request);
            _logger.Debug("HTTP announce: {0}", RedactUrl(url));

            var client = GetClient();

            var responseBytes = (ResiliencePipeline ?? ResiliencePipeline.Empty).Execute(ct =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                var userAgent = !string.IsNullOrWhiteSpace(request.UserAgent)
                    ? request.UserAgent
                    : _configService.BitTorrentUserAgent;
                req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                using var response = client.Send(req, ct);
                response.EnsureSuccessStatusCode();
                using var ms = new MemoryStream();
                response.Content.ReadAsStream(ct).CopyTo(ms);
                return ms.ToArray();
            });
            var parser = new BencodeParser();
            var dict = parser.Parse<BDictionary>(responseBytes);

            if (dict.ContainsKey("failure reason"))
            {
                return new TrackerAnnounceResponse
                {
                    Success = false,
                    FailureReason = ((BString)dict["failure reason"]).ToString()
                };
            }

            var response = new TrackerAnnounceResponse
            {
                Success = true,
                Interval = dict.ContainsKey("interval") ? (int)((BNumber)dict["interval"]).Value : 1800,
                MinInterval = dict.ContainsKey("min interval") ? (int)((BNumber)dict["min interval"]).Value : 900,
                Complete = dict.ContainsKey("complete") ? (int)((BNumber)dict["complete"]).Value : 0,
                Incomplete = dict.ContainsKey("incomplete") ? (int)((BNumber)dict["incomplete"]).Value : 0,
                Peers = new List<TrackerPeer>()
            };

            if (dict.ContainsKey("warning message"))
            {
                response.WarningMessage = ((BString)dict["warning message"]).ToString();
            }

            if (dict.ContainsKey("peers"))
            {
                var peers = dict["peers"];
                if (peers is BList peerList)
                {
                    foreach (var peer in peerList.Cast<BDictionary>())
                    {
                        response.Peers.Add(new TrackerPeer
                        {
                            Ip = ((BString)peer["ip"]).ToString(),
                            Port = (int)((BNumber)peer["port"]).Value,
                            PeerId = peer.ContainsKey("peer id") ? ((BString)peer["peer id"]).ToString() : null
                        });
                    }
                }
                else if (peers is BString compactPeers)
                {
                    var data = compactPeers.Value;
                    for (var i = 0; i + 5 < data.Length; i += 6)
                    {
                        var span = data.Span;
                        var ip = $"{span[i]}.{span[i + 1]}.{span[i + 2]}.{span[i + 3]}";
                        var port = (span[i + 4] << 8) | span[i + 5];
                        response.Peers.Add(new TrackerPeer { Ip = ip, Port = port });
                    }
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HTTP announce failed for {0}", request.TrackerUrl);
            return new TrackerAnnounceResponse
            {
                Success = false,
                FailureReason = ex.Message
            };
        }
    }

    public TrackerScrapeResponse Scrape(string infoHash, string trackerUrl)
    {
        try
        {
            var scrapeUrl = trackerUrl.Replace("/announce", "/scrape");
            var hashBytes = Convert.FromHexString(infoHash);
            var escapedHash = string.Join("", hashBytes.Select(b => $"%{b:X2}"));
            var scrapeSep = scrapeUrl.Contains('?') ? "&" : "?";
            scrapeUrl += $"{scrapeSep}info_hash={escapedHash}";

            _logger.Debug("HTTP scrape: {0}", RedactUrl(scrapeUrl));

            var client = GetClient();

            var responseBytes = (ResiliencePipeline ?? ResiliencePipeline.Empty).Execute(ct =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, scrapeUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", _configService.BitTorrentUserAgent);
                using var response = client.Send(req, ct);
                response.EnsureSuccessStatusCode();
                using var ms = new MemoryStream();
                response.Content.ReadAsStream(ct).CopyTo(ms);
                return ms.ToArray();
            });
            var parser = new BencodeParser();
            var dict = parser.Parse<BDictionary>(responseBytes);

            if (dict.ContainsKey("failure reason"))
            {
                return new TrackerScrapeResponse
                {
                    Success = false,
                    FailureReason = ((BString)dict["failure reason"]).ToString()
                };
            }

            if (dict.ContainsKey("files"))
            {
                var files = (BDictionary)dict["files"];
                var first = files.Values.FirstOrDefault() as BDictionary;
                if (first != null)
                {
                    return new TrackerScrapeResponse
                    {
                        Success = true,
                        Complete = first.ContainsKey("complete") ? (int)((BNumber)first["complete"]).Value : 0,
                        Incomplete = first.ContainsKey("incomplete") ? (int)((BNumber)first["incomplete"]).Value : 0,
                        Downloaded = first.ContainsKey("downloaded") ? (int)((BNumber)first["downloaded"]).Value : 0
                    };
                }
            }

            return new TrackerScrapeResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HTTP scrape failed for {0}", trackerUrl);
            return new TrackerScrapeResponse
            {
                Success = false,
                FailureReason = ex.Message
            };
        }
    }

    public Dictionary<string, TrackerScrapeResponse> BatchScrape(IEnumerable<string> infoHashes, string trackerUrl)
    {
        var result = new Dictionary<string, TrackerScrapeResponse>(StringComparer.OrdinalIgnoreCase);
        if (infoHashes == null)
        {
            return result;
        }

        foreach (var hash in infoHashes)
        {
            result[hash] = Scrape(hash, trackerUrl);
        }

        return result;
    }

    private static string RedactUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
        {
            return url;
        }

        return uri.GetComponents(
            UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
    }

    private static readonly string[] DefaultParameterOrder =
    {
        "info_hash", "peer_id", "port", "uploaded", "downloaded", "left",
        "compact", "numwant", "event", "key"
    };

    private static string BuildAnnounceUrl(TrackerAnnounceRequest request)
    {
        if (string.IsNullOrEmpty(request.Key))
        {
            request.Key = Generate32BitKey();
        }

        var profile = request.ClientProfile ?? DetectProfile(request);
        var order = profile?.AnnounceParameterOrder ?? DefaultParameterOrder;
        var extras = profile?.ExtraAnnounceParameters ?? new Dictionary<string, string>();

        var escapedHash = EscapeInfoHash(request.InfoHash);
        var escapedPeerId = EscapePeerId(request.PeerId);

        var paramValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["info_hash"] = escapedHash,
            ["peer_id"] = escapedPeerId,
            ["port"] = request.Port.ToString(CultureInfo.InvariantCulture),
            ["uploaded"] = request.Uploaded.ToString(CultureInfo.InvariantCulture),
            ["downloaded"] = request.Downloaded.ToString(CultureInfo.InvariantCulture),
            ["left"] = request.Left.ToString(CultureInfo.InvariantCulture),
            ["compact"] = request.Compact ? "1" : "0",
            ["numwant"] = request.NumWant.ToString(CultureInfo.InvariantCulture),
            ["key"] = request.Key
        };

        if (request.Event != AnnounceEvent.None && !string.IsNullOrEmpty(request.EventString))
        {
            paramValues["event"] = request.EventString;
        }

        foreach (var extra in extras)
        {
            if (!paramValues.ContainsKey(extra.Key))
            {
                paramValues[extra.Key] = extra.Value;
            }
        }

        var sb = new StringBuilder();
        sb.Append(request.TrackerUrl);
        var hasQuery = request.TrackerUrl.Contains('?');
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var paramName in order)
        {
            if (paramValues.TryGetValue(paramName, out var paramValue))
            {
                visited.Add(paramName);
                sb.Append(hasQuery ? '&' : '?');
                hasQuery = true;
                sb.Append(paramName);
                sb.Append('=');
                sb.Append(paramValue);
            }
        }

        foreach (var kvp in paramValues)
        {
            if (!visited.Contains(kvp.Key))
            {
                sb.Append(hasQuery ? '&' : '?');
                hasQuery = true;
                sb.Append(kvp.Key);
                sb.Append('=');
                sb.Append(kvp.Value);
            }
        }

        return sb.ToString();
    }

    private static IClientProfile DetectProfile(TrackerAnnounceRequest request)
    {
        if (request == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(request.PeerId))
        {
            if (request.PeerId.StartsWith("-qB", StringComparison.OrdinalIgnoreCase))
            {
                return new QBittorrentProfile();
            }

            if (request.PeerId.StartsWith("-TR", StringComparison.OrdinalIgnoreCase))
            {
                return new TransmissionProfile();
            }

            if (request.PeerId.StartsWith("-DE", StringComparison.OrdinalIgnoreCase))
            {
                return new DelugeProfile();
            }

            if (request.PeerId.StartsWith("-UT", StringComparison.OrdinalIgnoreCase))
            {
                return new UTorrentProfile();
            }

            if (request.PeerId.StartsWith("-BI", StringComparison.OrdinalIgnoreCase))
            {
                return new BiglyBTProfile();
            }
        }

        if (!string.IsNullOrEmpty(request.UserAgent))
        {
            if (request.UserAgent.Contains("qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                return new QBittorrentProfile();
            }

            if (request.UserAgent.Contains("Transmission", StringComparison.OrdinalIgnoreCase))
            {
                return new TransmissionProfile();
            }

            if (request.UserAgent.Contains("Deluge", StringComparison.OrdinalIgnoreCase))
            {
                return new DelugeProfile();
            }

            if (request.UserAgent.Contains("uTorrent", StringComparison.OrdinalIgnoreCase))
            {
                return new UTorrentProfile();
            }

            if (request.UserAgent.Contains("BiglyBT", StringComparison.OrdinalIgnoreCase))
            {
                return new BiglyBTProfile();
            }
        }

        return null;
    }

    private static string EscapePeerId(string peerId)
    {
        if (string.IsNullOrEmpty(peerId))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var bytes = Encoding.Latin1.GetBytes(peerId);
        foreach (var b in bytes)
        {
            if ((b >= 'a' && b <= 'z') ||
                (b >= 'A' && b <= 'Z') ||
                (b >= '0' && b <= '9') ||
                b == '-' || b == '_' || b == '.' || b == '~')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append($"%{b:X2}");
            }
        }

        return sb.ToString();
    }

    private static string EscapeInfoHash(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return string.Empty;
        }

        byte[] hashBytes;
        if (infoHash.Length == 40 && IsHexString(infoHash))
        {
            hashBytes = Convert.FromHexString(infoHash);
        }
        else
        {
            hashBytes = Encoding.Latin1.GetBytes(infoHash);
        }

        return string.Concat(hashBytes.Select(b => $"%{b:X2}"));
    }

    private static bool IsHexString(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    private static string Generate32BitKey()
    {
        var keyVal = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
        return keyVal.ToString("X8", CultureInfo.InvariantCulture);
    }

    private readonly struct ProxyConfigState : IEquatable<ProxyConfigState>
    {
        public bool IsEnabled { get; }
        public ProxyType Type { get; }
        public string Host { get; }
        public int Port { get; }
        public string Username { get; }
        public string Password { get; }
        public bool ProxyAuthEnabled { get; }
        public int TimeoutSeconds { get; }

        public ProxyConfigState(IProxySettingsProvider proxyProvider, IConfigService configService)
        {
            TimeoutSeconds = configService?.HttpTrackerTimeoutSeconds ?? 10;
            ProxyAuthEnabled = configService?.ProxyAuthEnabled ?? false;

            if (proxyProvider != null && proxyProvider.IsEnabled)
            {
                IsEnabled = true;
                Type = proxyProvider.Type;
                Host = proxyProvider.Host ?? string.Empty;
                Port = proxyProvider.Port;
                Username = proxyProvider.Username ?? string.Empty;
                Password = proxyProvider.Password ?? string.Empty;
            }
            else
            {
                IsEnabled = false;
                Type = ProxyType.None;
                Host = string.Empty;
                Port = 0;
                Username = string.Empty;
                Password = string.Empty;
            }
        }

        public bool Equals(ProxyConfigState other)
        {
            return IsEnabled == other.IsEnabled &&
                Type == other.Type &&
                string.Equals(Host, other.Host, StringComparison.Ordinal) &&
                Port == other.Port &&
                string.Equals(Username, other.Username, StringComparison.Ordinal) &&
                string.Equals(Password, other.Password, StringComparison.Ordinal) &&
                ProxyAuthEnabled == other.ProxyAuthEnabled &&
                TimeoutSeconds == other.TimeoutSeconds;
        }

        public override bool Equals(object obj) => obj is ProxyConfigState other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(
                IsEnabled,
                (int)Type,
                Host,
                Port,
                Username,
                Password,
                ProxyAuthEnabled,
                TimeoutSeconds);
        }
    }
}
