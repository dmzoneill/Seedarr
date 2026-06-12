using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Torrents;

public class WatchFolderService : BackgroundService
{
    private readonly ITorrentFileParser _parser;
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;
    private FileSystemWatcher _watcher;

    public WatchFolderService(ITorrentFileParser parser, ITorrentService torrentService)
    {
        _parser = parser;
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watchPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Seedarr",
            "watch");

        if (!Directory.Exists(watchPath))
        {
            Directory.CreateDirectory(watchPath);
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

        return Task.CompletedTask;
    }

    private void OnTorrentFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            _logger.Info("New torrent file detected: {0}", e.Name);

            Thread.Sleep(500);

            var parsed = _parser.Parse(e.FullPath);
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
                SourcePath = e.FullPath,
                DateAdded = DateTime.UtcNow,
                Status = TorrentStatus.Stopped
            };

            _torrentService.Add(torrent);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing torrent file: {0}", e.Name);
        }
    }
}
