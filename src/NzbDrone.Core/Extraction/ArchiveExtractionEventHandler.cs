using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Extraction;

public class ArchiveExtractionEventHandler : IHandle<TorrentDownloadCompletedEvent>
{
    private readonly IArchiveExtractorService _archiveExtractorService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public ArchiveExtractionEventHandler(IArchiveExtractorService archiveExtractorService)
    {
        _archiveExtractorService = archiveExtractorService;
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        var torrent = message.Torrent;

        if (!HasArchives(torrent))
        {
            _logger.Debug("No archives found for completed torrent '{0}', skipping extraction", torrent.Name);
            return;
        }

        _logger.Info("Archives found for completed torrent '{0}', triggering extraction", torrent.Name);
        Task.Run(async () =>
        {
            try
            {
                await _archiveExtractorService.ExtractTorrentArchiveAsync(torrent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract archives for torrent '{0}'", torrent.Name);
            }
        });
    }

    private bool HasArchives(Torrent torrent)
    {
        var rootDir = ArchiveExtractorService.GetTorrentDirectory(torrent);
        if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
                .Any(file => _archiveExtractorService.IsPrimaryArchive(file));
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error scanning directory '{0}' for archives", rootDir);
            return false;
        }
    }
}
