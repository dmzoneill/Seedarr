using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.TrackerBoost;

public interface ITrackerBoostService
{
    List<TrackerBoostTracker> GetAllTrackers();
    TrackerBoostTracker GetTrackerById(int id);
    TrackerBoostTracker AddTracker(string url, TrackerSourceType source = TrackerSourceType.Manual, string sourceName = "Manual");
    void DeleteTracker(int id);
    Task<TrackerBoostStatusSummary> GetStatusSummaryAsync();
    TrackerBoostSettings GetSettings();
    void UpdateSettings(TrackerBoostSettings settings);
    Task<int> HarvestFromActiveDownloadsAsync();
    Task<int> HarvestFromProwlarrAsync();
    Task<int> HarvestFromCuratedListsAsync();
    Task<int> ProbeTrackerHealthAsync();
    Task<TorrentTrackerInspectionResult> InspectTorrentTrackersAsync(int torrentId);
    Task<TorrentTrackerInspectionResult> InspectHashTrackersAsync(string infoHash, string name = "");
    Task<SwarmBoostResult> BoostTorrentAsync(int torrentId, bool onlyVerified = true);
    Task<SwarmBoostResult> BoostHashAsync(string infoHash, string name = "", bool onlyVerified = true);
    Task<SwarmBoostResult> InjectTrackerToTorrentAsync(int torrentId, string trackerUrl, bool force = false);
    Task<SwarmBoostResult> InjectTrackerToHashAsync(string infoHash, string trackerUrl, bool force = false);
    Task<List<SwarmBoostResult>> BoostAllTorrentsAsync(bool onlyVerified = true);
    Task<TrackerCrossMatrixResult> GetCrossMatrixAsync();
    Task<int> RecoverMissingTrackersAsync();
    int InjectIntoDownloadClients(string infoHash, IEnumerable<string> trackers);
    void ReannounceDownloadClients(string infoHash);
    IReadOnlyList<TrackerBoostLogEntry> GetLogs(int limit = 100, string category = null, string level = null);
    void ClearLogs();
    void LogActivity(string level, string category, string message, string trackerUrl = null, string infoHash = null);
    Task RunOptimizationCycleAsync();
}

public class TrackerBoostService : ITrackerBoostService
{
    private const int MaxLogEntries = 500;

    private static readonly HttpClient HttpClient = new(new HttpClientHandler { CheckCertificateRevocationList = true }) { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly BencodeParser BParser = new();
    private static readonly ConcurrentDictionary<string, (DateTime BoostedAt, HashSet<string> InjectedTrackers)> BoostHistory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<TrackerBoostLogEntry> LogBuffer = new();

    private static readonly string[] DefaultBootstrapTrackers = new[]
    {
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.tracker.cl:1337/announce",
        "udp://open.stealth.si:80/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://explodie.org:6969/announce",
        "udp://tracker.openbittorrent.com:6969/announce",
        "udp://tracker.bittor.pw:1337/announce",
        "udp://tracker.dler.org:6969/announce",
        "udp://tracker.moeking.me:6969/announce",
        "udp://p4p.arenabg.com:1337/announce",
        "http://tracker.files.fm:6969/announce",
        "https://tracker.tamersunion.org:443/announce"
    };

    private static DateTime? _lastScanTime;
    private static DateTime? _lastHarvestTime;
    private static DateTime? _lastProwlarrHarvestTime;
    private static DateTime? _lastAutoBoostTime;
    private static int _totalTorrentsBoosted;
    private static int _totalTrackersInjected;
    private static int _totalVerifiedMatchesCount;
    private static int _nextLogId;

    private readonly ITrackerBoostTrackerRepository _trackerRepository;
    private readonly ITorrentService _torrentService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IIndexerRepository _indexerRepository;
    private readonly IDownloadClientFactory _downloadClientFactory;
    private readonly IConfigService _configService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly Logger _logger;

    public TrackerBoostService(
        ITrackerBoostTrackerRepository trackerRepository,
        ITorrentService torrentService,
        ITrackerEntryService trackerEntryService,
        IIndexerRepository indexerRepository,
        IDownloadClientFactory downloadClientFactory,
        IConfigService configService,
        ITorrentFileParser torrentFileParser = null)
    {
        _trackerRepository = trackerRepository;
        _torrentService = torrentService;
        _trackerEntryService = trackerEntryService;
        _indexerRepository = indexerRepository;
        _downloadClientFactory = downloadClientFactory;
        _configService = configService;
        _torrentFileParser = torrentFileParser;
        _logger = LogManager.GetCurrentClassLogger();

        EnsureDefaultTrackersBootstrapped();
    }

    private void EnsureDefaultTrackersBootstrapped()
    {
        try
        {
            var existing = _trackerRepository.All().ToList();
            if (existing.Count == 0)
            {
                foreach (var url in DefaultBootstrapTrackers)
                {
                    AddTrackerInternal(url, TrackerSourceType.PublicList, "Builtin Curated List");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to bootstrap default tracker list");
        }
    }

    public TrackerBoostSettings GetSettings()
    {
        return new TrackerBoostSettings
        {
            AutoBoostEnabled = _configService.GetValueBoolean("TrackerBoostAutoBoostEnabled", true),
            AutoHarvestEnabled = _configService.GetValueBoolean("TrackerBoostAutoHarvestEnabled", true),
            IntervalMinutes = _configService.GetValueInt("TrackerBoostIntervalMinutes", 2),
            MaxTrackersPerTorrent = _configService.GetValueInt("TrackerBoostMaxTrackersPerTorrent", 8),
            OnlyVerified = _configService.GetValueBoolean("TrackerBoostOnlyVerified", true)
        };
    }

    public void UpdateSettings(TrackerBoostSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        var dict = new Dictionary<string, object>
        {
            ["TrackerBoostAutoBoostEnabled"] = settings.AutoBoostEnabled,
            ["TrackerBoostAutoHarvestEnabled"] = settings.AutoHarvestEnabled,
            ["TrackerBoostIntervalMinutes"] = Math.Max(1, settings.IntervalMinutes),
            ["TrackerBoostMaxTrackersPerTorrent"] = Math.Max(1, settings.MaxTrackersPerTorrent),
            ["TrackerBoostOnlyVerified"] = settings.OnlyVerified
        };
        _configService.SaveConfigDictionary(dict);
        LogActivity("Info", "General", $"Tracker Boost settings updated: AutoBoost={settings.AutoBoostEnabled}, Interval={settings.IntervalMinutes}m, OnlyVerified={settings.OnlyVerified}");
    }

    public IReadOnlyList<TrackerBoostLogEntry> GetLogs(int limit = 100, string category = null, string level = null)
    {
        var query = LogBuffer.ToArray().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(l => string.Equals(l.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(level) && !string.Equals(level, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(l => string.Equals(l.Level, level, StringComparison.OrdinalIgnoreCase));
        }

        return query.OrderByDescending(l => l.Id).Take(Math.Clamp(limit, 1, 500)).ToList();
    }

    public void ClearLogs()
    {
        while (LogBuffer.TryDequeue(out _))
        {
        }

        LogActivity("Info", "General", "Tracker Boost activity logs cleared");
    }

    public void LogActivity(string level, string category, string message, string trackerUrl = null, string infoHash = null)
    {
        var entry = new TrackerBoostLogEntry
        {
            Id = Interlocked.Increment(ref _nextLogId),
            Timestamp = DateTime.UtcNow,
            Level = level ?? "Info",
            Category = category ?? "General",
            TrackerUrl = trackerUrl ?? string.Empty,
            InfoHash = infoHash ?? string.Empty,
            Message = message ?? string.Empty
        };

        LogBuffer.Enqueue(entry);
        while (LogBuffer.Count > MaxLogEntries && LogBuffer.TryDequeue(out _))
        {
        }

        switch (level?.ToLowerInvariant())
        {
            case "error":
                _logger.Error("[{0}] {1}", category, message);
                break;
            case "warn":
                _logger.Warn("[{0}] {1}", category, message);
                break;
            case "debug":
                _logger.Debug("[{0}] {1}", category, message);
                break;
            default:
                _logger.Info("[{0}] {1}", category, message);
                break;
        }
    }

    public List<TrackerBoostTracker> GetAllTrackers()
    {
        return _trackerRepository.All()
            .OrderByDescending(t => t.Status == TrackerHealthStatus.Alive)
            .ThenByDescending(t => t.TotalVerifiedTorrents)
            .ThenBy(t => t.LatencyMs > 0 ? t.LatencyMs : 9999)
            .ToList();
    }

    public TrackerBoostTracker GetTrackerById(int id)
    {
        return _trackerRepository.Get(id);
    }

    public TrackerBoostTracker AddTracker(string url, TrackerSourceType source = TrackerSourceType.Manual, string sourceName = "Manual")
    {
        return AddTrackerInternal(url, source, sourceName);
    }

    private TrackerBoostTracker AddTrackerInternal(string url, TrackerSourceType source, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Tracker URL cannot be empty");
        }

        var cleanUrl = url.Trim();
        var existing = _trackerRepository.FindByUrl(cleanUrl);
        if (existing != null)
        {
            return existing;
        }

        var protocol = TrackerProtocol.Udp;
        if (cleanUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            protocol = TrackerProtocol.Https;
        }
        else if (cleanUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            protocol = TrackerProtocol.Http;
        }

        var host = cleanUrl;
        var port = protocol == TrackerProtocol.Https ? 443 : 80;

        try
        {
            if (Uri.TryCreate(cleanUrl, UriKind.Absolute, out var uri))
            {
                host = uri.Host;
                port = uri.Port > 0 ? uri.Port : (protocol == TrackerProtocol.Https ? 443 : 80);
            }
        }
        catch
        {
            // fallback
        }

        var tracker = new TrackerBoostTracker
        {
            Url = cleanUrl,
            Host = host,
            Port = port,
            Protocol = protocol,
            Status = TrackerHealthStatus.Untested,
            Source = source,
            SourceName = sourceName,
            LatencyMs = 0,
            Enabled = true
        };

        return _trackerRepository.Insert(tracker);
    }

    public void DeleteTracker(int id)
    {
        _trackerRepository.Delete(id);
    }

    public async Task<TrackerBoostStatusSummary> GetStatusSummaryAsync()
    {
        var all = _trackerRepository.All().ToList();
        var settings = GetSettings();
        return await Task.FromResult(new TrackerBoostStatusSummary
        {
            TotalTrackersMonitored = all.Count,
            AliveTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Alive),
            SlowTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Slow),
            OfflineTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Offline),
            UntestedTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Untested),
            ProwlarrTrackersCount = all.Count(t => t.Source == TrackerSourceType.Prowlarr),
            PublicListTrackersCount = all.Count(t => t.Source == TrackerSourceType.PublicList),
            ActiveTorrentTrackersCount = all.Count(t => t.Source == TrackerSourceType.ActiveTorrent),
            TorrentsBoostedCount = _totalTorrentsBoosted,
            ExtraTrackersInjectedCount = _totalTrackersInjected,
            TotalVerifiedMatchesCount = _totalVerifiedMatchesCount,
            AutoBoostEnabled = settings.AutoBoostEnabled,
            AutoHarvestEnabled = settings.AutoHarvestEnabled,
            LastScanTime = _lastScanTime,
            LastHarvestTime = _lastHarvestTime,
            LastProwlarrHarvestTime = _lastProwlarrHarvestTime,
            LastAutoBoostTime = _lastAutoBoostTime
        });
    }

    public async Task<int> HarvestFromActiveDownloadsAsync()
    {
        var discovered = 0;
        try
        {
            var seedarrEntries = _trackerEntryService.All();
            foreach (var entry in seedarrEntries)
            {
                if (IsValidPublicTrackerUrl(entry.Url))
                {
                    var res = AddTrackerInternal(entry.Url, TrackerSourceType.ActiveTorrent, "Seedarr Active Download");
                    if (res != null && res.Id > 0)
                    {
                        discovered++;
                    }
                }
            }

            var clients = _downloadClientFactory.All().Where(c => c.Enable).ToList();
            foreach (var clientDef in clients)
            {
                try
                {
                    var client = _downloadClientFactory.CreateClient(clientDef);
                    var items = client.GetItems();
                    foreach (var item in items)
                    {
                        if (string.IsNullOrWhiteSpace(item.InfoHash))
                        {
                            continue;
                        }

                        var trackers = client.GetTrackers(item.InfoHash);
                        foreach (var trUrl in trackers)
                        {
                            if (IsValidPublicTrackerUrl(trUrl))
                            {
                                var res = AddTrackerInternal(trUrl, TrackerSourceType.ActiveTorrent, $"{clientDef.Name} Swarm Harvest");
                                if (res != null && res.Id > 0)
                                {
                                    discovered++;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to harvest trackers from client {0}", clientDef.Name);
                }
            }

            _lastHarvestTime = DateTime.UtcNow;
            if (discovered > 0)
            {
                _logger.Info("Harvested {0} new public trackers from active download swarms", discovered);
                LogActivity("Success", "Discovery", $"Harvested {discovered} new public tracker(s) from active download clients (qBittorrent, Deluge, Transmission)");
            }
            else
            {
                LogActivity("Info", "Discovery", "Harvested active download clients: all client swarms up to date");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error harvesting trackers from active downloads");
            LogActivity("Error", "Discovery", $"Error harvesting from download clients: {ex.Message}");
        }

        return discovered;
    }

    private async Task<int> HarvestQBitTorrentTrackersAsync(DownloadClientDefinition clientDef)
    {
        var count = 0;
        try
        {
            var baseUrl = $"{(clientDef.UseSsl ? "https" : "http")}://{clientDef.Host}:{clientDef.Port}";
            using var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                CheckCertificateRevocationList = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            using var loginContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", clientDef.Username),
                new KeyValuePair<string, string>("password", clientDef.Password)
            });
            var loginResp = await client.PostAsync($"{baseUrl}/api/v2/auth/login", loginContent);
            if (!loginResp.IsSuccessStatusCode)
            {
                return 0;
            }

            var torrentsResp = await client.GetAsync($"{baseUrl}/api/v2/torrents/info");
            if (!torrentsResp.IsSuccessStatusCode)
            {
                return 0;
            }

            var torrentsJson = await torrentsResp.Content.ReadAsStringAsync();
            using var torrentsDoc = JsonDocument.Parse(torrentsJson);

            foreach (var item in torrentsDoc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("hash", out var hashProp))
                {
                    var hash = hashProp.GetString();
                    if (string.IsNullOrWhiteSpace(hash))
                    {
                        continue;
                    }

                    var trackersResp = await client.GetAsync($"{baseUrl}/api/v2/torrents/trackers?hash={hash}");
                    if (!trackersResp.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var trackersJson = await trackersResp.Content.ReadAsStringAsync();
                    using var trackersDoc = JsonDocument.Parse(trackersJson);
                    foreach (var tr in trackersDoc.RootElement.EnumerateArray())
                    {
                        if (tr.TryGetProperty("url", out var urlProp))
                        {
                            var trUrl = urlProp.GetString();
                            if (IsValidPublicTrackerUrl(trUrl))
                            {
                                AddTrackerInternal(trUrl, TrackerSourceType.ActiveTorrent, $"qBittorrent ({clientDef.Name})");
                                count++;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to query qBittorrent trackers for harvesting");
        }

        return count;
    }

    private static bool IsValidPublicTrackerUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var clean = url.Trim().ToLowerInvariant();
        if (!clean.StartsWith("udp://") && !clean.StartsWith("http://") && !clean.StartsWith("https://"))
        {
            return false;
        }

        if (clean.Contains("localhost") || clean.Contains("127.0.0.1") || clean.Contains("dht:") || clean.Contains("pex:") || clean.Contains("lsd:"))
        {
            return false;
        }

        if (clean.Contains("passkey=") || clean.Contains("authkey=") || clean.Contains("torrentpass="))
        {
            return false;
        }

        return true;
    }

    public async Task<int> HarvestFromProwlarrAsync()
    {
        var harvestedCount = 0;
        try
        {
            var indexers = _indexerRepository.All().ToList();
            var prowlarrIndexers = indexers.Where(i =>
                (i.IndexerType != null && i.IndexerType.Contains("Prowlarr", StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(i.Url) && !string.IsNullOrWhiteSpace(i.ApiKey))).ToList();

            foreach (var prowlarr in prowlarrIndexers)
            {
                if (string.IsNullOrWhiteSpace(prowlarr.Url))
                {
                    continue;
                }

                var baseUrl = prowlarr.Url.TrimEnd('/');
                var requestUrl = $"{baseUrl}/api/v1/indexer";

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                if (!string.IsNullOrWhiteSpace(prowlarr.ApiKey))
                {
                    request.Headers.Add("X-Api-Key", prowlarr.ApiKey);
                }

                var response = await HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var indexerElem in doc.RootElement.EnumerateArray())
                {
                    var privacy = indexerElem.TryGetProperty("privacy", out var pProp) ? pProp.GetString() : "public";
                    if (string.Equals(privacy, "private", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var indexerName = indexerElem.TryGetProperty("name", out var nProp) ? nProp.GetString() : "Prowlarr Indexer";

                    if (indexerElem.TryGetProperty("indexerUrls", out var urlsProp) && urlsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var urlItem in urlsProp.EnumerateArray())
                        {
                            var u = urlItem.GetString();
                            if (IsValidPublicTrackerUrl(u))
                            {
                                AddTrackerInternal(u, TrackerSourceType.Prowlarr, $"Prowlarr ({indexerName})");
                                harvestedCount++;
                            }
                        }
                    }

                    if (indexerElem.TryGetProperty("fields", out var fieldsProp) && fieldsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var field in fieldsProp.EnumerateArray())
                        {
                            if (field.TryGetProperty("name", out var fnProp))
                            {
                                var fn = fnProp.GetString() ?? string.Empty;
                                if (fn.Contains("tracker", StringComparison.OrdinalIgnoreCase) || fn.Contains("announce", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (field.TryGetProperty("value", out var fvProp) && fvProp.ValueKind == JsonValueKind.String)
                                    {
                                        var trackerVal = fvProp.GetString();
                                        if (IsValidPublicTrackerUrl(trackerVal))
                                        {
                                            AddTrackerInternal(trackerVal, TrackerSourceType.Prowlarr, $"Prowlarr ({indexerName})");
                                            harvestedCount++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            _lastProwlarrHarvestTime = DateTime.UtcNow;
            _logger.Info("Harvested {0} trackers from connected Prowlarr indexers", harvestedCount);
            LogActivity(harvestedCount > 0 ? "Success" : "Info", "Discovery", $"Prowlarr sync complete: {harvestedCount} tracker(s) harvested from indexers");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to harvest trackers from Prowlarr");
            LogActivity("Warn", "Discovery", $"Failed to harvest from Prowlarr: {ex.Message}");
        }

        return harvestedCount;
    }

    public async Task<int> HarvestFromCuratedListsAsync()
    {
        var count = 0;
        var feedUrls = new[]
        {
            "https://raw.githubusercontent.com/ngosang/trackerslist/master/trackers_best.txt",
            "https://raw.githubusercontent.com/XIU2/TrackersListCollection/master/best.txt"
        };

        foreach (var feed in feedUrls)
        {
            try
            {
                var content = await HttpClient.GetStringAsync(feed);
                using var reader = new StringReader(content);
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var clean = line.Trim();
                    if (string.IsNullOrWhiteSpace(clean) || clean.StartsWith("#"))
                    {
                        continue;
                    }

                    if (IsValidPublicTrackerUrl(clean))
                    {
                        AddTrackerInternal(clean, TrackerSourceType.PublicList, "Curated Public Feed");
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to download tracker feed from {0}", feed);
                LogActivity("Warn", "Discovery", $"Failed to download tracker feed from {feed}: {ex.Message}");
            }
        }

        LogActivity(count > 0 ? "Success" : "Info", "Discovery", $"Curated list sync complete: {count} new candidate tracker(s) discovered");
        return count;
    }

    public async Task<int> ProbeTrackerHealthAsync()
    {
        var trackers = _trackerRepository.All().Where(t => t.Enabled).ToList();
        var testedCount = 0;

        using var semaphore = new SemaphoreSlim(16);
        var tasks = trackers.Select(async tracker =>
        {
            await semaphore.WaitAsync();
            try
            {
                var sw = Stopwatch.StartNew();
                var isAlive = false;

                if (tracker.Protocol == TrackerProtocol.Udp)
                {
                    isAlive = await ProbeUdpTrackerAsync(tracker.Host, tracker.Port);
                }
                else
                {
                    isAlive = await ProbeHttpTrackerAsync(tracker.Url);
                }

                sw.Stop();
                tracker.LatencyMs = (int)sw.ElapsedMilliseconds;
                tracker.LastScraped = DateTime.UtcNow;

                if (isAlive)
                {
                    tracker.Status = tracker.LatencyMs < 400 ? TrackerHealthStatus.Alive : TrackerHealthStatus.Slow;
                    tracker.LastSuccess = DateTime.UtcNow;
                    tracker.SuccessfulScrapes++;
                    LogActivity(tracker.Status == TrackerHealthStatus.Alive ? "Success" : "Warn", "Health", $"Probe succeeded for {tracker.Url} ({tracker.LatencyMs}ms - {tracker.Status})", tracker.Url);
                }
                else
                {
                    tracker.Status = TrackerHealthStatus.Offline;
                    tracker.FailedScrapes++;
                    LogActivity("Error", "Health", $"Probe failed / connection timeout for {tracker.Url} - marked Offline", tracker.Url);
                }

                _trackerRepository.Update(tracker);
                Interlocked.Increment(ref testedCount);
            }
            catch (Exception ex)
            {
                tracker.Status = TrackerHealthStatus.Offline;
                tracker.FailedScrapes++;
                _trackerRepository.Update(tracker);
                LogActivity("Error", "Health", $"Probe exception for {tracker.Url}: {ex.Message} - marked Offline", tracker.Url);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        _lastScanTime = DateTime.UtcNow;
        LogActivity("Info", "Health", $"Completed health scan of {testedCount} candidate tracker(s)");
        return testedCount;
    }

    private async Task<bool> ProbeUdpTrackerAsync(string host, int port)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            if (addresses.Length == 0)
            {
                return false;
            }

            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 2000;
            client.Client.SendTimeout = 2000;

            var transactionId = Random.Shared.Next();
            var packet = new byte[16];
            BinaryPrimitivesWriteInt64BigEndian(packet, 0, 0x41727101980L);
            BinaryPrimitivesWriteInt32BigEndian(packet, 8, 0);
            BinaryPrimitivesWriteInt32BigEndian(packet, 12, transactionId);

            var endpoint = new IPEndPoint(addresses[0], port);
            await client.SendAsync(packet, packet.Length, endpoint);

            var receiveTask = client.ReceiveAsync();
            var completedTask = await Task.WhenAny(receiveTask, Task.Delay(2500));

            if (completedTask == receiveTask)
            {
                var result = await receiveTask;
                if (result.Buffer.Length >= 8)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ProbeHttpTrackerAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var resp = await HttpClient.SendAsync(req, cts.Token);
            return resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.BadRequest;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(bool Success, int Seeders, int Leechers, int Downloaded)> ScrapeTrackerForHashAsync(TrackerBoostTracker tracker, string infoHash)
    {
        if (tracker == null || string.IsNullOrWhiteSpace(infoHash))
        {
            return (false, 0, 0, 0);
        }

        var cleanHash = infoHash.Trim();
        if (cleanHash.Length != 40)
        {
            return (false, 0, 0, 0);
        }

        try
        {
            if (tracker.Protocol == TrackerProtocol.Udp)
            {
                return await ScrapeUdpTrackerAsync(tracker.Host, tracker.Port, cleanHash);
            }
            else
            {
                return await ScrapeHttpTrackerAsync(tracker.Url, cleanHash);
            }
        }
        catch
        {
            return (false, 0, 0, 0);
        }
    }

    private async Task<(bool Success, int Seeders, int Leechers, int Downloaded)> ScrapeUdpTrackerAsync(string host, int port, string hexHash)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            if (addresses.Length == 0)
            {
                return (false, 0, 0, 0);
            }

            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 2500;
            client.Client.SendTimeout = 2500;

            var endpoint = new IPEndPoint(addresses[0], port);

            var connectTxId = Random.Shared.Next();
            var connectPacket = new byte[16];
            BinaryPrimitivesWriteInt64BigEndian(connectPacket, 0, 0x41727101980L);
            BinaryPrimitivesWriteInt32BigEndian(connectPacket, 8, 0);
            BinaryPrimitivesWriteInt32BigEndian(connectPacket, 12, connectTxId);

            await client.SendAsync(connectPacket, connectPacket.Length, endpoint);

            var connectReceive = client.ReceiveAsync();
            if (await Task.WhenAny(connectReceive, Task.Delay(2500)) != connectReceive)
            {
                return (false, 0, 0, 0);
            }

            var connectResult = await connectReceive;
            if (connectResult.Buffer.Length < 16)
            {
                return (false, 0, 0, 0);
            }

            var action = ReadInt32BigEndian(connectResult.Buffer, 0);
            var respTxId = ReadInt32BigEndian(connectResult.Buffer, 4);
            if (action != 0 || respTxId != connectTxId)
            {
                return (false, 0, 0, 0);
            }

            var connectionId = ReadInt64BigEndian(connectResult.Buffer, 8);

            var scrapeTxId = Random.Shared.Next();
            var hashBytes = Convert.FromHexString(hexHash);
            var scrapePacket = new byte[36];
            BinaryPrimitivesWriteInt64BigEndian(scrapePacket, 0, connectionId);
            BinaryPrimitivesWriteInt32BigEndian(scrapePacket, 8, 2);
            BinaryPrimitivesWriteInt32BigEndian(scrapePacket, 12, scrapeTxId);
            Array.Copy(hashBytes, 0, scrapePacket, 16, 20);

            await client.SendAsync(scrapePacket, scrapePacket.Length, endpoint);

            var scrapeReceive = client.ReceiveAsync();
            if (await Task.WhenAny(scrapeReceive, Task.Delay(2500)) != scrapeReceive)
            {
                return (false, 0, 0, 0);
            }

            var scrapeResult = await scrapeReceive;
            if (scrapeResult.Buffer.Length < 20)
            {
                return (false, 0, 0, 0);
            }

            var scrapeRespAction = ReadInt32BigEndian(scrapeResult.Buffer, 0);
            var scrapeRespTxId = ReadInt32BigEndian(scrapeResult.Buffer, 4);
            if (scrapeRespAction != 2 || scrapeRespTxId != scrapeTxId)
            {
                return (false, 0, 0, 0);
            }

            var seeders = ReadInt32BigEndian(scrapeResult.Buffer, 8);
            var completed = ReadInt32BigEndian(scrapeResult.Buffer, 12);
            var leechers = ReadInt32BigEndian(scrapeResult.Buffer, 16);

            return (true, Math.Max(0, seeders), Math.Max(0, leechers), Math.Max(0, completed));
        }
        catch
        {
            return (false, 0, 0, 0);
        }
    }

    private async Task<(bool Success, int Seeders, int Leechers, int Downloaded)> ScrapeHttpTrackerAsync(string announceUrl, string hexHash)
    {
        try
        {
            if (!announceUrl.Contains("/announce"))
            {
                return (false, 0, 0, 0);
            }

            var hashBytes = Convert.FromHexString(hexHash);
            var encodedHash = string.Concat(hashBytes.Select(b => $"%{b:X2}"));
            var scrapeUrl = announceUrl.Replace("/announce", "/scrape");

            var separator = scrapeUrl.Contains('?') ? "&" : "?";
            var requestUrl = $"{scrapeUrl}{separator}info_hash={encodedHash}";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var resp = await HttpClient.GetAsync(requestUrl, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                return (false, 0, 0, 0);
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0)
            {
                return (false, 0, 0, 0);
            }

            var bObject = BParser.Parse(bytes);
            if (bObject is BDictionary dict && dict.ContainsKey("files") && dict["files"] is BDictionary filesDict)
            {
                foreach (var entry in filesDict)
                {
                    if (entry.Value is BDictionary fileStats)
                    {
                        var complete = fileStats.ContainsKey("complete") && fileStats["complete"] is BNumber c ? (int)c.Value : 0;
                        var incomplete = fileStats.ContainsKey("incomplete") && fileStats["incomplete"] is BNumber ic ? (int)ic.Value : 0;
                        var downloaded = fileStats.ContainsKey("downloaded") && fileStats["downloaded"] is BNumber dl ? (int)dl.Value : 0;

                        return (true, complete, incomplete, downloaded);
                    }
                }
            }

            return (true, 0, 0, 0);
        }
        catch
        {
            return (false, 0, 0, 0);
        }
    }

    public async Task<TorrentTrackerInspectionResult> InspectTorrentTrackersAsync(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return new TorrentTrackerInspectionResult { TorrentId = torrentId };
        }

        return await InspectHashInternalAsync(torrent.Id, torrent.Name, torrent.InfoHash, torrent.IsPrivate);
    }

    public async Task<TorrentTrackerInspectionResult> InspectHashTrackersAsync(string infoHash, string name = "")
    {
        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (torrent != null)
        {
            return await InspectTorrentTrackersAsync(torrent.Id);
        }

        return await InspectHashInternalAsync(0, !string.IsNullOrWhiteSpace(name) ? name : infoHash, infoHash, false);
    }

    private async Task<TorrentTrackerInspectionResult> InspectHashInternalAsync(int torrentId, string torrentName, string infoHash, bool isPrivate)
    {
        var attachedMap = new Dictionary<string, TrackerEntry>();
        if (torrentId > 0)
        {
            attachedMap = _trackerEntryService.GetByTorrentId(torrentId)
                .ToDictionary(t => (t.Url ?? string.Empty).Trim().ToLowerInvariant(), t => t);
        }

        var allKnownTrackers = _trackerRepository.All().Where(t => t.Enabled).ToList();
        var detections = new List<TorrentTrackerDetection>();

        using var semaphore = new SemaphoreSlim(12);
        var tasks = allKnownTrackers.Select(async tracker =>
        {
            await semaphore.WaitAsync();
            try
            {
                var cleanUrl = (tracker.Url ?? string.Empty).Trim().ToLowerInvariant();
                var isAttached = attachedMap.TryGetValue(cleanUrl, out var entry);

                var detection = new TorrentTrackerDetection
                {
                    TrackerId = tracker.Id,
                    TrackerUrl = tracker.Url,
                    TrackerHost = tracker.Host,
                    Protocol = tracker.Protocol,
                    Source = tracker.Source,
                    SourceName = tracker.SourceName,
                    IsAttached = isAttached,
                    LatencyMs = tracker.LatencyMs,
                    HealthStatus = tracker.Status,
                    Seeders = entry?.Seeders ?? 0,
                    Leechers = entry?.Leechers ?? 0
                };

                if (!string.IsNullOrWhiteSpace(infoHash) && !isPrivate)
                {
                    var scrape = await ScrapeTrackerForHashAsync(tracker, infoHash);
                    if (scrape.Success)
                    {
                        detection.Seeders = Math.Max(detection.Seeders, scrape.Seeders);
                        detection.Leechers = Math.Max(detection.Leechers, scrape.Leechers);
                        detection.Downloaded = scrape.Downloaded;
                        detection.IsVerified = scrape.Seeders > 0 || scrape.Leechers > 0 || scrape.Downloaded > 0;

                        if (tracker.Status == TrackerHealthStatus.Untested)
                        {
                            tracker.Status = TrackerHealthStatus.Alive;
                            tracker.LastSuccess = DateTime.UtcNow;
                            tracker.LastScraped = DateTime.UtcNow;
                            _trackerRepository.Update(tracker);
                        }

                        if (detection.IsVerified)
                        {
                            detection.IsDetected = true;
                            detection.DetectionStatus = isAttached
                                ? $"Attached & Active ({detection.Seeders} seeds, {detection.Leechers} leeches)"
                                : $"Verified on Tracker ({detection.Seeders} seeds, {detection.Leechers} leeches)";
                        }
                        else
                        {
                            detection.IsDetected = false;
                            detection.DetectionStatus = isAttached ? "Attached (0 Peers Scraped)" : "Not Registered (0 Peers)";
                        }
                    }
                    else
                    {
                        detection.DetectionStatus = isAttached ? "Attached (Scrape Failed)" : (tracker.Status == TrackerHealthStatus.Offline ? "Offline" : "Unresponsive");
                    }
                }
                else
                {
                    detection.DetectionStatus = isPrivate ? "Protected (Private Torrent)" : (isAttached ? "Attached" : "Available");
                }

                lock (detections)
                {
                    detections.Add(detection);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        foreach (var entry in attachedMap.Values)
        {
            var cleanUrl = (entry.Url ?? string.Empty).Trim().ToLowerInvariant();
            if (!detections.Any(d => (d.TrackerUrl ?? string.Empty).Trim().ToLowerInvariant() == cleanUrl))
            {
                var host = !string.IsNullOrEmpty(entry.Url) && Uri.TryCreate(entry.Url, UriKind.Absolute, out var u) ? u.Host : entry.Url;
                detections.Add(new TorrentTrackerDetection
                {
                    TrackerId = 0,
                    TrackerUrl = entry.Url ?? string.Empty,
                    TrackerHost = host ?? string.Empty,
                    Protocol = (entry.Url != null && entry.Url.StartsWith("udp", StringComparison.OrdinalIgnoreCase)) ? TrackerProtocol.Udp : TrackerProtocol.Http,
                    Source = TrackerSourceType.ActiveTorrent,
                    SourceName = "Torrent Attached Tracker",
                    IsAttached = true,
                    HealthStatus = TrackerHealthStatus.Alive,
                    Seeders = entry.Seeders,
                    Leechers = entry.Leechers,
                    DetectionStatus = isPrivate ? "Protected (Private Tracker Attached)" : "Attached"
                });
            }
        }

        var hasBoost = BoostHistory.TryGetValue(infoHash, out var boostInfo);

        return new TorrentTrackerInspectionResult
        {
            TorrentId = torrentId,
            TorrentName = torrentName,
            InfoHash = infoHash,
            IsPrivate = isPrivate,
            IsBoosted = hasBoost,
            BoostedAt = hasBoost ? boostInfo.BoostedAt : null,
            InjectedTrackersCount = hasBoost ? boostInfo.InjectedTrackers.Count : 0,
            TotalTrackersChecked = detections.Count,
            AttachedTrackersCount = detections.Count(d => d.IsAttached),
            DetectedTrackersCount = detections.Count(d => d.IsDetected),
            VerifiedTrackersCount = detections.Count(d => d.IsVerified && !d.IsAttached),
            Detections = detections.OrderByDescending(d => d.IsAttached)
                .ThenByDescending(d => d.IsVerified)
                .ThenByDescending(d => d.Seeders + d.Leechers)
                .ThenBy(d => d.LatencyMs > 0 ? d.LatencyMs : 9999)
                .ToList()
        };
    }

    public async Task<SwarmBoostResult> BoostTorrentAsync(int torrentId, bool onlyVerified = true)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return new SwarmBoostResult { TorrentId = torrentId, Boosted = false, Message = "Torrent not found" };
        }

        if (torrent.IsPrivate)
        {
            return new SwarmBoostResult
            {
                TorrentId = torrentId,
                TorrentName = torrent.Name,
                InfoHash = torrent.InfoHash,
                IsPrivate = true,
                Boosted = false,
                Message = "Skipped: Private torrents are protected from external tracker injection."
            };
        }

        var inspection = await InspectTorrentTrackersAsync(torrentId);
        var existingTrackers = _trackerEntryService.GetByTorrentId(torrentId)
            .Select(t => (t.Url ?? string.Empty).Trim().ToLowerInvariant())
            .ToHashSet();

        var settings = GetSettings();
        var maxToAdd = settings.MaxTrackersPerTorrent;

        var candidateDetections = inspection.Detections
            .Where(d => !existingTrackers.Contains(d.TrackerUrl.Trim().ToLowerInvariant()))
            .Where(d => !onlyVerified || d.IsVerified)
            .Take(maxToAdd)
            .ToList();

        var addedList = new List<string>();
        var totalSeeders = 0;
        var totalLeechers = 0;

        foreach (var candidate in candidateDetections)
        {
            var entry = new TrackerEntry
            {
                TorrentId = torrentId,
                Url = candidate.TrackerUrl,
                Tier = 1,
                Status = TrackerStatus.Unknown,
                Enabled = true,
                Seeders = candidate.Seeders,
                Leechers = candidate.Leechers,
                AnnounceInterval = 1800,
                MinAnnounceInterval = 900
            };
            _trackerEntryService.Add(entry);
            addedList.Add(candidate.TrackerUrl);
            totalSeeders += candidate.Seeders;
            totalLeechers += candidate.Leechers;

            var tr = _trackerRepository.Get(candidate.TrackerId);
            if (tr != null)
            {
                tr.TotalSwarmsFound++;
                tr.TotalVerifiedTorrents++;
                _trackerRepository.Update(tr);
            }
        }

        if (addedList.Count > 0)
        {
            _totalTorrentsBoosted++;
            _totalTrackersInjected += addedList.Count;
            _totalVerifiedMatchesCount += addedList.Count;

            var clientCount = InjectIntoDownloadClients(torrent.InfoHash, addedList);

            var existingHistory = BoostHistory.GetOrAdd(torrent.InfoHash, _ => (DateTime.UtcNow, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            foreach (var url in addedList)
            {
                existingHistory.InjectedTrackers.Add(url);
            }

            BoostHistory[torrent.InfoHash] = (DateTime.UtcNow, existingHistory.InjectedTrackers);

            _logger.Info(
                "Boosted torrent {0} with {1} verified trackers (+{2} seeds, +{3} leeches) into {4} download client(s)",
                torrent.Name,
                addedList.Count,
                totalSeeders,
                totalLeechers,
                clientCount);

            LogActivity(
                "Success",
                "Inject",
                $"Boosted torrent '{torrent.Name}': injected {addedList.Count} verified tracker(s) (+{totalSeeders} seeds, +{totalLeechers} leeches) into {clientCount} download client(s)",
                infoHash: torrent.InfoHash);

            return new SwarmBoostResult
            {
                TorrentId = torrentId,
                TorrentName = torrent.Name,
                InfoHash = torrent.InfoHash,
                IsPrivate = false,
                Boosted = true,
                AddedTrackersCount = addedList.Count,
                AddedTrackers = addedList,
                TotalSeedersFound = totalSeeders,
                TotalLeechersFound = totalLeechers,
                VerifiedCandidateTrackersCount = candidateDetections.Count,
                Message = $"Injected {addedList.Count} verified alive trackers (+{totalSeeders} seeds, +{totalLeechers} leeches discovered) into Seedarr & download clients."
            };
        }

        LogActivity("Info", "Inject", $"Torrent '{torrent.Name}' checked: no new candidate trackers needed", infoHash: torrent.InfoHash);

        return new SwarmBoostResult
        {
            TorrentId = torrentId,
            TorrentName = torrent.Name,
            InfoHash = torrent.InfoHash,
            IsPrivate = false,
            Boosted = false,
            AddedTrackersCount = 0,
            Message = onlyVerified
                ? "All candidate trackers checked; no new verified trackers with active swarms found."
                : "Swarm already has all optimal trackers attached."
        };
    }

    public async Task<SwarmBoostResult> BoostHashAsync(string infoHash, string name = "", bool onlyVerified = true)
    {
        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (torrent != null)
        {
            return await BoostTorrentAsync(torrent.Id, onlyVerified);
        }

        var inspection = await InspectHashTrackersAsync(infoHash, name);
        var settings = GetSettings();
        var candidateDetections = inspection.Detections
            .Where(d => !onlyVerified || d.IsVerified)
            .Take(settings.MaxTrackersPerTorrent)
            .ToList();

        var trackerUrls = candidateDetections.Select(d => d.TrackerUrl).ToList();
        var clientCount = InjectIntoDownloadClients(infoHash, trackerUrls);

        if (clientCount > 0 && trackerUrls.Count > 0)
        {
            var existingHistory = BoostHistory.GetOrAdd(infoHash, _ => (DateTime.UtcNow, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            foreach (var url in trackerUrls)
            {
                existingHistory.InjectedTrackers.Add(url);
            }

            BoostHistory[infoHash] = (DateTime.UtcNow, existingHistory.InjectedTrackers);
            LogActivity("Success", "Inject", $"Injected {trackerUrls.Count} verified tracker(s) into hash {infoHash} across {clientCount} download client(s)", infoHash: infoHash);
        }

        return new SwarmBoostResult
        {
            TorrentId = 0,
            TorrentName = !string.IsNullOrWhiteSpace(name) ? name : infoHash,
            InfoHash = infoHash,
            Boosted = clientCount > 0 && trackerUrls.Count > 0,
            AddedTrackersCount = trackerUrls.Count,
            AddedTrackers = trackerUrls,
            TotalSeedersFound = candidateDetections.Sum(d => d.Seeders),
            TotalLeechersFound = candidateDetections.Sum(d => d.Leechers),
            Message = trackerUrls.Count > 0
                ? $"Injected {trackerUrls.Count} verified trackers into {clientCount} active download client(s)."
                : "No verified swarms found on candidate trackers."
        };
    }

    public async Task<SwarmBoostResult> InjectTrackerToTorrentAsync(int torrentId, string trackerUrl, bool force = false)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return new SwarmBoostResult { TorrentId = torrentId, Boosted = false, Message = "Torrent not found" };
        }

        if (torrent.IsPrivate && !force)
        {
            LogActivity("Warn", "Inject", $"Injection skipped for private torrent '{torrent.Name}' (BEP 27 protection)", trackerUrl, torrent.InfoHash);
            return new SwarmBoostResult
            {
                TorrentId = torrentId,
                TorrentName = torrent.Name,
                InfoHash = torrent.InfoHash,
                IsPrivate = true,
                Boosted = false,
                Message = "Skipped: Private torrents are protected from external tracker injection."
            };
        }

        var clean = trackerUrl.Trim().ToLowerInvariant();
        var existing = _trackerEntryService.GetByTorrentId(torrentId)
            .Any(t => (t.Url ?? string.Empty).Trim().ToLowerInvariant() == clean);

        if (!existing)
        {
            var entry = new TrackerEntry
            {
                TorrentId = torrentId,
                Url = trackerUrl.Trim(),
                Tier = 1,
                Status = TrackerStatus.Unknown,
                Enabled = true,
                Seeders = 0,
                Leechers = 0,
                AnnounceInterval = 1800,
                MinAnnounceInterval = 900
            };
            _trackerEntryService.Add(entry);
            _totalTrackersInjected++;
        }

        if (string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            torrent.TrackerUrl = trackerUrl.Trim();
            _torrentService.Update(torrent);
        }

        InjectIntoDownloadClients(torrent.InfoHash, new[] { trackerUrl.Trim() });
        ReannounceDownloadClients(torrent.InfoHash);
        LogActivity("Success", "Inject", $"Injected tracker {trackerUrl} into torrent '{torrent.Name}' and triggered immediate reannounce", trackerUrl, torrent.InfoHash);

        return await Task.FromResult(new SwarmBoostResult
        {
            TorrentId = torrentId,
            TorrentName = torrent.Name,
            InfoHash = torrent.InfoHash,
            Boosted = true,
            AddedTrackersCount = 1,
            AddedTrackers = new List<string> { trackerUrl.Trim() },
            Message = $"Injected {trackerUrl} and announced to Seedarr & active download agents."
        });
    }

    public async Task<SwarmBoostResult> InjectTrackerToHashAsync(string infoHash, string trackerUrl, bool force = false)
    {
        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (torrent != null)
        {
            return await InjectTrackerToTorrentAsync(torrent.Id, trackerUrl, force);
        }

        var injected = InjectIntoDownloadClients(infoHash, new[] { trackerUrl.Trim() });
        ReannounceDownloadClients(infoHash);
        LogActivity("Success", "Inject", $"Injected tracker {trackerUrl} into hash {infoHash} across {injected} download client(s) and reannounced", trackerUrl, infoHash);
        return await Task.FromResult(new SwarmBoostResult
        {
            TorrentId = 0,
            TorrentName = infoHash,
            InfoHash = infoHash,
            Boosted = injected > 0,
            AddedTrackersCount = injected > 0 ? 1 : 0,
            AddedTrackers = new List<string> { trackerUrl.Trim() },
            Message = injected > 0 ? $"Injected tracker into {injected} download client(s) and triggered reannounce." : "Injected tracker."
        });
    }

    public async Task<List<SwarmBoostResult>> BoostAllTorrentsAsync(bool onlyVerified = true)
    {
        var results = new List<SwarmBoostResult>();

        var torrents = _torrentService.GetAll().Where(t => !t.IsPrivate).ToList();
        foreach (var t in torrents)
        {
            var res = await BoostTorrentAsync(t.Id, onlyVerified);
            results.Add(res);
        }

        try
        {
            var clients = _downloadClientFactory.All().Where(c => c.Enable).ToList();
            foreach (var clientDef in clients)
            {
                try
                {
                    var provider = CreateDownloadClient(clientDef);
                    if (provider == null)
                    {
                        continue;
                    }

                    var items = provider.GetItems();
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrWhiteSpace(item.InfoHash) && !torrents.Any(t => string.Equals(t.InfoHash, item.InfoHash, StringComparison.OrdinalIgnoreCase)))
                        {
                            var res = await BoostHashAsync(item.InfoHash, item.Title, onlyVerified);
                            results.Add(res);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to boost items for client {0}", clientDef.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to boost download client swarms");
        }

        _lastAutoBoostTime = DateTime.UtcNow;
        return results;
    }

    public async Task<TrackerCrossMatrixResult> GetCrossMatrixAsync()
    {
        var torrents = _torrentService.GetAll();
        var allTrackers = _trackerRepository.All().Where(t => t.Enabled).ToList();

        var torrentMatrix = new List<TorrentMatrixItem>();
        var trackerTorrentsMap = new Dictionary<int, List<string>>();
        foreach (var tr in allTrackers)
        {
            trackerTorrentsMap[tr.Id] = new List<string>();
        }

        foreach (var t in torrents)
        {
            var inspection = await InspectTorrentTrackersAsync(t.Id);
            var item = new TorrentMatrixItem
            {
                TorrentId = t.Id,
                TorrentName = t.Name,
                InfoHash = t.InfoHash,
                IsPrivate = t.IsPrivate,
                IsBoosted = inspection.IsBoosted,
                AttachedTrackersCount = inspection.AttachedTrackersCount,
                VerifiedTrackersCount = inspection.VerifiedTrackersCount,
                Trackers = inspection.Detections.Where(d => d.IsAttached || d.IsVerified).ToList()
            };

            foreach (var d in item.Trackers)
            {
                if (trackerTorrentsMap.TryGetValue(d.TrackerId, out var list))
                {
                    list.Add(t.Name);
                }
            }

            torrentMatrix.Add(item);
        }

        var trackerMatrix = allTrackers.Select(tr => new TrackerMatrixItem
        {
            TrackerId = tr.Id,
            TrackerUrl = tr.Url,
            Host = tr.Host,
            Protocol = tr.Protocol,
            Status = tr.Status,
            LatencyMs = tr.LatencyMs,
            RegisteredTorrentsCount = trackerTorrentsMap.TryGetValue(tr.Id, out var l) ? l.Count : 0,
            RegisteredTorrentNames = trackerTorrentsMap.TryGetValue(tr.Id, out var l2) ? l2 : new List<string>()
        }).OrderByDescending(tr => tr.RegisteredTorrentsCount)
            .ThenByDescending(tr => tr.Status == TrackerHealthStatus.Alive)
            .ToList();

        return new TrackerCrossMatrixResult
        {
            Torrents = torrentMatrix,
            Trackers = trackerMatrix
        };
    }

    public async Task<int> RecoverMissingTrackersAsync()
    {
        var recoveredCount = 0;
        try
        {
            var torrents = _torrentService.GetAll();
            var activeClients = _downloadClientFactory.All().Where(c => c.Enable).ToList();

            foreach (var torrent in torrents)
            {
                var existingEntries = _trackerEntryService.GetByTorrentId(torrent.Id);
                if (existingEntries.Count > 0 && !string.IsNullOrWhiteSpace(torrent.TrackerUrl))
                {
                    continue;
                }

                var existingUrls = existingEntries.Select(t => t.Url.Trim().ToLowerInvariant()).ToHashSet();
                var foundTrackers = false;

                // 1. Query active download clients for attached trackers
                foreach (var clientDef in activeClients)
                {
                    try
                    {
                        var provider = CreateDownloadClient(clientDef);
                        if (provider == null)
                        {
                            continue;
                        }

                        var clientTrackers = provider.GetTrackers(torrent.InfoHash);
                        if (clientTrackers != null && clientTrackers.Count > 0)
                        {
                            var tier = existingEntries.Count + 1;
                            foreach (var trUrl in clientTrackers)
                            {
                                var clean = trUrl.Trim();
                                if (!string.IsNullOrEmpty(clean) && !existingUrls.Contains(clean.ToLowerInvariant()))
                                {
                                    _trackerEntryService.Add(new TrackerEntry
                                    {
                                        TorrentId = torrent.Id,
                                        Url = clean,
                                        Tier = tier++,
                                        Enabled = true
                                    });
                                    existingUrls.Add(clean.ToLowerInvariant());
                                    foundTrackers = true;
                                }
                            }

                            if (string.IsNullOrWhiteSpace(torrent.TrackerUrl) && clientTrackers.Count > 0)
                            {
                                torrent.TrackerUrl = clientTrackers[0].Trim();
                                _torrentService.Update(torrent);
                            }

                            if (foundTrackers)
                            {
                                _logger.Info("Recovered {0} tracker(s) from download client {1} for torrent '{2}' ({3})", clientTrackers.Count, clientDef.Name, torrent.Name, torrent.InfoHash);
                                recoveredCount++;
                                break;
                            }
                        }

                        // Try .torrent export if available
                        var torrentBytes = provider.GetTorrentFile(torrent.InfoHash);
                        if (torrentBytes != null && torrentBytes.Length > 0 && _torrentFileParser != null)
                        {
                            using var ms = new MemoryStream(torrentBytes);
                            var parsed = _torrentFileParser.Parse(ms);
                            if (parsed.AnnounceList != null && parsed.AnnounceList.Count > 0)
                            {
                                var tier = 1;
                                foreach (var tierUrls in parsed.AnnounceList)
                                {
                                    foreach (var url in tierUrls)
                                    {
                                        var clean = url.Trim();
                                        if (!string.IsNullOrEmpty(clean) && !existingUrls.Contains(clean.ToLowerInvariant()))
                                        {
                                            _trackerEntryService.Add(new TrackerEntry
                                            {
                                                TorrentId = torrent.Id,
                                                Url = clean,
                                                Tier = tier,
                                                Enabled = true
                                            });
                                            existingUrls.Add(clean.ToLowerInvariant());
                                            foundTrackers = true;
                                        }
                                    }

                                    tier++;
                                }
                            }
                            else if (!string.IsNullOrEmpty(parsed.AnnounceUrl))
                            {
                                var clean = parsed.AnnounceUrl.Trim();
                                if (!existingUrls.Contains(clean.ToLowerInvariant()))
                                {
                                    _trackerEntryService.Add(new TrackerEntry
                                    {
                                        TorrentId = torrent.Id,
                                        Url = clean,
                                        Tier = 1,
                                        Enabled = true
                                    });
                                    existingUrls.Add(clean.ToLowerInvariant());
                                    foundTrackers = true;
                                }
                            }

                            if (!string.IsNullOrEmpty(parsed.AnnounceUrl) && string.IsNullOrEmpty(torrent.TrackerUrl))
                            {
                                torrent.TrackerUrl = parsed.AnnounceUrl;
                            }

                            torrent.IsPrivate = parsed.IsPrivate;
                            _torrentService.Update(torrent);

                            if (foundTrackers)
                            {
                                recoveredCount++;
                                break;
                            }
                        }
                    }
                    catch (Exception clientEx)
                    {
                        _logger.Debug(clientEx, "Failed to inspect download client {0} for torrent {1}", clientDef.Name, torrent.InfoHash);
                    }
                }

                // 2. If still no trackers and torrent is not private, scrape candidate trackers via TrackerBoost
                if (!foundTrackers && !torrent.IsPrivate)
                {
                    try
                    {
                        var boostRes = await BoostTorrentAsync(torrent.Id, onlyVerified: true);
                        if (boostRes.Boosted && boostRes.AddedTrackersCount > 0)
                        {
                            recoveredCount++;
                        }
                    }
                    catch (Exception boostEx)
                    {
                        _logger.Debug(boostEx, "Failed to auto-boost torrent {0} during recovery", torrent.InfoHash);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to run RecoverMissingTrackersAsync");
        }

        LogActivity("Info", "Discovery", $"Missing tracker recovery finished: {recoveredCount} torrent tracker swarm(s) recovered");
        return recoveredCount;
    }

    public async Task RunOptimizationCycleAsync()
    {
        LogActivity("Info", "Cycle", "Background tracker optimization cycle started");

        await RecoverMissingTrackersAsync();

        var settings = GetSettings();
        if (settings.AutoHarvestEnabled)
        {
            await HarvestFromActiveDownloadsAsync();
        }

        var hasUntested = _trackerRepository.All().Any(t => t.Enabled && t.Status == TrackerHealthStatus.Untested);
        if (hasUntested || _lastScanTime == null || DateTime.UtcNow.Subtract(_lastScanTime.Value).TotalMinutes > 5)
        {
            await ProbeTrackerHealthAsync();
        }

        if (settings.AutoBoostEnabled)
        {
            await BoostAllTorrentsAsync(onlyVerified: settings.OnlyVerified);
        }

        LogActivity("Info", "Cycle", "Background tracker optimization cycle completed successfully");
    }

    public int InjectIntoDownloadClients(string infoHash, IEnumerable<string> trackers)
    {
        if (string.IsNullOrWhiteSpace(infoHash) || trackers == null)
        {
            return 0;
        }

        var count = 0;
        try
        {
            var clients = _downloadClientFactory.All().Where(c => c.Enable).ToList();
            foreach (var clientDef in clients)
            {
                try
                {
                    var provider = CreateDownloadClient(clientDef);
                    if (provider != null && provider.AddTrackers(infoHash, trackers))
                    {
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to add trackers to client {0} for {1}", clientDef.Name, infoHash);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to inject trackers into download clients for {0}", infoHash);
        }

        return count;
    }

    public void ReannounceDownloadClients(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        try
        {
            var clients = _downloadClientFactory.All().Where(c => c.Enable).ToList();
            foreach (var clientDef in clients)
            {
                try
                {
                    var provider = CreateDownloadClient(clientDef);
                    provider?.Reannounce(infoHash);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to reannounce in client {0} for {1}", clientDef.Name, infoHash);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to reannounce torrent {0} across download clients", infoHash);
        }
    }

    private static IDownloadClient CreateDownloadClient(DownloadClientDefinition definition)
    {
        return definition.ClientType switch
        {
            "QBitTorrent" => new NzbDrone.Core.DownloadClients.QBitTorrent.QBitTorrentClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
            },
            "Transmission" => new NzbDrone.Core.DownloadClients.Transmission.TransmissionClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
            },
            "Deluge" => new NzbDrone.Core.DownloadClients.Deluge.DelugeClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
            },
            _ => null
        };
    }

    private static void BinaryPrimitivesWriteInt64BigEndian(byte[] dest, int offset, long value)
    {
        var bytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value));
        Array.Copy(bytes, 0, dest, offset, 8);
    }

    private static void BinaryPrimitivesWriteInt32BigEndian(byte[] dest, int offset, int value)
    {
        var bytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value));
        Array.Copy(bytes, 0, dest, offset, 4);
    }

    private static int ReadInt32BigEndian(byte[] buffer, int offset)
    {
        var value = BitConverter.ToInt32(buffer, offset);
        return IPAddress.NetworkToHostOrder(value);
    }

    private static long ReadInt64BigEndian(byte[] buffer, int offset)
    {
        var value = BitConverter.ToInt64(buffer, offset);
        return IPAddress.NetworkToHostOrder(value);
    }
}
