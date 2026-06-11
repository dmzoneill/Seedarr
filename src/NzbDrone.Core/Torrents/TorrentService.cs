using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public interface ITorrentService
{
    List<Torrent> GetAll();
    Torrent Get(int id);
    Torrent Add(Torrent torrent);
    Torrent Update(Torrent torrent);
    void Delete(int id);
}

public class TorrentService : ITorrentService
{
    private readonly ITorrentRepository _repository;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    public TorrentService(ITorrentRepository repository, IEventAggregator eventAggregator)
    {
        _repository = repository;
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

    public Torrent Add(Torrent torrent)
    {
        _logger.Info("Adding torrent: {0}", torrent.Name);
        var added = _repository.Insert(torrent);
        _eventAggregator.PublishEvent(new TorrentAddedEvent(added));
        return added;
    }

    public Torrent Update(Torrent torrent)
    {
        _logger.Info("Updating torrent: {0}", torrent.Name);
        return _repository.Update(torrent);
    }

    public void Delete(int id)
    {
        _logger.Info("Deleting torrent {0}", id);
        _repository.Delete(id);
        _eventAggregator.PublishEvent(new TorrentDeletedEvent(id));
    }
}
