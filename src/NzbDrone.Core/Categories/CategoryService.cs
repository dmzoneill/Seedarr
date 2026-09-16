using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
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
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;

    public CategoryService(
        ICategoryRepository repository,
        IEventAggregator eventAggregator,
        ITorrentRepository torrentRepository = null,
        ITorrentService torrentService = null)
    {
        _repository = repository;
        _eventAggregator = eventAggregator;
        _torrentRepository = torrentRepository;
        _torrentService = torrentService;
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

        if (existing != null &&
            !string.IsNullOrWhiteSpace(existing.Name) &&
            !string.Equals(existing.Name, updated.Name, StringComparison.OrdinalIgnoreCase))
        {
            SyncTorrentsOnCategoryRename(existing.Name, updated.Name);
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

        var affectedTorrentIds = new List<int>();

        if (!string.IsNullOrWhiteSpace(cat.Name))
        {
            var torrents = GetTorrents();
            foreach (var torrent in torrents)
            {
                var changed = false;

                if (string.Equals(torrent.Category, cat.Name, StringComparison.OrdinalIgnoreCase))
                {
                    torrent.Category = string.Empty;
                    changed = true;
                }

                var scrubbedLabel = ScrubLabelOnDelete(torrent.Label, cat.Name);
                if (!string.Equals(torrent.Label, scrubbedLabel, StringComparison.Ordinal))
                {
                    torrent.Label = scrubbedLabel;
                    changed = true;
                }

                if (changed)
                {
                    affectedTorrentIds.Add(torrent.Id);
                    UpdateTorrent(torrent);
                }
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

    private void SyncTorrentsOnCategoryRename(string oldName, string newName)
    {
        var torrents = GetTorrents();
        foreach (var torrent in torrents)
        {
            var changed = false;

            if (string.Equals(torrent.Category, oldName, StringComparison.OrdinalIgnoreCase))
            {
                torrent.Category = newName;
                changed = true;
            }

            var updatedLabel = SynchronizeLabelOnRename(torrent.Label, oldName, newName);
            if (!string.Equals(torrent.Label, updatedLabel, StringComparison.Ordinal))
            {
                torrent.Label = updatedLabel;
                changed = true;
            }

            if (changed)
            {
                UpdateTorrent(torrent);
            }
        }
    }

    private IEnumerable<Torrent> GetTorrents()
    {
        if (_torrentService != null)
        {
            return _torrentService.GetAll();
        }

        if (_torrentRepository != null)
        {
            return _torrentRepository.All().ToList();
        }

        return Enumerable.Empty<Torrent>();
    }

    private void UpdateTorrent(Torrent torrent)
    {
        if (_torrentService != null)
        {
            _torrentService.Update(torrent);
        }
        else if (_torrentRepository != null)
        {
            _torrentRepository.Update(torrent);
            _eventAggregator?.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
            _eventAggregator?.PublishEvent(new TorrentUpdatedEvent(torrent));
        }
    }

    public static string SynchronizeLabelOnRename(string label, string oldCategoryName, string newCategoryName)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(oldCategoryName))
        {
            return label;
        }

        var trimmedOld = oldCategoryName.Trim();
        var trimmedNew = (newCategoryName ?? string.Empty).Trim();

        if (string.Equals(label.Trim(), trimmedOld, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedNew;
        }

        var hasDelimiter = label.Contains(',') || label.Contains(';');
        if (!hasDelimiter)
        {
            return label;
        }

        var delimiter = label.Contains(';') && !label.Contains(',') ? ';' : ',';
        var tokens = label.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(t => t.Trim())
                          .Where(t => !string.IsNullOrEmpty(t))
                          .ToList();

        var changed = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (string.Equals(tokens[i], trimmedOld, StringComparison.OrdinalIgnoreCase))
            {
                tokens[i] = trimmedNew;
                changed = true;
            }
        }

        if (!changed)
        {
            return label;
        }

        var joinSeparator = delimiter == ';' ? "; " : ", ";
        return string.Join(joinSeparator, tokens.Where(t => !string.IsNullOrEmpty(t)));
    }

    public static string ScrubLabelOnDelete(string label, string categoryNameToRemove)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(categoryNameToRemove))
        {
            return label ?? string.Empty;
        }

        var trimmedCategory = categoryNameToRemove.Trim();

        if (string.Equals(label.Trim(), trimmedCategory, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var hasDelimiter = label.Contains(',') || label.Contains(';');
        if (!hasDelimiter)
        {
            return label;
        }

        var delimiter = label.Contains(';') && !label.Contains(',') ? ';' : ',';
        var tokens = label.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(t => t.Trim())
                          .Where(t => !string.IsNullOrEmpty(t))
                          .ToList();

        var remaining = tokens.Where(t => !string.Equals(t, trimmedCategory, StringComparison.OrdinalIgnoreCase)).ToList();

        if (remaining.Count == tokens.Count)
        {
            return label;
        }

        if (remaining.Count == 0)
        {
            return string.Empty;
        }

        var joinSeparator = delimiter == ';' ? "; " : ", ";
        return string.Join(joinSeparator, remaining);
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
