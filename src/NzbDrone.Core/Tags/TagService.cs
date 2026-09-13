using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Tags;

public interface ITagService
{
    List<Tag> GetAll();
    Tag Get(int id);
    Tag Add(Tag tag);
    Tag Update(Tag tag);
    void Delete(int id);
}

public class TagService : ITagService
{
    private readonly ITagRepository _repo;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentRepository _torrentRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IIndexerRepository _indexerRepository;
    private readonly IDownloadClientRepository _downloadClientRepository;
    private readonly IArrConnectionRepository _arrConnectionRepository;
    private readonly Logger _logger;

    public TagService(
        ITagRepository repo,
        IEventAggregator eventAggregator,
        ITorrentRepository torrentRepository = null,
        INotificationRepository notificationRepository = null,
        IIndexerRepository indexerRepository = null,
        IDownloadClientRepository downloadClientRepository = null,
        IArrConnectionRepository arrConnectionRepository = null)
    {
        _repo = repo;
        _eventAggregator = eventAggregator;
        _torrentRepository = torrentRepository;
        _notificationRepository = notificationRepository;
        _indexerRepository = indexerRepository;
        _downloadClientRepository = downloadClientRepository;
        _arrConnectionRepository = arrConnectionRepository;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<Tag> GetAll() => _repo.All().ToList();
    public Tag Get(int id) => _repo.Get(id);

    public Tag Add(Tag tag)
    {
        _logger.Info("Adding tag: {0}", tag.Label);
        var result = _repo.Insert(tag);
        _eventAggregator.PublishEvent(new ModelEvent<Tag>(result, ModelAction.Created));
        return result;
    }

    public Tag Update(Tag tag)
    {
        _logger.Info("Updating tag: {0}", tag.Label);
        var result = _repo.Update(tag);
        _eventAggregator.PublishEvent(new ModelEvent<Tag>(result, ModelAction.Updated));
        return result;
    }

    public void Delete(int id)
    {
        var tag = _repo.Get(id);
        _logger.Info("Deleting tag: {0}", id);

        if (_torrentRepository != null)
        {
            var torrents = _torrentRepository.All().Where(t => t.TagIds != null && t.TagIds.Contains(id)).ToList();
            foreach (var torrent in torrents)
            {
                torrent.TagIds.RemoveAll(t => t == id);
                _torrentRepository.Update(torrent);
            }
        }

        if (_notificationRepository != null)
        {
            var notifications = _notificationRepository.All().Where(n => n.Tags != null && n.Tags.Contains(id)).ToList();
            foreach (var notif in notifications)
            {
                notif.Tags.RemoveAll(t => t == id);
                _notificationRepository.Update(notif);
            }
        }

        if (_indexerRepository != null)
        {
            var indexers = _indexerRepository.All().Where(i => i.Tags != null && i.Tags.Contains(id)).ToList();
            foreach (var indexer in indexers)
            {
                indexer.Tags.RemoveAll(t => t == id);
                _indexerRepository.Update(indexer);
            }
        }

        if (_downloadClientRepository != null)
        {
            var clients = _downloadClientRepository.All().Where(c => c.Tags != null && c.Tags.Contains(id)).ToList();
            foreach (var client in clients)
            {
                client.Tags.RemoveAll(t => t == id);
                _downloadClientRepository.Update(client);
            }
        }

        if (_arrConnectionRepository != null)
        {
            var arrs = _arrConnectionRepository.All().Where(a => a.Tags != null && a.Tags.Contains(id)).ToList();
            foreach (var arr in arrs)
            {
                arr.Tags.RemoveAll(t => t == id);
                _arrConnectionRepository.Update(arr);
            }
        }

        _repo.Delete(id);

        if (tag != null)
        {
            _eventAggregator.PublishEvent(new ModelEvent<Tag>(tag, ModelAction.Deleted));
        }
    }
}
