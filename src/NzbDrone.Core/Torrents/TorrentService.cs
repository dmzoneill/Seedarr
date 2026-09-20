using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Torrents;

public interface ITorrentService
{
    List<Torrent> GetAll();
    List<Torrent> GetByInfoHashes(IEnumerable<string> infoHashes);
    Torrent Get(int id);
    Torrent GetByInfoHash(string infoHash);
    Torrent FindByInfoHash(string infoHash);
    bool ExistsByInfoHash(string infoHash);
    Torrent Add(Torrent torrent);
    Torrent Update(Torrent torrent);
    void UpdateMany(IEnumerable<Torrent> torrents);
    Torrent UpdateUserFields(int id, Torrent updates);
    Torrent UpdateUserFields(Torrent torrent);
    void Delete(int id, bool deleteFiles = false);
    Torrent Recheck(int id);
    void MoveQueue(int id, string position);
    void BatchMoveQueue(IEnumerable<int> ids, string position);
    Torrent Start(int id);
    Torrent Pause(int id, string reason = null);
}

public class TorrentService : ITorrentService,
    IHandle<VpnKillSwitchTriggeredEvent>,
    IHandle<VpnInterfaceRestoredEvent>,
    IHandle<VpnRestoredEvent>,
    IHandle<DiskSpaceCriticalEvent>,
    IHandle<DiskSpaceRestoredEvent>
{
    public const string EmergencyDiskSpacePausePrefix = "Paused: Emergency low disk space on volume ";
    public const long EmergencyPauseFreeSpaceBufferBytes = 500L * 1024 * 1024; // 500 MB

    private readonly ITorrentRepository _repository;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentRecheckService _torrentRecheckService;
    private readonly object _sortOrderLock = new();
    private readonly Logger _logger;

    public IDiskSpaceService DiskSpaceService { get; set; }

    public TorrentService(
        ITorrentRepository repository,
        ITorrentFileService torrentFileService,
        ITrackerEntryService trackerEntryService,
        IEventAggregator eventAggregator,
        IDiskSpaceService diskSpaceService = null,
        ITorrentRecheckService torrentRecheckService = null)
    {
        _repository = repository;
        _torrentFileService = torrentFileService;
        _trackerEntryService = trackerEntryService;
        _eventAggregator = eventAggregator;
        DiskSpaceService = diskSpaceService;
        _torrentRecheckService = torrentRecheckService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<Torrent> GetAll()
    {
        return _repository.All().ToList();
    }

    public List<Torrent> GetByInfoHashes(IEnumerable<string> infoHashes)
    {
        if (infoHashes == null)
        {
            return new List<Torrent>();
        }

        return _repository.GetByInfoHashes(infoHashes);
    }

    public Torrent Get(int id)
    {
        return _repository.Get(id);
    }

    public Torrent GetByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        return _repository.GetByInfoHash(infoHash.Trim().ToLowerInvariant());
    }

    public Torrent FindByInfoHash(string infoHash)
    {
        return GetByInfoHash(infoHash);
    }

    public bool ExistsByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return false;
        }

        return _repository.ExistsByInfoHash(infoHash.Trim().ToLowerInvariant());
    }

    public Torrent Add(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        if (torrent.Status == TorrentStatus.Downloading)
        {
            ValidateDiskSpaceForTorrent(torrent);
        }

        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            torrent.InfoHash = torrent.InfoHash.Trim().ToLowerInvariant();
        }

        _logger.Info("Adding torrent: {0}", torrent.Name);

        if (!string.IsNullOrWhiteSpace(torrent.InfoHash) && _repository.ExistsByInfoHash(torrent.InfoHash))
        {
            throw new DuplicateTorrentException(torrent.InfoHash);
        }

        Torrent added;
        lock (_sortOrderLock)
        {
            if (!string.IsNullOrWhiteSpace(torrent.InfoHash) && _repository.ExistsByInfoHash(torrent.InfoHash))
            {
                throw new DuplicateTorrentException(torrent.InfoHash);
            }

            torrent.SortOrder = _repository.GetNextSortOrder();
            added = _repository.Insert(torrent);
        }

        _eventAggregator.PublishEvent(new TorrentAddedEvent(added));
        _eventAggregator.PublishEvent(new ModelEvent<Torrent>(added, ModelAction.Created));
        return added;
    }

    public Torrent Update(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            torrent.InfoHash = torrent.InfoHash.Trim().ToLowerInvariant();
        }

        _logger.Debug("Updating torrent: {0}", torrent.Name);
        var updated = _repository.Update(torrent);
        _eventAggregator.PublishEvent(new ModelEvent<Torrent>(updated, ModelAction.Updated));
        _eventAggregator.PublishEvent(new TorrentUpdatedEvent(updated));
        return updated;
    }

    public void UpdateMany(IEnumerable<Torrent> torrents)
    {
        var list = torrents as IList<Torrent> ?? torrents.ToList();
        if (list.Count == 0)
        {
            return;
        }

        _logger.Debug("Batch updating {0} torrents", list.Count);
        _repository.UpdateMany(list);
    }

    public Torrent UpdateUserFields(int id, Torrent updates)
    {
        var existing = _repository.Get(id);
        if (existing == null)
        {
            return null;
        }

        existing.ApplyUserFields(updates);
        return Update(existing);
    }

    public Torrent UpdateUserFields(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return UpdateUserFields(torrent.Id, torrent);
    }

    public void Delete(int id, bool deleteFiles = false)
    {
        _logger.Info("Deleting torrent {0} (deleteFiles={1})", id, deleteFiles);

        var torrent = _repository.Get(id);

        if (deleteFiles && torrent != null && !string.IsNullOrEmpty(torrent.SourcePath))
        {
            try
            {
                if (File.Exists(torrent.SourcePath))
                {
                    File.Delete(torrent.SourcePath);
                    _logger.Info("Deleted source file: {0}", torrent.SourcePath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to delete source file: {0}", torrent.SourcePath);
            }
        }

        _eventAggregator.PublishEvent(new TorrentDeletedEvent(id, torrent));
        _torrentFileService.DeleteByTorrentId(id);
        _trackerEntryService.DeleteByTorrentId(id);
        _repository.Delete(id);

        if (torrent != null)
        {
            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Deleted));
        }
    }

    public Torrent Recheck(int id)
    {
        var torrent = _repository.Get(id);
        if (torrent == null)
        {
            return null;
        }

        if (_torrentRecheckService != null)
        {
            return _torrentRecheckService.QueueRecheck(id) ?? _torrentRecheckService.Recheck(torrent);
        }

        _logger.Info("Rechecking torrent: {0}", torrent.Name);

        torrent.Progress = torrent.Progress >= 1.0 ? 1.0 : 0.0;
        torrent.LastActive = DateTime.UtcNow;

        return _repository.Update(torrent);
    }

    public void MoveQueue(int id, string position)
    {
        lock (_sortOrderLock)
        {
            var all = _repository.All().OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
            var torrent = all.FirstOrDefault(t => t.Id == id);
            if (torrent == null)
            {
                return;
            }

            var currentIndex = all.IndexOf(torrent);

            _logger.Info("Moving torrent {0} queue position: {1}", torrent.Name, position);

            all.RemoveAt(currentIndex);

            switch (position.ToLowerInvariant())
            {
                case "top":
                    all.Insert(0, torrent);
                    break;
                case "up":
                    var upIndex = Math.Max(0, currentIndex - 1);
                    all.Insert(upIndex, torrent);
                    break;
                case "down":
                    var downIndex = Math.Min(all.Count, currentIndex + 1);
                    all.Insert(downIndex, torrent);
                    break;
                case "bottom":
                    all.Add(torrent);
                    break;
                default:
                    all.Insert(currentIndex, torrent);
                    return;
            }

            var toUpdate = new List<Torrent>();
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].SortOrder != i)
                {
                    all[i].SortOrder = i;
                    toUpdate.Add(all[i]);
                }
            }

            if (toUpdate.Count > 0)
            {
                _repository.UpdateMany(toUpdate);
            }
        }
    }

    public void BatchMoveQueue(IEnumerable<int> ids, string position)
    {
        if (ids == null || string.IsNullOrWhiteSpace(position))
        {
            return;
        }

        var idSet = ids as ISet<int> ?? new HashSet<int>(ids);
        if (idSet.Count == 0)
        {
            return;
        }

        lock (_sortOrderLock)
        {
            var all = _repository.All().OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
            if (all.Count == 0)
            {
                return;
            }

            var moving = all.Where(t => idSet.Contains(t.Id)).ToList();
            if (moving.Count == 0)
            {
                return;
            }

            _logger.Info("Batch moving {0} torrents queue position: {1}", moving.Count, position);

            List<Torrent> reordered;
            switch (position.ToLowerInvariant())
            {
                case "top":
                    var remainderTop = all.Where(t => !idSet.Contains(t.Id)).ToList();
                    reordered = new List<Torrent>(all.Count);
                    reordered.AddRange(moving);
                    reordered.AddRange(remainderTop);
                    break;

                case "bottom":
                    var remainderBottom = all.Where(t => !idSet.Contains(t.Id)).ToList();
                    reordered = new List<Torrent>(all.Count);
                    reordered.AddRange(remainderBottom);
                    reordered.AddRange(moving);
                    break;

                case "up":
                    reordered = ShiftQueueUp(all, idSet);
                    break;

                case "down":
                    reordered = ShiftQueueDown(all, idSet);
                    break;

                default:
                    return;
            }

            var changed = new List<Torrent>();
            for (var i = 0; i < reordered.Count; i++)
            {
                if (reordered[i].SortOrder != i)
                {
                    reordered[i].SortOrder = i;
                    changed.Add(reordered[i]);
                }
            }

            if (changed.Count > 0)
            {
                _repository.UpdateMany(changed);
            }
        }
    }

    private static List<Torrent> ShiftQueueUp(List<Torrent> all, ISet<int> idSet)
    {
        var reordered = new List<Torrent>(all);
        for (var i = 0; i < reordered.Count; i++)
        {
            if (idSet.Contains(reordered[i].Id) && i > 0 && !idSet.Contains(reordered[i - 1].Id))
            {
                var temp = reordered[i];
                reordered[i] = reordered[i - 1];
                reordered[i - 1] = temp;
            }
        }

        return reordered;
    }

    private static List<Torrent> ShiftQueueDown(List<Torrent> all, ISet<int> idSet)
    {
        var reordered = new List<Torrent>(all);
        for (var i = reordered.Count - 1; i >= 0; i--)
        {
            if (idSet.Contains(reordered[i].Id) && i < reordered.Count - 1 && !idSet.Contains(reordered[i + 1].Id))
            {
                var temp = reordered[i];
                reordered[i] = reordered[i + 1];
                reordered[i + 1] = temp;
            }
        }

        return reordered;
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        var activeTorrents = _repository.All()
            .Where(t => (t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding) && !t.IsVpnPaused)
            .ToList();

        if (activeTorrents.Count == 0)
        {
            return;
        }

        _logger.Info("VPN kill switch triggered: pausing {0} active torrents.", activeTorrents.Count);
        foreach (var torrent in activeTorrents)
        {
            torrent.IsVpnPaused = true;
        }

        _repository.UpdateMany(activeTorrents);
        foreach (var torrent in activeTorrents)
        {
            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
            _eventAggregator.PublishEvent(new TorrentUpdatedEvent(torrent));
        }
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        ResumeVpnPausedTorrents();
    }

    public void Handle(VpnRestoredEvent message)
    {
        ResumeVpnPausedTorrents();
    }

    private void ResumeVpnPausedTorrents()
    {
        var pausedTorrents = _repository.All()
            .Where(t => t.IsVpnPaused)
            .ToList();

        if (pausedTorrents.Count == 0)
        {
            return;
        }

        _logger.Info("VPN restored: unpausing {0} VPN-paused torrents.", pausedTorrents.Count);
        foreach (var torrent in pausedTorrents)
        {
            torrent.IsVpnPaused = false;
        }

        _repository.UpdateMany(pausedTorrents);
        foreach (var torrent in pausedTorrents)
        {
            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
            _eventAggregator.PublishEvent(new TorrentUpdatedEvent(torrent));
        }
    }

    public Torrent Start(int id)
    {
        var torrent = _repository.Get(id);
        if (torrent == null)
        {
            return null;
        }

        ValidateDiskSpaceForTorrent(torrent);

        torrent.Resume();
        if (torrent.ErrorMessage != null && torrent.ErrorMessage.StartsWith(EmergencyDiskSpacePausePrefix, StringComparison.OrdinalIgnoreCase))
        {
            torrent.ErrorMessage = null;
        }

        return Update(torrent);
    }

    public Torrent Pause(int id, string reason = null)
    {
        var torrent = _repository.Get(id);
        if (torrent == null)
        {
            return null;
        }

        torrent.Pause();
        if (!string.IsNullOrEmpty(reason))
        {
            torrent.ErrorMessage = reason;
        }

        return Update(torrent);
    }

    public void ValidateDiskSpaceForTorrent(Torrent torrent)
    {
        if (torrent == null || DiskSpaceService == null)
        {
            return;
        }

        if (torrent.TotalSize <= 0)
        {
            return;
        }

        var remainingBytes = Math.Max(0L, torrent.TotalSize - torrent.Downloaded);
        if (remainingBytes <= 0)
        {
            return;
        }

        var diskInfo = DiskSpaceService.GetDiskSpaceForPath(torrent.SavePath);
        if (diskInfo == null)
        {
            return;
        }

        var requiredSpace = remainingBytes + EmergencyPauseFreeSpaceBufferBytes;
        if (diskInfo.FreeSpace < requiredSpace)
        {
            _logger.Warn(
                "Insufficient disk space on destination volume '{0}' for torrent '{1}'. Required: {2} bytes (including 500MB safety buffer), Available: {3} bytes",
                diskInfo.Path,
                torrent.Name,
                requiredSpace,
                diskInfo.FreeSpace);

            throw new InsufficientDiskSpaceException("Insufficient disk space on destination volume to complete torrent download");
        }
    }

    public void Handle(DiskSpaceCriticalEvent message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.DrivePath))
        {
            return;
        }

        var activeTorrents = _repository.All()
            .Where(t => t.Status == TorrentStatus.Downloading)
            .ToList();

        if (activeTorrents.Count == 0)
        {
            return;
        }

        var matchingTorrents = activeTorrents
            .Where(t => MatchesVolume(t.SavePath, message.DrivePath))
            .ToList();

        if (matchingTorrents.Count == 0)
        {
            return;
        }

        _logger.Warn("Emergency low disk space on volume {0}: pausing {1} active downloading torrents.", message.DrivePath, matchingTorrents.Count);
        foreach (var torrent in matchingTorrents)
        {
            torrent.Pause();
            torrent.ErrorMessage = EmergencyDiskSpacePausePrefix + message.DrivePath;
        }

        _repository.UpdateMany(matchingTorrents);
        foreach (var torrent in matchingTorrents)
        {
            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
            _eventAggregator.PublishEvent(new TorrentUpdatedEvent(torrent));
        }
    }

    public void Handle(DiskSpaceRestoredEvent message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.DrivePath))
        {
            return;
        }

        var pausedTorrents = _repository.All()
            .Where(t => t.Status == TorrentStatus.Paused &&
                        !string.IsNullOrEmpty(t.ErrorMessage) &&
                        t.ErrorMessage.StartsWith(EmergencyDiskSpacePausePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pausedTorrents.Count == 0)
        {
            return;
        }

        var matchingTorrents = pausedTorrents
            .Where(t => MatchesVolume(t.SavePath, message.DrivePath))
            .ToList();

        if (matchingTorrents.Count == 0)
        {
            return;
        }

        _logger.Info("Disk space restored on volume {0}: resuming {1} auto-paused torrents.", message.DrivePath, matchingTorrents.Count);
        foreach (var torrent in matchingTorrents)
        {
            torrent.ErrorMessage = null;
            torrent.Resume();
        }

        _repository.UpdateMany(matchingTorrents);
        foreach (var torrent in matchingTorrents)
        {
            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
            _eventAggregator.PublishEvent(new TorrentUpdatedEvent(torrent));
        }
    }

    private bool MatchesVolume(string savePath, string volumePath)
    {
        if (string.IsNullOrWhiteSpace(volumePath))
        {
            return false;
        }

        if (DiskSpaceService != null)
        {
            try
            {
                var diskInfo = DiskSpaceService.GetDiskSpaceForPath(savePath);
                if (diskInfo != null && !string.IsNullOrWhiteSpace(diskInfo.Path))
                {
                    if (string.Equals(diskInfo.Path, volumePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Trace(ex, "Failed to match volume via DiskSpaceService for path {0}", savePath);
            }
        }

        return IsPathPrefixOf(savePath, volumePath);
    }

    private static bool IsPathPrefixOf(string path, string volumePath)
    {
        if (string.IsNullOrWhiteSpace(volumePath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return volumePath == "/" || volumePath.StartsWith("C:", StringComparison.OrdinalIgnoreCase);
        }

        string normPath;
        try
        {
            normPath = Path.GetFullPath(path).Replace('\\', '/');
        }
        catch
        {
            normPath = path.Replace('\\', '/');
        }

        string normVol;
        try
        {
            normVol = Path.GetFullPath(volumePath).Replace('\\', '/');
        }
        catch
        {
            normVol = volumePath.Replace('\\', '/');
        }

        if (!normPath.EndsWith("/"))
        {
            normPath += "/";
        }

        if (!normVol.EndsWith("/"))
        {
            normVol += "/";
        }

        return normPath.StartsWith(normVol, StringComparison.OrdinalIgnoreCase);
    }
}
