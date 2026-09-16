using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public class TorrentStateMachine : ITorrentStateMachine
{
    private readonly ITorrentEventLogService _eventLogService;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;

    public TorrentStateMachine(ITorrentEventLogService eventLogService, IEventAggregator eventAggregator = null, ITorrentService torrentService = null)
    {
        _eventLogService = eventLogService;
        _eventAggregator = eventAggregator;
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool HandleForceCompleted(Torrent torrent)
    {
        if (torrent.ForceCompleted)
        {
            if (torrent.Status == TorrentStatus.Downloading)
            {
                var oldStatus = torrent.Status;
                torrent.Status = TorrentStatus.Seeding;
                _logger.Info("Torrent {0} is force-completed, switching to seeding", torrent.Name);
                _eventLogService.Info(torrent.Id, "Seeding", "Force-completed (100%), switched to seeding");
                _eventAggregator?.PublishEvent(new TorrentDownloadCompletedEvent(torrent));
                _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Seeding));
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
            var oldStatus = torrent.Status;
            _logger.Info("Torrent {0} reached download threshold ({1}%), switching to seeding", torrent.Name, (int)(effectiveThreshold * 100));
            _eventLogService.Info(torrent.Id, "Seeding", $"Download reached threshold ({(int)(effectiveThreshold * 100)}%), switching to seeding");
            torrent.Status = TorrentStatus.Seeding;
            _eventAggregator?.PublishEvent(new TorrentDownloadCompletedEvent(torrent));
            _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Seeding));
            return true;
        }

        return false;
    }

    public void ApplyRatioLimit(List<Torrent> seedingTorrents, double globalRatioLimit, string action = "Stop")
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
                var effectiveAction = string.IsNullOrWhiteSpace(action) ? "Stop" : action;
                _logger.Info("Torrent {0} reached global seed ratio limit ({1:F2}), action: {2}", torrent.Name, globalRatioLimit, effectiveAction);
                _eventLogService.Info(torrent.Id, "Seeding", $"Global seed ratio limit reached ({globalRatioLimit:F2}), action: {effectiveAction}");
                var oldStatus = torrent.Status;
                var newStatus = string.Equals(effectiveAction, "Pause", StringComparison.OrdinalIgnoreCase)
                    ? TorrentStatus.Paused
                    : TorrentStatus.Stopped;

                torrent.Status = newStatus;
                torrent.UploadSpeed = 0;
                torrent.DownloadSpeed = 0;
                torrent.Active = false;
                seedingTorrents.RemoveAt(i);
                _eventAggregator?.PublishEvent(new TorrentSeedGoalReachedEvent(torrent));
                _eventAggregator?.PublishEvent(new TorrentRatioReachedEvent(torrent, torrent.Ratio));
                _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, newStatus));

                if (string.Equals(effectiveAction, "RemoveTorrent", StringComparison.OrdinalIgnoreCase))
                {
                    _torrentService?.Delete(torrent.Id, false);
                }
                else if (string.Equals(effectiveAction, "RemoveTorrentAndData", StringComparison.OrdinalIgnoreCase))
                {
                    _torrentService?.Delete(torrent.Id, true);
                }
            }
        }
    }
}
