using System;
using System.Collections.Generic;
using System.IO;
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

        if (!string.IsNullOrWhiteSpace(category.SavePath))
        {
            category.SavePath = NormalizeSavePath(category.SavePath);
            VerifySavePathAccess(category.SavePath);
        }

        var inserted = _repository.Insert(category);
        if (inserted.IsDefault)
        {
            ClearExistingDefaults(inserted.Id);
        }

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

        if (!string.IsNullOrWhiteSpace(category.SavePath))
        {
            category.SavePath = NormalizeSavePath(category.SavePath);
            VerifySavePathAccess(category.SavePath);
        }

        if (category.IsDefault)
        {
            ClearExistingDefaults(category.Id);
        }

        var updated = _repository.Update(category);

        if (_torrentRepository != null && existing != null &&
            !string.IsNullOrWhiteSpace(existing.Name) &&
            !string.Equals(existing.Name, updated.Name, StringComparison.OrdinalIgnoreCase))
        {
            _torrentRepository.UpdateCategoryName(existing.Name, updated.Name);
        }

        _eventAggregator?.PublishEvent(new CategoryUpdatedEvent { Category = updated });
        return updated;
    }

    private void ClearExistingDefaults(int currentCategoryId)
    {
        _repository.SetExclusiveDefault(currentCategoryId);
    }

    public void Delete(int id)
    {
        var cat = _repository.Get(id);
        if (cat == null)
        {
            return;
        }

        if (cat.IsDefault)
        {
            throw new InvalidOperationException($"Cannot delete category '{cat.Name}' because it is configured as the default category. Designate another category as default before deleting this one.");
        }

        _logger.Info("Deleting category id: {0} ({1})", id, cat.Name);

        if (_torrentRepository != null && !string.IsNullOrWhiteSpace(cat.Name))
        {
            _torrentRepository.ClearCategory(cat.Name);
        }

        _repository.Delete(id);
        _eventAggregator?.PublishEvent(new CategoryDeletedEvent
        {
            CategoryId = id,
            CategoryName = cat.Name,
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

    private static void VerifySavePathAccess(string savePath)
    {
        try
        {
            var dirInfo = Directory.CreateDirectory(savePath);
            var testFile = Path.Combine(dirInfo.FullName, $".seedarr_perm_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Cannot access or write to save path '{savePath}': {ex.Message}", ex);
        }
    }

    public static string NormalizeSavePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        var root = Path.GetPathRoot(trimmed);
        if (!string.IsNullOrEmpty(root) && string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.Length == 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':' && (trimmed[2] == '\\' || trimmed[2] == '/'))
        {
            return trimmed;
        }

        return trimmed.TrimEnd('/', '\\');
    }
}
