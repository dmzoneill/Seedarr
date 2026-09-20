using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Torrents;

public class WatchFolderService : BackgroundService
{
    private readonly ITorrentFileParser _parser;
    private readonly ITorrentService _torrentService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IConfigService _configService;
    private readonly ICategoryService _categoryService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _fileDebounceTokens = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher _watcher;

    public WatchFolderService(
        ITorrentFileParser parser,
        ITorrentService torrentService,
        ITrackerEntryService trackerEntryService,
        ITorrentFileService torrentFileService,
        IAppFolderInfo appFolderInfo,
        IConfigService configService,
        ICategoryService categoryService = null)
    {
        _parser = parser;
        _torrentService = torrentService;
        _trackerEntryService = trackerEntryService;
        _torrentFileService = torrentFileService;
        _appFolderInfo = appFolderInfo;
        _configService = configService;
        _categoryService = categoryService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configService.WatchFolderEnabled)
        {
            _logger.Info("Watch folder service is disabled via configuration");
            return;
        }

        var watchPath = GetWatchPath();

        try
        {
            if (!Directory.Exists(watchPath))
            {
                Directory.CreateDirectory(watchPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Unable to create watch folder at {0}, watch folder service disabled", watchPath);
            return;
        }

        _logger.Info("Watching folder: {0}", watchPath);

        try
        {
            _watcher = new FileSystemWatcher(watchPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnTorrentFileCreated;
            _watcher.Renamed += OnTorrentFileRenamed;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Unable to initialize FileSystemWatcher for {0}. Falling back to periodic scan only.", watchPath);
            _watcher = null;
        }

        stoppingToken.Register(() =>
        {
            if (_watcher != null)
            {
                try
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                }
                catch
                {
                }
            }
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            PeriodicScan(watchPath);

            var scanInterval = Math.Max(1, _configService.WatchFolderScanIntervalSeconds);
            var interval = TimeSpan.FromSeconds(scanInterval);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void PeriodicScan(string watchPath)
    {
        try
        {
            if (!Directory.Exists(watchPath))
            {
                return;
            }

            string[] candidateFiles;
            try
            {
                candidateFiles = Directory.GetFiles(watchPath, "*.*", SearchOption.AllDirectories)
                    .Where(IsCandidateFile)
                    .ToArray();
            }
            catch (DirectoryNotFoundException)
            {
                _logger.Debug("Watch folder directory not found during scan: {0}", watchPath);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Warn(ex, "Access denied scanning watch folder at {0}", watchPath);
                return;
            }

            foreach (var filePath in candidateFiles)
            {
                ProcessFile(filePath, watchPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during periodic scan of watch folder");
        }
    }

    private void OnTorrentFileCreated(object sender, FileSystemEventArgs e)
    {
        if (IsCandidateFile(e.FullPath))
        {
            _ = HandleTorrentFileCreatedAsync(e.FullPath);
        }
    }

    private void OnTorrentFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsCandidateFile(e.FullPath))
        {
            _ = HandleTorrentFileCreatedAsync(e.FullPath);
        }
    }

    private async Task HandleTorrentFileCreatedAsync(string filePath)
    {
        var newCts = new CancellationTokenSource();

        var oldCts = _fileDebounceTokens.AddOrUpdate(
            filePath,
            newCts,
            (_, existing) =>
            {
                existing.Cancel();
                existing.Dispose();
                return newCts;
            });

        if (oldCts != null && oldCts != newCts)
        {
            await oldCts.CancelAsync();
            oldCts.Dispose();
        }

        try
        {
            await Task.Delay(500, newCts.Token);
            _fileDebounceTokens.TryRemove(filePath, out _);
            ProcessFile(filePath);
        }
        catch (OperationCanceledException)
        {
            // A newer event superseded this one for the same file path.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unhandled error in watch folder file created handler for {0}", filePath);
        }
        finally
        {
            newCts.Dispose();
        }
    }

    private static bool IsCandidateFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        if (filePath.EndsWith(".imported", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".failed", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".corrupt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return filePath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".magnet", StringComparison.OrdinalIgnoreCase);
    }

    internal bool WaitForFileReady(string filePath, int maxAttempts = 5, int initialDelayMs = 100, int stabilityDelayMs = 25)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }

                long initialLength;
                using (var fs1 = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    initialLength = fs1.Length;
                }

                if (initialLength > 0)
                {
                    Thread.Sleep(stabilityDelayMs);

                    if (!File.Exists(filePath))
                    {
                        return false;
                    }

                    long secondLength;
                    using (var fs2 = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        secondLength = fs2.Length;
                    }

                    if (initialLength == secondLength)
                    {
                        return true;
                    }

                    _logger.Debug("File {0} length changed from {1} to {2}, retrying...", filePath, initialLength, secondLength);
                }
                else
                {
                    _logger.Debug("File {0} has zero length on attempt {1}, retrying...", filePath, attempt);
                }
            }
            catch (IOException ex)
            {
                _logger.Debug(ex, "File {0} is locked or being written to on attempt {1}/{2}", filePath, attempt, maxAttempts);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Warn(ex, "Access denied checking readiness for {0}", filePath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error while checking readiness for {0} on attempt {1}", filePath, attempt);
            }

            if (attempt < maxAttempts)
            {
                var delayMs = initialDelayMs * attempt;
                Thread.Sleep(delayMs);
            }
        }

        _logger.Warn("File {0} did not stabilize or unlock within {1} attempts", filePath, maxAttempts);
        return false;
    }

    private string GetWatchPath()
    {
        var configuredPath = _configService?.WatchFolderPath;
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(_appFolderInfo?.AppDataFolder ?? string.Empty, "watch")
            : configuredPath;
    }

    private Category ResolveCategory(string filePath, string watchPath)
    {
        if (_categoryService == null || string.IsNullOrWhiteSpace(watchPath) || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            var fileDir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(fileDir))
            {
                return null;
            }

            var relDir = Path.GetRelativePath(watchPath, fileDir);
            if (string.IsNullOrWhiteSpace(relDir) || relDir == "." || relDir.StartsWith(".."))
            {
                return null;
            }

            var parts = relDir.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return null;
            }

            var firstComponent = parts[0];
            var cat = _categoryService.GetByName(firstComponent);
            if (cat != null && !string.IsNullOrWhiteSpace(cat.Name))
            {
                return cat;
            }

            if (parts.Length > 1)
            {
                var fullRel = string.Join("/", parts);
                cat = _categoryService.GetByName(fullRel);
                if (cat != null && !string.IsNullOrWhiteSpace(cat.Name))
                {
                    return cat;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error resolving category for file {0}", filePath);
            return null;
        }
    }

    private void ProcessFile(string filePath, string watchPath = null)
    {
        if (filePath.EndsWith(".magnet", StringComparison.OrdinalIgnoreCase))
        {
            ProcessMagnetFile(filePath, watchPath);
        }
        else
        {
            ProcessTorrentFile(filePath, watchPath);
        }
    }

    internal void ProcessTorrentFile(string filePath, string watchPath = null)
    {
        var fileName = Path.GetFileName(filePath);

        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            if (!WaitForFileReady(filePath))
            {
                _logger.Warn("Torrent file is locked or incomplete, skipping: {0}", fileName);
                return;
            }

            _logger.Info("Processing torrent file: {0}", fileName);

            var autoStart = _configService.WatchFolderAutoStartTorrents;
            var deleteAfterAdd = _configService.WatchFolderDeleteAddedTorrents;

            ParsedTorrent parsed;
            try
            {
                parsed = _parser.Parse(filePath);
            }
            catch (Exception parseEx)
            {
                _logger.Error(parseEx, "Error parsing torrent file: {0}", fileName);
                MarkFileFailed(filePath);
                return;
            }

            var torrent = new Torrent
            {
                Name = parsed.Name,
                InfoHash = parsed.InfoHash,
                TotalSize = parsed.TotalSize,
                PieceCount = parsed.PieceCount,
                PieceLength = parsed.PieceLength,
                PieceHashes = parsed.PieceHashes,
                Comment = parsed.Comment,
                CreatedBy = parsed.CreatedBy,
                CreationDate = parsed.CreationDate,
                IsPrivate = parsed.IsPrivate,
                TrackerUrl = parsed.AnnounceUrl,
                SourcePath = deleteAfterAdd ? null : filePath,
                DateAdded = DateTime.UtcNow,
                Progress = 0.0
            };

            watchPath ??= GetWatchPath();
            var category = ResolveCategory(filePath, watchPath);
            if (category != null && !string.IsNullOrWhiteSpace(category.Name))
            {
                torrent.Category = category.Name;
            }

            var defaultPath = !string.IsNullOrWhiteSpace(_configService?.TorrentSaveDirectory)
                ? _configService.TorrentSaveDirectory
                : (!string.IsNullOrWhiteSpace(watchPath) ? watchPath : Path.Combine(_appFolderInfo?.AppDataFolder ?? string.Empty, "downloads"));

            string resolvedSavePath = null;
            if (_categoryService != null)
            {
                resolvedSavePath = _categoryService.GetSavePathForCategory(torrent.Category, defaultPath);
            }

            if (string.IsNullOrWhiteSpace(resolvedSavePath))
            {
                resolvedSavePath = defaultPath;
            }

            torrent.SavePath = resolvedSavePath;

            var initialStatus = TorrentStatus.Stopped;
            if (autoStart)
            {
                initialStatus = (torrent.Progress >= 1.0 || torrent.ForceCompleted)
                    ? TorrentStatus.Seeding
                    : TorrentStatus.Downloading;
            }

            torrent.Status = initialStatus;

            var existing = _torrentService.GetByInfoHash(parsed.InfoHash);
            if (existing != null)
            {
                _logger.Info("Torrent already exists with info hash {0}, merging trackers: {1}", parsed.InfoHash, fileName);
                MergeTrackerEntries(existing.Id, parsed);
                if (string.IsNullOrWhiteSpace(existing.TrackerUrl) && !string.IsNullOrWhiteSpace(parsed.AnnounceUrl))
                {
                    existing.TrackerUrl = parsed.AnnounceUrl;
                    _torrentService.Update(existing);
                }
                HandlePostImport(filePath, deleteAfterAdd);
                return;
            }

            if (_torrentService.ExistsByInfoHash(parsed.InfoHash))
            {
                _logger.Debug("Torrent already exists, skipping: {0}", fileName);
                HandlePostImport(filePath, deleteAfterAdd);
                return;
            }

            var added = _torrentService.Add(torrent);

            CreateTrackerEntries(added.Id, parsed);
            SaveTorrentFiles(added.Id, parsed);

            HandlePostImport(filePath, deleteAfterAdd);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing torrent file: {0}", fileName);
        }
    }

    internal void ProcessMagnetFile(string filePath, string watchPath = null)
    {
        var fileName = Path.GetFileName(filePath);

        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            if (!WaitForFileReady(filePath))
            {
                _logger.Warn("Magnet file is locked or incomplete, skipping: {0}", fileName);
                return;
            }

            _logger.Info("Processing magnet file: {0}", fileName);

            ParsedMagnetLink parsed;
            string magnetUri;
            try
            {
                var text = File.ReadAllText(filePath);
                magnetUri = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(magnetUri))
                {
                    throw new FormatException("File does not contain a valid magnet URI starting with 'magnet:?'");
                }

                parsed = MagnetLinkParser.Parse(magnetUri);
            }
            catch (Exception parseEx)
            {
                _logger.Error(parseEx, "Error parsing magnet file: {0}", fileName);
                MarkFileFailed(filePath);
                return;
            }

            var autoStart = _configService.WatchFolderAutoStartTorrents;
            var deleteAfterAdd = _configService.WatchFolderDeleteAddedTorrents;

            var primaryHash = parsed.InfoHash ?? parsed.InfoHashV2;
            var existing = _torrentService.GetByInfoHash(primaryHash);
            if (existing != null)
            {
                _logger.Info("Torrent already exists with info hash {0}, merging trackers: {1}", primaryHash, fileName);
                MergeTrackerEntriesFromMagnet(existing.Id, parsed.Trackers);
                HandlePostImport(filePath, deleteAfterAdd);
                return;
            }

            if (_torrentService.ExistsByInfoHash(primaryHash))
            {
                _logger.Debug("Torrent already exists, skipping: {0}", fileName);
                HandlePostImport(filePath, deleteAfterAdd);
                return;
            }

            var torrent = new Torrent
            {
                Name = parsed.Name,
                InfoHash = parsed.InfoHash ?? parsed.InfoHashV2,
                InfoHashV2 = parsed.InfoHashV2,
                TrackerUrl = parsed.Trackers != null && parsed.Trackers.Length > 0 ? parsed.Trackers[0] : null,
                MagnetUrl = magnetUri,
                SourcePath = deleteAfterAdd ? null : filePath,
                DateAdded = DateTime.UtcNow,
                Progress = 0.0
            };

            watchPath ??= GetWatchPath();
            var category = ResolveCategory(filePath, watchPath);
            if (category != null && !string.IsNullOrWhiteSpace(category.Name))
            {
                torrent.Category = category.Name;
            }

            var defaultPath = !string.IsNullOrWhiteSpace(_configService?.TorrentSaveDirectory)
                ? _configService.TorrentSaveDirectory
                : (!string.IsNullOrWhiteSpace(watchPath) ? watchPath : Path.Combine(_appFolderInfo?.AppDataFolder ?? string.Empty, "downloads"));

            string resolvedSavePath = null;
            if (_categoryService != null)
            {
                resolvedSavePath = _categoryService.GetSavePathForCategory(torrent.Category, defaultPath);
            }

            if (string.IsNullOrWhiteSpace(resolvedSavePath))
            {
                resolvedSavePath = defaultPath;
            }

            torrent.SavePath = resolvedSavePath;

            var initialStatus = TorrentStatus.Stopped;
            if (autoStart)
            {
                initialStatus = (torrent.Progress >= 1.0 || torrent.ForceCompleted)
                    ? TorrentStatus.Seeding
                    : TorrentStatus.Downloading;
            }

            torrent.Status = initialStatus;

            var added = _torrentService.Add(torrent);

            CreateTrackerEntriesFromMagnet(added.Id, parsed.Trackers);

            HandlePostImport(filePath, deleteAfterAdd);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing magnet file: {0}", fileName);
        }
    }

    private void HandlePostImport(string filePath, bool deleteAfterAdd)
    {
        var fileName = Path.GetFileName(filePath);
        if (deleteAfterAdd)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.Info("Deleted file after adding: {0}", fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to delete file after adding: {0}", fileName);
            }
        }
        else
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var importedPath = $"{filePath}.imported";
                    File.Move(filePath, importedPath, overwrite: true);
                    _logger.Info("Marked file as imported: {0}", Path.GetFileName(importedPath));
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to rename file to .imported: {0}", fileName);
            }
        }
    }

    internal void QuarantineFile(string filePath, string extension = "failed")
    {
        try
        {
            if (File.Exists(filePath))
            {
                var quarantinedPath = $"{filePath}.{extension}";
                File.Move(filePath, quarantinedPath, overwrite: true);
                _logger.Warn("Quarantined unparseable or corrupted file as {0}: {1}", extension, Path.GetFileName(quarantinedPath));
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to quarantine file to .{0}: {1}", extension, filePath);
        }
    }

    private void MarkFileFailed(string filePath)
    {
        QuarantineFile(filePath, "failed");
    }

    private void MergeTrackerEntries(int torrentId, ParsedTorrent parsed)
    {
        if (_trackerEntryService == null)
        {
            return;
        }

        var existingTrackers = _trackerEntryService.GetByTorrentId(torrentId) ?? new List<TrackerEntry>();
        var existingUrls = new HashSet<string>(existingTrackers.Select(t => t.Url), StringComparer.OrdinalIgnoreCase);

        if (parsed.AnnounceList != null && parsed.AnnounceList.Count > 0)
        {
            for (var tier = 0; tier < parsed.AnnounceList.Count; tier++)
            {
                foreach (var url in parsed.AnnounceList[tier])
                {
                    if (string.IsNullOrWhiteSpace(url) || !existingUrls.Add(url))
                    {
                        continue;
                    }

                    _trackerEntryService.Add(new TrackerEntry
                    {
                        TorrentId = torrentId,
                        Url = url,
                        Tier = tier,
                        Status = TrackerStatus.Unknown,
                        Enabled = true,
                        AnnounceInterval = _configService?.AnnounceIntervalSeconds ?? 1800,
                        MinAnnounceInterval = _configService?.MinAnnounceIntervalSeconds ?? 300
                    });
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(parsed.AnnounceUrl) && existingUrls.Add(parsed.AnnounceUrl))
        {
            _trackerEntryService.Add(new TrackerEntry
            {
                TorrentId = torrentId,
                Url = parsed.AnnounceUrl,
                Tier = 0,
                Status = TrackerStatus.Unknown,
                Enabled = true,
                AnnounceInterval = _configService?.AnnounceIntervalSeconds ?? 1800,
                MinAnnounceInterval = _configService?.MinAnnounceIntervalSeconds ?? 300
            });
        }
    }

    private void MergeTrackerEntriesFromMagnet(int torrentId, string[] trackers)
    {
        if (_trackerEntryService == null || trackers == null || trackers.Length == 0)
        {
            return;
        }

        var existingTrackers = _trackerEntryService.GetByTorrentId(torrentId) ?? new List<TrackerEntry>();
        var existingUrls = new HashSet<string>(existingTrackers.Select(t => t.Url), StringComparer.OrdinalIgnoreCase);

        var tier = existingTrackers.Count > 0 ? existingTrackers.Max(t => t.Tier) + 1 : 0;
        foreach (var url in trackers)
        {
            if (string.IsNullOrWhiteSpace(url) || !existingUrls.Add(url))
            {
                continue;
            }

            _trackerEntryService.Add(new TrackerEntry
            {
                TorrentId = torrentId,
                Url = url,
                Tier = tier++,
                Status = TrackerStatus.Unknown,
                Enabled = true,
                AnnounceInterval = _configService?.AnnounceIntervalSeconds ?? 1800,
                MinAnnounceInterval = _configService?.MinAnnounceIntervalSeconds ?? 300
            });
        }
    }

    private void CreateTrackerEntriesFromMagnet(int torrentId, string[] trackers)
    {
        if (_trackerEntryService == null || trackers == null || trackers.Length == 0)
        {
            return;
        }

        var tier = 0;
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in trackers)
        {
            if (string.IsNullOrWhiteSpace(url) || !urls.Add(url))
            {
                continue;
            }

            _trackerEntryService.Add(new TrackerEntry
            {
                TorrentId = torrentId,
                Url = url,
                Tier = tier++,
                Status = TrackerStatus.Unknown,
                Enabled = true,
                AnnounceInterval = _configService.AnnounceIntervalSeconds,
                MinAnnounceInterval = _configService.MinAnnounceIntervalSeconds
            });
        }
    }

    private void SaveTorrentFiles(int torrentId, ParsedTorrent parsed)
    {
        if (_torrentFileService != null && parsed.Files != null && parsed.Files.Count > 0)
        {
            var pieceLength = parsed.PieceLength > 0 ? (long)parsed.PieceLength : 0L;
            var runningByteOffset = 0L;

            foreach (var file in parsed.Files)
            {
                var (pieceOffset, pieceCount) = TorrentPieceCalculator.CalculateForFile(runningByteOffset, file.Size, pieceLength);
                if (pieceLength > 0)
                {
                    runningByteOffset += file.Size;
                }

                _torrentFileService.Add(new TorrentFile
                {
                    TorrentId = torrentId,
                    Path = file.Path,
                    Size = file.Size,
                    PieceOffset = pieceOffset,
                    PieceCount = pieceCount,
                    IsPaddingFile = file.IsPaddingFile
                });
            }
        }
    }

    private void CreateTrackerEntries(int torrentId, ParsedTorrent parsed)
    {
        var urls = new HashSet<string>();

        if (parsed.AnnounceList != null && parsed.AnnounceList.Count > 0)
        {
            for (var tier = 0; tier < parsed.AnnounceList.Count; tier++)
            {
                foreach (var url in parsed.AnnounceList[tier])
                {
                    if (string.IsNullOrWhiteSpace(url) || !urls.Add(url))
                    {
                        continue;
                    }

                    _trackerEntryService.Add(new TrackerEntry
                    {
                        TorrentId = torrentId,
                        Url = url,
                        Tier = tier,
                        Status = TrackerStatus.Unknown,
                        Enabled = true,
                        AnnounceInterval = _configService.AnnounceIntervalSeconds,
                        MinAnnounceInterval = _configService.MinAnnounceIntervalSeconds
                    });
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(parsed.AnnounceUrl))
        {
            _trackerEntryService.Add(new TrackerEntry
            {
                TorrentId = torrentId,
                Url = parsed.AnnounceUrl,
                Tier = 0,
                Status = TrackerStatus.Unknown,
                Enabled = true,
                AnnounceInterval = _configService.AnnounceIntervalSeconds,
                MinAnnounceInterval = _configService.MinAnnounceIntervalSeconds
            });
        }
    }
}
