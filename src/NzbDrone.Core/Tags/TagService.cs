using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;

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
    private readonly Logger _logger;

    public TagService(ITagRepository repo, IEventAggregator eventAggregator)
    {
        _repo = repo;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<Tag> GetAll() => _repo.All().ToList();
    public Tag Get(int id) => _repo.Get(id);

    public Tag Add(Tag tag)
    {
        _logger.Info("Adding tag: {0}", tag.Label);
        var result = _repo.Insert(tag);
        _eventAggregator.PublishEvent(new TagsUpdatedEvent());
        return result;
    }

    public Tag Update(Tag tag)
    {
        _logger.Info("Updating tag: {0}", tag.Label);
        _repo.Update(tag);
        _eventAggregator.PublishEvent(new TagsUpdatedEvent());
        return tag;
    }

    public void Delete(int id)
    {
        _logger.Info("Deleting tag: {0}", id);
        _repo.Delete(id);
        _eventAggregator.PublishEvent(new TagsUpdatedEvent());
    }
}

public class TagsUpdatedEvent : IEvent
{
}
