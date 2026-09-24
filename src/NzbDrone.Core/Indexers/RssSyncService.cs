using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Indexers;

public interface IRssSyncService
{
    int Sync(bool isManual = false);

    Task<int> SyncAsync(bool isManual = false);

    bool MatchesRule(RssRule rule, ReleaseInfo release, DateTime? now = null);

    RssRule GetFirstMatchingRule(IEnumerable<RssRule> rules, ReleaseInfo release, DateTime? now = null);

    List<ReleaseInfo> FilterReleases(IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null);

    List<ReleaseInfo> FilterReleases(int indexerId, IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null);

    bool ShouldSyncIndexer(IndexerDefinition indexer, bool isManual = false, DateTime? now = null);

    List<IndexerDefinition> FilterEligibleIndexers(IEnumerable<IndexerDefinition> indexers, bool isManual = false, DateTime? now = null);

    List<ReleaseInfo> FilterNewReleases(int indexerId, IEnumerable<ReleaseInfo> releases);

    void RecordSeen(int indexerId, ReleaseInfo release, RssSeenStatus status, int? matchedRuleId = null);

    void RecordSeenBatch(int indexerId, IEnumerable<ReleaseInfo> releases, RssSeenStatus status);

    int PurgeSeenReleases(TimeSpan age);

    RssGrabResult GrabRelease(ReleaseInfo release, RssRule rule, string indexerName = null);
}

public class RssGrabResult
{
    public bool Success { get; set; }
    public Torrent Torrent { get; set; }
    public RssGrabHistory GrabHistory { get; set; }
    public string ErrorMessage { get; set; }
}

public class RssSyncService : IRssSyncService
{
    private readonly IRssRuleEvaluator _ruleEvaluator;
    private readonly IIndexerStatusService _indexerStatusService;
    private readonly IRssSeenReleaseRepository _seenReleaseRepository;
    private readonly ITorrentService _torrentService;
    private readonly IRssGrabHistoryRepository _grabHistoryRepository;
    private readonly IIndexerRepository _indexerRepository;
    private readonly IIndexerFactory _indexerFactory;
    private readonly IRssRuleRepository _rssRuleRepository;
    private readonly IDownloadHistoryRepository _downloadHistoryRepository;
    private readonly IDownloadHistoryService _downloadHistoryService;
    private readonly Func<IndexerDefinition, IIndexer> _indexerInstanceFactory;
    private readonly Logger _logger;
    private readonly object _syncLock = new();

    public RssSyncService(
        IRssRuleEvaluator ruleEvaluator = null,
        IIndexerStatusService indexerStatusService = null,
        IRssSeenReleaseRepository seenReleaseRepository = null,
        ITorrentService torrentService = null,
        IRssGrabHistoryRepository grabHistoryRepository = null,
        IIndexerRepository indexerRepository = null,
        IIndexerFactory indexerFactory = null,
        IRssRuleRepository rssRuleRepository = null,
        IDownloadHistoryRepository downloadHistoryRepository = null,
        IDownloadHistoryService downloadHistoryService = null,
        Func<IndexerDefinition, IIndexer> indexerInstanceFactory = null)
    {
        _ruleEvaluator = ruleEvaluator ?? new RssRuleEvaluator();
        _indexerStatusService = indexerStatusService;
        _seenReleaseRepository = seenReleaseRepository;
        _torrentService = torrentService;
        _grabHistoryRepository = grabHistoryRepository;
        _indexerRepository = indexerRepository;
        _indexerFactory = indexerFactory;
        _rssRuleRepository = rssRuleRepository;
        _downloadHistoryRepository = downloadHistoryRepository;
        _downloadHistoryService = downloadHistoryService;
        _indexerInstanceFactory = indexerInstanceFactory;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool MatchesRule(RssRule rule, ReleaseInfo release, DateTime? now = null)
    {
        if (rule == null || release == null)
        {
            return false;
        }

        return _ruleEvaluator.Matches(rule, release, now);
    }

    public RssRule GetFirstMatchingRule(IEnumerable<RssRule> rules, ReleaseInfo release, DateTime? now = null)
    {
        if (rules == null || release == null)
        {
            return null;
        }

        var enabledRules = rules
            .Where(r => r != null && r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToList();

        foreach (var rule in enabledRules)
        {
            if (MatchesRule(rule, release, now))
            {
                return rule;
            }
        }

        return null;
    }

    public List<ReleaseInfo> FilterReleases(IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null)
    {
        var matched = new List<ReleaseInfo>();
        if (releases == null || rules == null)
        {
            return matched;
        }

        var enabledRules = rules
            .Where(r => r != null && r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToList();

        if (enabledRules.Count == 0)
        {
            return matched;
        }

        foreach (var release in releases)
        {
            if (release == null)
            {
                continue;
            }

            foreach (var rule in enabledRules)
            {
                if (MatchesRule(rule, release, now))
                {
                    matched.Add(release);
                    break;
                }
            }
        }

        return matched;
    }

    public List<ReleaseInfo> FilterReleases(int indexerId, IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null)
    {
        var newReleases = FilterNewReleases(indexerId, releases);
        return FilterReleases(newReleases, rules, now);
    }

    public List<ReleaseInfo> FilterNewReleases(int indexerId, IEnumerable<ReleaseInfo> releases)
    {
        if (releases == null)
        {
            return new List<ReleaseInfo>();
        }

        if (_seenReleaseRepository == null)
        {
            return releases.Where(r => r != null).ToList();
        }

        var seenGuids = _seenReleaseRepository.GetSeenGuids(indexerId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenHashes = _seenReleaseRepository.GetSeenInfoHashes(indexerId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = new List<ReleaseInfo>();
        var batchGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var batchHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var release in releases)
        {
            if (release == null)
            {
                continue;
            }

            var guid = !string.IsNullOrWhiteSpace(release.Guid)
                ? release.Guid.Trim()
                : (!string.IsNullOrWhiteSpace(release.DownloadUrl) ? release.DownloadUrl.Trim() : string.Empty);
            var infoHash = string.IsNullOrWhiteSpace(release.InfoHash) ? null : release.InfoHash.Trim().ToLowerInvariant();

            var hasGuid = !string.IsNullOrWhiteSpace(guid);
            var hasInfoHash = !string.IsNullOrWhiteSpace(infoHash);

            if (!hasGuid && !hasInfoHash)
            {
                result.Add(release);
                continue;
            }

            if (hasGuid && (seenGuids.Contains(guid) || batchGuids.Contains(guid)))
            {
                continue;
            }

            if (hasInfoHash && (seenHashes.Contains(infoHash) || batchHashes.Contains(infoHash)))
            {
                continue;
            }

            if (hasGuid)
            {
                batchGuids.Add(guid);
            }

            if (hasInfoHash)
            {
                batchHashes.Add(infoHash);
            }

            result.Add(release);
        }

        return result;
    }

    public void RecordSeen(int indexerId, ReleaseInfo release, RssSeenStatus status, int? matchedRuleId = null)
    {
        if (release == null || _seenReleaseRepository == null)
        {
            return;
        }

        _seenReleaseRepository.MarkSeen(indexerId, release, status, matchedRuleId);
    }

    public void RecordSeenRelease(int indexerId, ReleaseInfo release, RssSeenStatus status, int? matchedRuleId = null)
    {
        RecordSeen(indexerId, release, status, matchedRuleId);
    }

    public void RecordSeenBatch(int indexerId, IEnumerable<ReleaseInfo> releases, RssSeenStatus status)
    {
        if (releases == null || _seenReleaseRepository == null)
        {
            return;
        }

        _seenReleaseRepository.MarkSeenBatch(indexerId, releases, status);
    }

    public void RecordSeenReleases(int indexerId, IEnumerable<ReleaseInfo> releases, RssSeenStatus status)
    {
        RecordSeenBatch(indexerId, releases, status);
    }

    public int PurgeSeenReleases(TimeSpan age)
    {
        return _seenReleaseRepository?.PurgeOlderThan(age) ?? 0;
    }

    public bool ShouldSyncIndexer(IndexerDefinition indexer, bool isManual = false, DateTime? now = null)
    {
        if (indexer == null || !indexer.Enable || !indexer.EnableRss)
        {
            return false;
        }

        if (isManual)
        {
            return true;
        }

        if (_indexerStatusService == null)
        {
            return true;
        }

        if (_indexerStatusService.IsDisabled(indexer.Id))
        {
            return false;
        }

        var status = _indexerStatusService.GetStatus(indexer.Id);
        var current = now ?? DateTime.UtcNow;
        if (status.NextRssSyncTimeUtc.HasValue && status.NextRssSyncTimeUtc.Value > current)
        {
            return false;
        }

        return true;
    }

    public List<IndexerDefinition> FilterEligibleIndexers(IEnumerable<IndexerDefinition> indexers, bool isManual = false, DateTime? now = null)
    {
        if (indexers == null)
        {
            return new List<IndexerDefinition>();
        }

        return indexers.Where(i => ShouldSyncIndexer(i, isManual, now)).ToList();
    }

    public int Sync(bool isManual = false)
    {
        lock (_syncLock)
        {
            _logger.Info("Starting RSS sync cycle (manual: {0})", isManual);

            var indexers = (_indexerFactory?.All() ?? _indexerRepository?.All())?.ToList();
            if (indexers == null || indexers.Count == 0)
            {
                _logger.Debug("No indexers configured for RSS sync");
                return 0;
            }

            var eligibleIndexers = FilterEligibleIndexers(indexers, isManual);
            if (eligibleIndexers.Count == 0)
            {
                _logger.Debug("No eligible indexers available for RSS sync");
                return 0;
            }

            var rules = _rssRuleRepository?.All() ?? new List<RssRule>();
            var enabledRules = rules
                .Where(r => r != null && r.IsEnabled)
                .OrderBy(r => r.Priority)
                .ThenBy(r => r.Id)
                .ToList();

            var grabbedCount = 0;
            var grabbedHashesInCycle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var indexerDef in eligibleIndexers)
            {
                try
                {
                    var indexer = GetIndexerInstance(indexerDef);
                    if (indexer == null)
                    {
                        _logger.Warn("Unable to create indexer instance for '{0}' ({1})", indexerDef.Name, indexerDef.IndexerType);
                        continue;
                    }

                    _logger.Debug("Fetching RSS releases from indexer '{0}'", indexerDef.Name);

                    var query = new SearchQuery
                    {
                        Mode = SearchMode.Rss,
                        Limit = 100
                    };

                    List<ReleaseInfo> releases;
                    try
                    {
                        releases = indexer.Search(indexerDef, query) ?? new List<ReleaseInfo>();
                        _indexerStatusService?.RecordSuccess(indexerDef.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to fetch RSS releases from indexer '{0}'", indexerDef.Name);
                        _indexerStatusService?.RecordFailure(indexerDef.Id, null, ex.Message, ex);
                        continue;
                    }

                    foreach (var rel in releases)
                    {
                        if (rel.IndexerId <= 0)
                        {
                            rel.IndexerId = indexerDef.Id;
                        }

                        if (string.IsNullOrWhiteSpace(rel.Indexer))
                        {
                            rel.Indexer = indexerDef.Name;
                        }
                    }

                    var newReleases = FilterNewReleases(indexerDef.Id, releases);

                    foreach (var release in newReleases)
                    {
                        if (release == null)
                        {
                            continue;
                        }

                        var infoHash = release.InfoHash;
                        if (string.IsNullOrWhiteSpace(infoHash) && !string.IsNullOrWhiteSpace(release.MagnetUrl))
                        {
                            try
                            {
                                infoHash = MagnetLinkParser.Parse(release.MagnetUrl)?.InfoHash;
                            }
                            catch (Exception ex)
                            {
                                _logger.Trace(ex, "Failed to parse magnet link for release '{0}'", release.Title);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(infoHash))
                        {
                            release.InfoHash = infoHash.Trim().ToLowerInvariant();
                        }

                        var isDuplicate = false;
                        if (!string.IsNullOrWhiteSpace(release.InfoHash))
                        {
                            if (grabbedHashesInCycle.Contains(release.InfoHash))
                            {
                                isDuplicate = true;
                            }
                            else if (_torrentService != null && _torrentService.ExistsByInfoHash(release.InfoHash))
                            {
                                isDuplicate = true;
                            }
                            else if (_downloadHistoryRepository != null && _downloadHistoryRepository.FindByInfoHash(release.InfoHash) != null)
                            {
                                isDuplicate = true;
                            }
                            else if (_downloadHistoryService != null && _downloadHistoryService.GetByInfoHash(release.InfoHash) != null)
                            {
                                isDuplicate = true;
                            }
                        }

                        if (isDuplicate)
                        {
                            _logger.Debug("Release '{0}' ({1}) is already grabbed or active; skipping duplicate.", release.Title, release.InfoHash);
                            RecordSeen(indexerDef.Id, release, RssSeenStatus.Grabbed);
                            continue;
                        }

                        var matchedRule = GetFirstMatchingRule(enabledRules, release);
                        if (matchedRule != null)
                        {
                            _logger.Info("Release '{0}' matched rule '{1}'. Initiating grab.", release.Title, matchedRule.Name);
                            var grabResult = GrabRelease(release, matchedRule, indexerDef.Name);
                            if (grabResult.Success)
                            {
                                grabbedCount++;
                                if (!string.IsNullOrWhiteSpace(release.InfoHash))
                                {
                                    grabbedHashesInCycle.Add(release.InfoHash);
                                }
                            }
                        }
                        else
                        {
                            RecordSeen(indexerDef.Id, release, RssSeenStatus.Rejected);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error processing RSS sync for indexer '{0}'", indexerDef.Name);
                }
            }

            _logger.Info("RSS sync cycle completed. Total releases grabbed: {0}", grabbedCount);
            return grabbedCount;
        }
    }

    public Task<int> SyncAsync(bool isManual = false)
    {
        return Task.Run(() => Sync(isManual));
    }

    private IIndexer GetIndexerInstance(IndexerDefinition definition)
    {
        if (_indexerInstanceFactory != null)
        {
            return _indexerInstanceFactory(definition);
        }

        if (_indexerFactory != null)
        {
            var available = _indexerFactory.GetAvailableProviders();
            var matched = available?.FirstOrDefault(p =>
                string.Equals(p.GetType().Name, definition.Implementation, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.IndexerType, definition.IndexerType, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }
        }

        return definition.IndexerType?.ToLowerInvariant() switch
        {
            "newznab" => new Newznab.NewznabIndexer(indexerStatusService: _indexerStatusService),
            "prowlarr" => new Prowlarr.ProwlarrIndexer(indexerStatusService: _indexerStatusService),
            _ => new Torznab.TorznabIndexer(indexerStatusService: _indexerStatusService)
        };
    }

    public RssGrabResult GrabRelease(ReleaseInfo release, RssRule rule, string indexerName = null)
    {
        if (release == null || rule == null)
        {
            return new RssGrabResult { Success = false, ErrorMessage = "Release or rule cannot be null" };
        }

        var history = new RssGrabHistory
        {
            ReleaseTitle = release.Title,
            IndexerName = !string.IsNullOrWhiteSpace(indexerName) ? indexerName : release.Indexer,
            RuleId = rule.Id,
            RuleName = rule.Name,
            InfoHash = release.InfoHash,
            Size = release.Size,
            GrabTimestamp = DateTime.UtcNow
        };

        try
        {
            if (_torrentService == null)
            {
                history.Status = RssGrabHistory.StatusFailed;
                history.ErrorMessage = "Torrent service is not configured";
                _grabHistoryRepository?.Insert(history);
                return new RssGrabResult { Success = false, GrabHistory = history, ErrorMessage = history.ErrorMessage };
            }

            var infoHash = release.InfoHash;
            string trackerUrl = null;
            if (!string.IsNullOrWhiteSpace(release.MagnetUrl) || (!string.IsNullOrWhiteSpace(release.DownloadUrl) && release.DownloadUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)))
            {
                var magnetStr = !string.IsNullOrWhiteSpace(release.MagnetUrl) ? release.MagnetUrl : release.DownloadUrl;
                try
                {
                    var parsedMagnet = MagnetLinkParser.Parse(magnetStr);
                    if (string.IsNullOrWhiteSpace(infoHash))
                    {
                        infoHash = parsedMagnet.InfoHash;
                    }

                    if (parsedMagnet.Trackers.Length > 0)
                    {
                        trackerUrl = parsedMagnet.Trackers[0];
                    }
                }
                catch
                {
                    // Fallback if parsing fails
                }
            }

            if (!string.IsNullOrWhiteSpace(infoHash))
            {
                infoHash = infoHash.Trim().ToLowerInvariant();
            }

            history.InfoHash = infoHash;

            var existsInHistory = (_downloadHistoryRepository != null && !string.IsNullOrWhiteSpace(infoHash) && _downloadHistoryRepository.FindByInfoHash(infoHash) != null)
                || (_downloadHistoryService != null && !string.IsNullOrWhiteSpace(infoHash) && _downloadHistoryService.GetByInfoHash(infoHash) != null);

            if (!string.IsNullOrWhiteSpace(infoHash) && ((_torrentService != null && _torrentService.ExistsByInfoHash(infoHash)) || existsInHistory))
            {
                history.Status = RssGrabHistory.StatusFailed;
                history.ErrorMessage = existsInHistory
                    ? "Torrent with this info hash already exists in download history"
                    : "Torrent with this info hash already exists in active library";
                _grabHistoryRepository?.Insert(history);
                RecordSeen(release.IndexerId, release, RssSeenStatus.Grabbed, rule.Id);
                return new RssGrabResult { Success = false, GrabHistory = history, ErrorMessage = history.ErrorMessage };
            }

            var torrent = new Torrent
            {
                Name = release.Title,
                InfoHash = infoHash,
                TrackerUrl = trackerUrl,
                TotalSize = release.Size,
                DateAdded = DateTime.UtcNow,
                SavePath = rule.SavePath,
                TagIds = rule.Tags != null ? new List<int>(rule.Tags) : new List<int>(),
                SequentialDownload = rule.SequentialDownload,
                Status = rule.InitialStatus ?? TorrentStatus.Queued,
                Category = rule.CategoryId > 0 ? rule.CategoryId.ToString() : null,
                Seeders = release.Seeders ?? 0,
                Leechers = release.Leechers ?? 0
            };

            var added = _torrentService.Add(torrent);

            history.Status = RssGrabHistory.StatusGrabbed;
            _grabHistoryRepository?.Insert(history);
            RecordSeen(release.IndexerId, release, RssSeenStatus.Grabbed, rule.Id);

            if (_downloadHistoryService != null)
            {
                _downloadHistoryService.RecordTorrentAdded(added, source: "RSS", magnetUrl: release.MagnetUrl, downloadUrl: release.DownloadUrl, indexerName: history.IndexerName);
            }
            else if (_downloadHistoryRepository != null && !string.IsNullOrWhiteSpace(infoHash) && _downloadHistoryRepository.FindByInfoHash(infoHash) == null)
            {
                _downloadHistoryRepository.Insert(new DownloadHistory
                {
                    TorrentId = added?.Id,
                    Title = release.Title,
                    InfoHash = infoHash,
                    TotalSize = release.Size,
                    DateAdded = DateTime.UtcNow,
                    Source = "RSS",
                    IndexerName = history.IndexerName,
                    MagnetUrl = release.MagnetUrl,
                    DownloadUrl = release.DownloadUrl,
                    Status = "Active"
                });
            }

            return new RssGrabResult
            {
                Success = true,
                Torrent = added,
                GrabHistory = history
            };
        }
        catch (Exception ex)
        {
            history.Status = RssGrabHistory.StatusFailed;
            history.ErrorMessage = ex.Message;
            _grabHistoryRepository?.Insert(history);
            RecordSeen(release.IndexerId, release, RssSeenStatus.Rejected, rule.Id);

            return new RssGrabResult
            {
                Success = false,
                GrabHistory = history,
                ErrorMessage = ex.Message
            };
        }
    }
}
