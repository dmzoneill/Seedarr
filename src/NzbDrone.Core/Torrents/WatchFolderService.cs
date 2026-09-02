using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Torrents;

public class WatchFolderService : BackgroundService
{
    private readonly ITorrentFileParser _parser;
    private readonly ITorrentService _torrentService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IConfigService _configService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _fileDebounceTokens = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher _watcher;

    public WatchFolderService(ITorrentFileParser parser, ITorrentService torrentService, ITrackerEntryService trackerEntryService, IAppFolderInfo appFolderInfo, IConfigService configService)
    {
        _parser = parser;
        _torrentService = torrentService;
        _trackerEntryService = trackerEntryService;
        _appFolderInfo = appFolderInfo;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configService.WatchFolderEnabled)
        {
            _logger.Info("Watch folder service is disabled via configuration");
            return;
        }

        var configuredPath = _configService.WatchFolderPath;
        var watchPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(_appFolderInfo.AppDataFolder, "watch")
            : configuredPath;

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

        _watcher = new FileSystemWatcher(watchPath, "*.torrent")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnTorrentFileCreated;

        stoppingToken.Register(() =>
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
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

            var torrentFiles = Directory.GetFiles(watchPath, "*.torrent");

            foreach (var filePath in torrentFiles)
            {
                ProcessTorrentFile(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during periodic scan of watch folder");
        }
    }

    private async void OnTorrentFileCreated(object sender, FileSystemEventArgs e)
    {
        var filePath = e.FullPath;
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

        if (oldCts != newCts)
        {
            oldCts?.Cancel();
            oldCts?.Dispose();
        }

        try
        {
            await Task.Delay(500, newCts.Token);
            _fileDebounceTokens.TryRemove(filePath, out _);
            ProcessTorrentFile(filePath);
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

    private void ProcessTorrentFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            _logger.Info("Processing torrent file: {0}", fileName);

            var autoStart = _configService.WatchFolderAutoStartTorrents;
            var deleteAfterAdd = _configService.WatchFolderDeleteAddedTorrents;

            var parsed = _parser.Parse(filePath);
            var torrent = new Torrent
            {
                Name = parsed.Name,
                InfoHash = parsed.InfoHash,
                TotalSize = parsed.TotalSize,
                PieceCount = parsed.PieceCount,
                PieceLength = parsed.PieceLength,
                Comment = parsed.Comment,
                CreatedBy = parsed.CreatedBy,
                CreationDate = parsed.CreationDate,
                IsPrivate = parsed.IsPrivate,
                TrackerUrl = parsed.AnnounceUrl,
                SourcePath = filePath,
                DateAdded = DateTime.UtcNow,
                Status = autoStart ? TorrentStatus.Seeding : TorrentStatus.Stopped,
                Progress = 0.0
            };

            if (_torrentService.ExistsByInfoHash(parsed.InfoHash))
            {
                _logger.Debug("Torrent already exists, skipping: {0}", fileName);
                return;
            }

            var added = _torrentService.Add(torrent);

            CreateTrackerEntries(added.Id, parsed);

            if (deleteAfterAdd)
            {
                try
                {
                    File.Delete(filePath);
                    _logger.Info("Deleted torrent file after adding: {0}", fileName);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to delete torrent file after adding: {0}", fileName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing torrent file: {0}", fileName);
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
