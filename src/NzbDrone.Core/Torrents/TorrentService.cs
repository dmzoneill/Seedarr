using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public interface ITorrentService
{
    List<Torrent> GetAll();
    Torrent Get(int id);
    bool ExistsByInfoHash(string infoHash);
    Torrent Add(Torrent torrent);
    Torrent Update(Torrent torrent);
    void Delete(int id, bool deleteFiles = false);
    Torrent Recheck(int id);
    void MoveQueue(int id, string position);
}

public class TorrentService : ITorrentService
{
    private readonly ITorrentRepository _repository;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IEventAggregator _eventAggregator;
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

    public Torrent Get(int id)
    {
        return _repository.Get(id);
    }

    public bool ExistsByInfoHash(string infoHash)
    {
        return _repository.ExistsByInfoHash(infoHash);
    }

    public Torrent Add(Torrent torrent)
    {
        _logger.Info("Adding torrent: {0}", torrent.Name);

        var all = _repository.All().ToList();
        torrent.SortOrder = all.Count > 0 ? all.Max(t => t.SortOrder) + 1 : 0;

        var added = _repository.Insert(torrent);
        _eventAggregator.PublishEvent(new TorrentAddedEvent(added));
        _eventAggregator.PublishEvent(new ModelEvent<Torrent>(added, ModelAction.Created));
        return added;
    }

    public Torrent Update(Torrent torrent)
    {
        _logger.Info("Updating torrent: {0}", torrent.Name);
        var updated = _repository.Update(torrent);
        _eventAggregator.PublishEvent(new ModelEvent<Torrent>(updated, ModelAction.Updated));
        return updated;
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

        _torrentFileService.DeleteByTorrentId(id);
        _trackerEntryService.DeleteByTorrentId(id);
        _repository.Delete(id);
        _eventAggregator.PublishEvent(new TorrentDeletedEvent(id));

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
}
