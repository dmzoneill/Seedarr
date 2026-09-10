using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Categories;

public class CategoryUpdatedEvent : IEvent
{
    public Category Category { get; set; }
}

public class CategoryDeletedEvent : IEvent
{
    public int CategoryId { get; set; }

    public string CategoryName { get; set; }

    public List<int> AffectedTorrentIds { get; set; } = new();
}

public interface ICategoryService
{
    IEnumerable<Category> GetAll();

    Category Get(int id);

    Category GetByName(string name);

    Category Add(Category category);

    Category Update(Category category);

    void Delete(int id);

    string GetSavePathForCategory(string categoryName, string defaultPath = "");
}

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _repository;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentRepository _torrentRepository;
    private readonly Logger _logger;

    public CategoryService(
        ICategoryRepository repository,
        IEventAggregator eventAggregator,
        ITorrentRepository torrentRepository = null)
    {
        _repository = repository;
        _eventAggregator = eventAggregator;
        _torrentRepository = torrentRepository;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public IEnumerable<Category> GetAll()
    {
        return _repository.All().OrderBy(c => c.Name);
    }

    public Category Get(int id)
    {
        return _repository.Get(id);
    }

    public Category GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return _repository.GetDefault();
        }

        return _repository.GetByName(name);
    }

    public Category Add(Category category)
    {
        if (category == null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        _logger.Info("Adding category: {0}", category.Name);
        if (category.IsDefault)
        {
            ClearExistingDefaults(0);
        }

        var inserted = _repository.Insert(category);
        _eventAggregator?.PublishEvent(new CategoryUpdatedEvent { Category = inserted });
        return inserted;
    }

    public Category Update(Category category)
    {
        if (category == null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        _logger.Info("Updating category: {0}", category.Name);
        var existing = _repository.Get(category.Id);

        if (category.IsDefault)
        {
            ClearExistingDefaults(category.Id);
        }

        var updated = _repository.Update(category);

        if (_torrentRepository != null && existing != null &&
            !string.IsNullOrWhiteSpace(existing.Name) &&
            !string.Equals(existing.Name, updated.Name, StringComparison.OrdinalIgnoreCase))
        {
            var torrents = _torrentRepository.All()
                .Where(t => string.Equals(t.Category, existing.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var torrent in torrents)
            {
                torrent.Category = updated.Name;
                _torrentRepository.Update(torrent);
            }
        }

        _eventAggregator?.PublishEvent(new CategoryUpdatedEvent { Category = updated });
        return updated;
    }

    private void ClearExistingDefaults(int currentCategoryId)
    {
        var existingDefaults = _repository.All().Where(c => c.IsDefault && c.Id != currentCategoryId);
        foreach (var existing in existingDefaults)
        {
            existing.IsDefault = false;
            _repository.Update(existing);
        }
    }

    public void Delete(int id)
    {
        var cat = _repository.Get(id);
        if (cat == null)
        {
            return;
        }

        _logger.Info("Deleting category id: {0} ({1})", id, cat.Name);

        var affectedTorrentIds = new List<int>();
        if (_torrentRepository != null && !string.IsNullOrWhiteSpace(cat.Name))
        {
            var torrents = _torrentRepository.All()
                .Where(t => string.Equals(t.Category, cat.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var torrent in torrents)
            {
                affectedTorrentIds.Add(torrent.Id);
                torrent.Category = string.Empty;
                _torrentRepository.Update(torrent);
            }
        }

        _repository.Delete(id);
        _eventAggregator?.PublishEvent(new CategoryDeletedEvent
        {
            CategoryId = id,
            CategoryName = cat.Name,
            AffectedTorrentIds = affectedTorrentIds,
        });
    }

    public string GetSavePathForCategory(string categoryName, string defaultPath = "")
    {
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            var cat = _repository.GetByName(categoryName);
            if (cat != null && !string.IsNullOrWhiteSpace(cat.SavePath))
            {
                return cat.SavePath;
            }
        }

        var defaultCat = _repository.GetDefault();
        if (defaultCat != null && !string.IsNullOrWhiteSpace(defaultCat.SavePath))
        {
            return defaultCat.SavePath;
        }

        return defaultPath;
    }
}
