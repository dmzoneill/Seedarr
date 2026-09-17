using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
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
}

public class TorrentService : ITorrentService,
    IHandle<VpnKillSwitchTriggeredEvent>,
    IHandle<VpnInterfaceRestoredEvent>,
    IHandle<VpnRestoredEvent>
{
    private readonly ITorrentRepository _repository;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IEventAggregator _eventAggregator;
    private readonly object _sortOrderLock = new();
    private readonly Logger _logger;

    public TorrentService(ITorrentRepository repository, ITorrentFileService torrentFileService, ITrackerEntryService trackerEntryService, IEventAggregator eventAggregator)
    {
        _repository = repository;
        _torrentFileService = torrentFileService;
        _trackerEntryService = trackerEntryService;
        _eventAggregator = eventAggregator;
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

        _logger.Info("Rechecking torrent: {0}", torrent.Name);

        torrent.Progress = torrent.Progress >= 1.0 ? 1.0 : 0.0;
        torrent.LastActive = DateTime.UtcNow;

        return _repository.Update(torrent);
    }

    public void MoveQueue(int id, string position)
    {
        var all = _repository.All().OrderBy(t => t.SortOrder).ToList();
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

        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].SortOrder != i)
            {
                all[i].SortOrder = i;
                _repository.Update(all[i]);
            }
        }
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
}
