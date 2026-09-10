using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public class TorrentStateMachine : ITorrentStateMachine
{
    private readonly ITorrentEventLogService _eventLogService;
    private readonly Logger _logger;

    public TorrentStateMachine(ITorrentEventLogService eventLogService)
    {
        _eventLogService = eventLogService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool HandleForceCompleted(Torrent torrent)
    {
        if (torrent.ForceCompleted)
        {
            if (torrent.Status == TorrentStatus.Downloading)
            {
                torrent.Status = TorrentStatus.Seeding;
                _logger.Info("Torrent {0} is force-completed, switching to seeding", torrent.Name);
                _eventLogService.Info(torrent.Id, "Seeding", "Force-completed (100%), switched to seeding");
                return true;
            }
        }

        return false;
    }

    public bool CheckDownloadThreshold(Torrent torrent, double defaultThreshold)
    {
        var effectiveThreshold = torrent.Threshold > 0 ? torrent.Threshold / 100.0 : defaultThreshold;
        if (torrent.Progress >= effectiveThreshold && torrent.Status == TorrentStatus.Downloading)
        {
            _logger.Info("Torrent {0} reached download threshold ({1}%), switching to seeding", torrent.Name, (int)(effectiveThreshold * 100));
            _eventLogService.Info(torrent.Id, "Seeding", $"Download reached threshold ({(int)(effectiveThreshold * 100)}%), switching to seeding");
            torrent.Status = TorrentStatus.Seeding;
            return true;
        }

        return false;
    }

    public void ApplyRatioLimit(List<Torrent> seedingTorrents, double globalRatioLimit)
    {
        if (globalRatioLimit <= 0)
        {
            return;
        }

        for (var i = seedingTorrents.Count - 1; i >= 0; i--)
        {
            var torrent = seedingTorrents[i];
            if (torrent.Ratio >= globalRatioLimit)
            {
                _logger.Info("Torrent {0} reached global seed ratio limit ({1:F2}), stopping", torrent.Name, globalRatioLimit);
                _eventLogService.Info(torrent.Id, "Seeding", $"Global seed ratio limit reached ({globalRatioLimit:F2}), torrent stopped");
                torrent.Status = TorrentStatus.Stopped;
                torrent.UploadSpeed = 0;
                torrent.DownloadSpeed = 0;
                torrent.Active = false;
                seedingTorrents.RemoveAt(i);
            }
        }
    }
}
