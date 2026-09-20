using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public class TorrentStateMachine : ITorrentStateMachine
{
    private readonly ITorrentEventLogService _eventLogService;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentService _torrentService;
    private readonly ICategoryService _categoryService;
    private readonly Logger _logger;

    public TorrentStateMachine(
        ITorrentEventLogService eventLogService,
        IEventAggregator eventAggregator = null,
        ITorrentService torrentService = null,
        ICategoryService categoryService = null)
    {
        _eventLogService = eventLogService;
        _eventAggregator = eventAggregator;
        _torrentService = torrentService;
        _categoryService = categoryService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool HandleForceCompleted(Torrent torrent)
    {
        if (torrent == null || torrent.Status == TorrentStatus.Checking || torrent.Status == TorrentStatus.QueuedForChecking)
        {
            return false;
        }

        if (torrent.ForceCompleted)
        {
            if (torrent.Status == TorrentStatus.Downloading)
            {
                var oldStatus = torrent.Status;
                torrent.Status = TorrentStatus.Seeding;
                _logger.Info("Torrent {0} is force-completed, switching to seeding", torrent.Name);
                _eventLogService.Info(torrent.Id, "Seeding", "Force-completed (100%), switched to seeding");
                _eventAggregator?.PublishEvent(new TorrentDownloadCompletedEvent(torrent));
                _eventAggregator?.PublishEvent(new TorrentFinishedEvent(torrent));
                _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Seeding));
                return true;
            }
        }

        return false;
    }

    public bool CheckDownloadThreshold(Torrent torrent, double defaultThreshold)
    {
        if (torrent == null || torrent.Status == TorrentStatus.Checking || torrent.Status == TorrentStatus.QueuedForChecking)
        {
            return false;
        }

        var effectiveThreshold = torrent.Threshold > 0 ? torrent.Threshold / 100.0 : defaultThreshold;
        if (torrent.Progress >= effectiveThreshold && torrent.Status == TorrentStatus.Downloading)
        {
            var oldStatus = torrent.Status;
            _logger.Info("Torrent {0} reached download threshold ({1}%), switching to seeding", torrent.Name, (int)(effectiveThreshold * 100));
            _eventLogService.Info(torrent.Id, "Seeding", $"Download reached threshold ({(int)(effectiveThreshold * 100)}%), switching to seeding");
            torrent.Status = TorrentStatus.Seeding;
            _eventAggregator?.PublishEvent(new TorrentDownloadCompletedEvent(torrent));
            _eventAggregator?.PublishEvent(new TorrentFinishedEvent(torrent));
            _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Seeding));
            return true;
        }

        return false;
    }

    public List<Torrent> ApplyRatioLimit(List<Torrent> seedingTorrents, double globalRatioLimit, string action = "Stop")
    {
        var stoppedTorrents = new List<Torrent>();

        if (seedingTorrents == null || seedingTorrents.Count == 0)
        {
            return stoppedTorrents;
        }

        for (var i = seedingTorrents.Count - 1; i >= 0; i--)
        {
            var torrent = seedingTorrents[i];

            Category category = null;
            if (_categoryService != null && !string.IsNullOrWhiteSpace(torrent.Category))
            {
                category = _categoryService.GetByName(torrent.Category);
            }

            double? effectiveRatioLimit = null;
            if (torrent.RatioLimit.HasValue && torrent.RatioLimit.Value > 0)
            {
                effectiveRatioLimit = torrent.RatioLimit.Value;
            }
            else if (category != null && category.TargetRatio > 0)
            {
                effectiveRatioLimit = category.TargetRatio;
            }
            else if (globalRatioLimit > 0)
            {
                effectiveRatioLimit = globalRatioLimit;
            }

            long? effectiveTimeLimitSeconds = null;
            if (torrent.SeedingTimeLimit.HasValue && torrent.SeedingTimeLimit.Value > 0)
            {
                effectiveTimeLimitSeconds = torrent.SeedingTimeLimit.Value;
            }
            else if (category != null && category.TargetSeedTimeMinutes > 0)
            {
                effectiveTimeLimitSeconds = category.TargetSeedTimeMinutes * 60L;
            }

            var ratioReached = effectiveRatioLimit.HasValue && torrent.Ratio >= effectiveRatioLimit.Value;
            var timeReached = effectiveTimeLimitSeconds.HasValue && torrent.SeedingTime >= effectiveTimeLimitSeconds.Value;

            if (ratioReached || timeReached)
            {
                var effectiveAction = string.IsNullOrWhiteSpace(action) ? "Stop" : action;
                var reason = ratioReached && timeReached
                    ? $"Seed limit reached (ratio: {torrent.Ratio:F2} >= {effectiveRatioLimit:F2}, time: {torrent.SeedingTime}s >= {effectiveTimeLimitSeconds}s)"
                    : ratioReached
                        ? $"Seed ratio limit reached ({torrent.Ratio:F2} >= {effectiveRatioLimit:F2})"
                        : $"Seed time limit reached ({torrent.SeedingTime}s >= {effectiveTimeLimitSeconds}s)";

                _logger.Info("Torrent {0} reached {1}, action: {2}", torrent.Name, reason, effectiveAction);
                _eventLogService?.Info(torrent.Id, "Seeding", $"{reason}, action: {effectiveAction}");

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
                if (ratioReached)
                {
                    _eventAggregator?.PublishEvent(new TorrentRatioReachedEvent(torrent, torrent.Ratio));
                }

                if (timeReached)
                {
                    _eventAggregator?.PublishEvent(new TorrentSeedingTimeReachedEvent(torrent, TimeSpan.FromSeconds(torrent.SeedingTime)));
                }

                _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, newStatus));

                if (string.Equals(effectiveAction, "RemoveTorrent", StringComparison.OrdinalIgnoreCase))
                {
                    _torrentService?.Delete(torrent.Id, false);
                }
                else if (string.Equals(effectiveAction, "RemoveTorrentAndData", StringComparison.OrdinalIgnoreCase))
                {
                    _torrentService?.Delete(torrent.Id, true);
                }
                else
                {
                    stoppedTorrents.Add(torrent);
                }
            }
        }

        return stoppedTorrents;
    }

    public bool CanScheduleTransfers(Torrent torrent)
    {
        if (torrent == null)
        {
            return false;
        }

        if (torrent.Status == TorrentStatus.Checking || torrent.Status == TorrentStatus.QueuedForChecking)
        {
            return false;
        }

        return true;
    }

    public void TransitionToChecking(Torrent torrent)
    {
        if (torrent == null)
        {
            return;
        }

        var oldStatus = torrent.Status;
        torrent.Status = TorrentStatus.Checking;
        torrent.Active = false;
        torrent.UploadSpeed = 0;
        torrent.DownloadSpeed = 0;
        _logger.Info("Torrent {0} transitioned to Checking", torrent.Name);
        _eventLogService?.Info(torrent.Id, "Recheck", "Torrent entered hash verification");
        _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Checking));
    }

    public TorrentStatus TransitionFromChecking(Torrent torrent)
    {
        if (torrent == null)
        {
            return TorrentStatus.Downloading;
        }

        var oldStatus = torrent.Status;
        var newStatus = torrent.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
        torrent.Status = newStatus;
        torrent.Active = false;
        torrent.UploadSpeed = 0;
        torrent.DownloadSpeed = 0;
        _logger.Info("Torrent {0} completed hash verification, transitioned to {1}", torrent.Name, newStatus);
        _eventLogService?.Info(torrent.Id, "Recheck", $"Torrent completed hash verification, transitioned to {newStatus}");
        _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, newStatus));
        return newStatus;
    }
}
