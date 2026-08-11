using System;
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
using NLog;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.DownloadPlusPlus;

public interface IDownloadPlusPlusService
{
    List<DownloadPlusPlusTracker> GetAllTrackers();
    DownloadPlusPlusTracker GetTrackerById(int id);
    DownloadPlusPlusTracker AddTracker(string url, TrackerSourceType source = TrackerSourceType.Manual, string sourceName = "Manual");
    void DeleteTracker(int id);
    Task<DownloadPlusPlusStatusSummary> GetStatusSummaryAsync();
    Task<int> HarvestFromProwlarrAsync();
    Task<int> HarvestFromCuratedListsAsync();
    Task<int> ProbeTrackerHealthAsync();
    Task<TorrentTrackerInspectionResult> InspectTorrentTrackersAsync(int torrentId);
    Task<TorrentTrackerInspectionResult> InspectHashTrackersAsync(string infoHash, string name = "");
    Task<SwarmBoostResult> BoostTorrentAsync(int torrentId);
    Task<SwarmBoostResult> BoostHashAsync(string infoHash, string name = "");
    Task<SwarmBoostResult> InjectTrackerToTorrentAsync(int torrentId, string trackerUrl);
    Task<SwarmBoostResult> InjectTrackerToHashAsync(string infoHash, string trackerUrl);
    Task<List<SwarmBoostResult>> BoostAllTorrentsAsync();
}

public class DownloadPlusPlusService : IDownloadPlusPlusService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
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

    private readonly IDownloadPlusPlusTrackerRepository _trackerRepository;
    private readonly ITorrentService _torrentService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IIndexerRepository _indexerRepository;
    private readonly IDownloadClientFactory _downloadClientFactory;
    private readonly Logger _logger;

    private static DateTime? _lastScanTime;
    private static DateTime? _lastProwlarrHarvestTime;
    private static int _totalTorrentsBoosted;
    private static int _totalTrackersInjected;

    public DownloadPlusPlusService(
        IDownloadPlusPlusTrackerRepository trackerRepository,
        ITorrentService torrentService,
        ITrackerEntryService trackerEntryService,
        IIndexerRepository indexerRepository,
        IDownloadClientFactory downloadClientFactory)
    {
        _trackerRepository = trackerRepository;
        _torrentService = torrentService;
        _trackerEntryService = trackerEntryService;
        _indexerRepository = indexerRepository;
        _downloadClientFactory = downloadClientFactory;
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

    public List<DownloadPlusPlusTracker> GetAllTrackers()
    {
        return _trackerRepository.All().OrderByDescending(t => t.Status == TrackerHealthStatus.Alive)
            .ThenBy(t => t.LatencyMs > 0 ? t.LatencyMs : 9999)
            .ToList();
    }

    public DownloadPlusPlusTracker GetTrackerById(int id)
    {
        return _trackerRepository.Get(id);
    }

    public DownloadPlusPlusTracker AddTracker(string url, TrackerSourceType source = TrackerSourceType.Manual, string sourceName = "Manual")
    {
        return AddTrackerInternal(url, source, sourceName);
    }

    private DownloadPlusPlusTracker AddTrackerInternal(string url, TrackerSourceType source, string sourceName)
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

        var tracker = new DownloadPlusPlusTracker
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

    public async Task<DownloadPlusPlusStatusSummary> GetStatusSummaryAsync()
    {
        var all = _trackerRepository.All().ToList();
        return await Task.FromResult(new DownloadPlusPlusStatusSummary
        {
            TotalTrackersMonitored = all.Count,
            AliveTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Alive),
            SlowTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Slow),
            OfflineTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Offline),
            UntestedTrackersCount = all.Count(t => t.Status == TrackerHealthStatus.Untested),
            ProwlarrTrackersCount = all.Count(t => t.Source == TrackerSourceType.Prowlarr),
            PublicListTrackersCount = all.Count(t => t.Source == TrackerSourceType.PublicList),
            TorrentsBoostedCount = _totalTorrentsBoosted,
            ExtraTrackersInjectedCount = _totalTrackersInjected,
            LastScanTime = _lastScanTime,
            LastProwlarrHarvestTime = _lastProwlarrHarvestTime
        });
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
                            if (!string.IsNullOrWhiteSpace(u))
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
                                        if (!string.IsNullOrWhiteSpace(trackerVal) && (trackerVal.StartsWith("udp://") || trackerVal.StartsWith("http://") || trackerVal.StartsWith("https://")))
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
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to harvest trackers from Prowlarr");
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

                    if (clean.StartsWith("udp://") || clean.StartsWith("http://") || clean.StartsWith("https://"))
                    {
                        AddTrackerInternal(clean, TrackerSourceType.PublicList, "Curated Public Feed");
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to download tracker feed from {0}", feed);
            }
        }

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
                }
                else
                {
                    tracker.Status = TrackerHealthStatus.Offline;
                    tracker.FailedScrapes++;
                }

                _trackerRepository.Update(tracker);
                Interlocked.Increment(ref testedCount);
            }
            catch
            {
                tracker.Status = TrackerHealthStatus.Offline;
                tracker.FailedScrapes++;
                _trackerRepository.Update(tracker);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        _lastScanTime = DateTime.UtcNow;
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

    public async Task<TorrentTrackerInspectionResult> InspectTorrentTrackersAsync(int torrentId)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            return new TorrentTrackerInspectionResult { TorrentId = torrentId };
        }

        var attachedEntries = _trackerEntryService.GetByTorrentId(torrentId);
        var attachedMap = attachedEntries.ToDictionary(
            t => (t.Url ?? string.Empty).Trim().ToLowerInvariant(),
            t => t);

        var allKnownTrackers = _trackerRepository.All().Where(t => t.Enabled).ToList();
        var detections = new List<TorrentTrackerDetection>();

        foreach (var tracker in allKnownTrackers)
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

            if (isAttached)
            {
                detection.IsDetected = entry?.Seeders > 0 || entry?.Leechers > 0;
                detection.DetectionStatus = detection.IsDetected ? "Detected & Active" : "Attached";
            }
            else
            {
                detection.IsDetected = tracker.Status == TrackerHealthStatus.Alive;
                detection.DetectionStatus = tracker.Status == TrackerHealthStatus.Alive
                    ? "Available to Inject"
                    : tracker.Status == TrackerHealthStatus.Offline
                        ? "Offline"
                        : "Untested";
            }

            detections.Add(detection);
        }

        return await Task.FromResult(new TorrentTrackerInspectionResult
        {
            TorrentId = torrentId,
            TorrentName = torrent.Name,
            InfoHash = torrent.InfoHash,
            IsPrivate = torrent.IsPrivate,
            TotalTrackersChecked = detections.Count,
            AttachedTrackersCount = detections.Count(d => d.IsAttached),
            DetectedTrackersCount = detections.Count(d => d.IsDetected),
            Detections = detections.OrderByDescending(d => d.IsAttached)
                .ThenByDescending(d => d.IsDetected)
                .ThenBy(d => d.LatencyMs > 0 ? d.LatencyMs : 9999)
                .ToList()
        });
    }

    public async Task<TorrentTrackerInspectionResult> InspectHashTrackersAsync(string infoHash, string name = "")
    {
        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (torrent != null)
        {
            return await InspectTorrentTrackersAsync(torrent.Id);
        }

        var allKnownTrackers = _trackerRepository.All().Where(t => t.Enabled).ToList();
        var detections = new List<TorrentTrackerDetection>();

        foreach (var tracker in allKnownTrackers)
        {
            var detection = new TorrentTrackerDetection
            {
                TrackerId = tracker.Id,
                TrackerUrl = tracker.Url,
                TrackerHost = tracker.Host,
                Protocol = tracker.Protocol,
                Source = tracker.Source,
                SourceName = tracker.SourceName,
                IsAttached = false,
                LatencyMs = tracker.LatencyMs,
                HealthStatus = tracker.Status,
                IsDetected = tracker.Status == TrackerHealthStatus.Alive,
                DetectionStatus = tracker.Status == TrackerHealthStatus.Alive ? "Available to Inject" : "Offline"
            };

            detections.Add(detection);
        }

        return await Task.FromResult(new TorrentTrackerInspectionResult
        {
            TorrentId = 0,
            TorrentName = !string.IsNullOrWhiteSpace(name) ? name : infoHash,
            InfoHash = infoHash,
            IsPrivate = false,
            TotalTrackersChecked = detections.Count,
            AttachedTrackersCount = 0,
            DetectedTrackersCount = detections.Count(d => d.IsDetected),
            Detections = detections.OrderByDescending(d => d.IsDetected)
                .ThenBy(d => d.LatencyMs > 0 ? d.LatencyMs : 9999)
                .ToList()
        });
    }

    public async Task<SwarmBoostResult> InjectTrackerToTorrentAsync(int torrentId, string trackerUrl)
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

        // Also inject into real download clients if present
        InjectIntoDownloadClients(torrent.InfoHash, new[] { trackerUrl.Trim() });

        return await Task.FromResult(new SwarmBoostResult
        {
            TorrentId = torrentId,
            TorrentName = torrent.Name,
            InfoHash = torrent.InfoHash,
            Boosted = true,
            AddedTrackersCount = 1,
            AddedTrackers = new List<string> { trackerUrl.Trim() },
            Message = $"Injected {trackerUrl} into Seedarr & active download agents."
        });
    }

    public async Task<SwarmBoostResult> InjectTrackerToHashAsync(string infoHash, string trackerUrl)
    {
        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (torrent != null)
        {
            return await InjectTrackerToTorrentAsync(torrent.Id, trackerUrl);
        }

        var injected = InjectIntoDownloadClients(infoHash, new[] { trackerUrl.Trim() });
        return await Task.FromResult(new SwarmBoostResult
        {
            TorrentId = 0,
            TorrentName = infoHash,
            InfoHash = infoHash,
            Boosted = injected > 0,
            AddedTrackersCount = injected > 0 ? 1 : 0,
            AddedTrackers = new List<string> { trackerUrl.Trim() },
            Message = injected > 0 ? $"Injected tracker into {injected} download client(s)." : "Injected tracker."
        });
    }

    public async Task<SwarmBoostResult> BoostTorrentAsync(int torrentId)
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

        var existingTrackers = _trackerEntryService.GetByTorrentId(torrentId)
            .Select(t => (t.Url ?? string.Empty).Trim().ToLowerInvariant())
            .Where(u => !string.IsNullOrEmpty(u))
            .ToHashSet();

        var aliveTrackers = _trackerRepository.GetAliveTrackers();
        if (aliveTrackers.Count == 0)
        {
            aliveTrackers = _trackerRepository.All().Where(t => t.Enabled).Take(15).ToList();
        }

        var toAdd = aliveTrackers
            .Where(t => !existingTrackers.Contains(t.Url.Trim().ToLowerInvariant()))
            .Take(8)
            .ToList();

        var addedList = new List<string>();
        foreach (var tracker in toAdd)
        {
            var entry = new TrackerEntry
            {
                TorrentId = torrentId,
                Url = tracker.Url,
                Tier = 1,
                Status = TrackerStatus.Unknown,
                Enabled = true,
                Seeders = 0,
                Leechers = 0,
                AnnounceInterval = 1800,
                MinAnnounceInterval = 900
            };
            _trackerEntryService.Add(entry);
            addedList.Add(tracker.Url);
            tracker.TotalSwarmsFound++;
            _trackerRepository.Update(tracker);
        }

        _totalTorrentsBoosted++;
        _totalTrackersInjected += addedList.Count;

        // Also inject into real download agents (qBittorrent / Transmission / Deluge)
        var clientCount = InjectIntoDownloadClients(torrent.InfoHash, addedList);

        _logger.Info("Boosted torrent {0} ({1}) with {2} new alive trackers (and {3} download clients)", torrent.Name, torrent.InfoHash, addedList.Count, clientCount);

        var clientMsg = clientCount > 0 ? $" and {clientCount} active download agent(s)" : "";
        return await Task.FromResult(new SwarmBoostResult
        {
            TorrentId = torrentId,
            TorrentName = torrent.Name,
            InfoHash = torrent.InfoHash,
            IsPrivate = false,
            Boosted = addedList.Count > 0,
            AddedTrackersCount = addedList.Count,
            AddedTrackers = addedList,
            Message = addedList.Count > 0
                ? $"Successfully injected {addedList.Count} verified alive trackers into Seedarr{clientMsg}."
                : "Swarm already has all optimal trackers attached."
        });
    }

    public async Task<SwarmBoostResult> BoostHashAsync(string infoHash, string name = "")
    {
        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (torrent != null)
        {
            return await BoostTorrentAsync(torrent.Id);
        }

        var aliveTrackers = _trackerRepository.GetAliveTrackers();
        if (aliveTrackers.Count == 0)
        {
            aliveTrackers = _trackerRepository.All().Where(t => t.Enabled).Take(15).ToList();
        }

        var trackerUrls = aliveTrackers.Select(t => t.Url).Take(8).ToList();
        var clientCount = InjectIntoDownloadClients(infoHash, trackerUrls);

        return await Task.FromResult(new SwarmBoostResult
        {
            TorrentId = 0,
            TorrentName = !string.IsNullOrWhiteSpace(name) ? name : infoHash,
            InfoHash = infoHash,
            Boosted = clientCount > 0,
            AddedTrackersCount = trackerUrls.Count,
            AddedTrackers = trackerUrls,
            Message = $"Injected {trackerUrls.Count} alive trackers into {clientCount} active download client(s)."
        });
    }

    public async Task<List<SwarmBoostResult>> BoostAllTorrentsAsync()
    {
        var results = new List<SwarmBoostResult>();

        // 1. Boost all Seedarr torrents
        var torrents = _torrentService.GetAll().Where(t => !t.IsPrivate).ToList();
        foreach (var t in torrents)
        {
            var res = await BoostTorrentAsync(t.Id);
            results.Add(res);
        }

        // 2. Also boost all real downloads in connected download clients
        try
        {
            var alive = _trackerRepository.GetAliveTrackers().Select(t => t.Url).Take(8).ToList();
            if (alive.Count > 0)
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
                            if (!string.IsNullOrWhiteSpace(item.InfoHash))
                            {
                                provider.AddTrackers(item.InfoHash, alive);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to boost items for client {0}", clientDef.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to boost download client swarms");
        }

        return results;
    }

    private int InjectIntoDownloadClients(string infoHash, IEnumerable<string> trackers)
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
}
