using System;
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
            _watcher?.Dispose();
            _logger.Info("Watch folder service stopped");
        });

        var scanInterval = _configService.WatchFolderScanIntervalSeconds;

        if (scanInterval < 1)
        {
            scanInterval = 10;
        }

        _logger.Info("Periodic scan interval: {0} seconds", scanInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(scanInterval), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            PeriodicScan(watchPath);
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

    private void OnTorrentFileCreated(object sender, FileSystemEventArgs e)
    {
        Thread.Sleep(500);
        ProcessTorrentFile(e.FullPath);
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
