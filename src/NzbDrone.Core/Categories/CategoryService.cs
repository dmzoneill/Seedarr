using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Automation;
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

    void InvalidateCache();

    bool CanDownload(Torrent torrent, IEnumerable<Torrent> activeTorrents, int? globalMaxActiveDownloads = null, bool ignoreSlowTorrents = true);

    bool CanUpload(Torrent torrent, IEnumerable<Torrent> activeTorrents, int? globalMaxActiveUploads = null);

    List<Torrent> EvaluateDownloadQueue(IEnumerable<Torrent> queuedTorrents, IEnumerable<Torrent> activeTorrents, int? globalMaxActiveDownloads = null, bool ignoreSlowTorrents = true);

    List<Torrent> EvaluateDownloadQueue(IEnumerable<Torrent> allTorrents, int? globalMaxActiveDownloads = null, bool ignoreSlowTorrents = true);
}

public class CategoryService : ICategoryService, IQueueService
{
    private const long SlowTorrentThresholdBytesPerSec = 10 * 1024; // 10 KB/s
    private static readonly Category NotFoundCategory = new();

    private readonly ICategoryRepository _repository;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentRepository _torrentRepository;
    private readonly Lazy<ITorrentService> _torrentService;
    private readonly IAutomationScriptRepository _automationScriptRepository;
    private readonly Logger _logger;

    private readonly ConcurrentDictionary<string, Category> _nameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, Category> _idCache = new();
    private readonly object _cacheLock = new();
    private volatile Category _defaultCategory;
    private volatile bool _isCacheInitialized;

    public CategoryService(
        ICategoryRepository repository,
        IEventAggregator eventAggregator,
        ITorrentRepository torrentRepository = null,
        Lazy<ITorrentService> torrentService = null,
        IAutomationScriptRepository automationScriptRepository = null)
    {
        _repository = repository;
        _eventAggregator = eventAggregator;
        _torrentRepository = torrentRepository;
        _torrentService = torrentService;
        _automationScriptRepository = automationScriptRepository ??
            ((repository as BasicRepository<Category>)?.Database != null
                ? new AutomationScriptRepository(((BasicRepository<Category>)repository).Database)
                : null);
        _logger = LogManager.GetCurrentClassLogger();
    }

    private void EnsureCacheInitialized()
    {
        if (_isCacheInitialized)
        {
            return;
        }

        lock (_cacheLock)
        {
            if (_isCacheInitialized)
            {
                return;
            }

            var all = _repository.All()?.ToList() ?? new List<Category>();
            _nameCache.Clear();
            _idCache.Clear();
            Category defaultCategory = null;

            foreach (var cat in all)
            {
                if (cat != null)
                {
                    _idCache[cat.Id] = cat;
                    if (!string.IsNullOrWhiteSpace(cat.Name))
                    {
                        _nameCache[cat.Name.Trim().ToLowerInvariant()] = cat;
                    }

                    if (cat.IsDefault)
                    {
                        defaultCategory = cat;
                    }
                }
            }

            _defaultCategory = defaultCategory ?? _repository.GetDefault();
            _isCacheInitialized = true;
        }
    }

    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _nameCache.Clear();
            _idCache.Clear();
            _defaultCategory = null;
            _isCacheInitialized = false;
        }
    }

    public IEnumerable<Category> GetAll()
    {
        EnsureCacheInitialized();
        return _idCache.Values.Where(c => !ReferenceEquals(c, NotFoundCategory)).OrderBy(c => c.Name).ToList();
    }

    public Category Get(int id)
    {
        EnsureCacheInitialized();

        if (_idCache.TryGetValue(id, out var cat))
        {
            return ReferenceEquals(cat, NotFoundCategory) ? null : cat;
        }

        var fromDb = _repository.Get(id);
        _idCache[id] = fromDb ?? NotFoundCategory;
        if (fromDb != null && !string.IsNullOrWhiteSpace(fromDb.Name))
        {
            _nameCache[fromDb.Name.Trim().ToLowerInvariant()] = fromDb;
        }

        return fromDb;
    }

    public Category GetByName(string name)
    {
        EnsureCacheInitialized();

        if (string.IsNullOrWhiteSpace(name))
        {
            return _defaultCategory ?? _repository.GetDefault();
        }

        var key = name.Trim().ToLowerInvariant();
        if (_nameCache.TryGetValue(key, out var category))
        {
            return ReferenceEquals(category, NotFoundCategory) ? null : category;
        }

        var fromDb = _repository.GetByName(name);
        _nameCache[key] = fromDb ?? NotFoundCategory;
        if (fromDb != null)
        {
            _idCache[fromDb.Id] = fromDb;
            if (fromDb.IsDefault)
            {
                _defaultCategory = fromDb;
            }
        }

        return fromDb;
    }

    public Category Add(Category category)
    {
        if (category == null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name cannot be empty.", nameof(category));
        }

        var trimmedName = category.Name.Trim();
        category.Name = trimmedName;

        EnsureCacheInitialized();
        if (_nameCache.TryGetValue(trimmedName.ToLowerInvariant(), out var existing) && !ReferenceEquals(existing, NotFoundCategory))
        {
            throw new InvalidOperationException($"A category with the name '{trimmedName}' already exists.");
        }

        _logger.Info("Adding category: {0}", category.Name);

        if (!string.IsNullOrWhiteSpace(category.SavePath))
        {
            ValidateSavePath(category.SavePath);
            category.SavePath = NormalizeSavePath(category.SavePath);
            VerifySavePathAccess(category.SavePath);
        }

        var inserted = _repository.Insert(category);
        if (inserted.IsDefault)
        {
            ClearExistingDefaults(inserted.Id);
        }

        _nameCache[inserted.Name.Trim().ToLowerInvariant()] = inserted;
        _idCache[inserted.Id] = inserted;
        if (inserted.IsDefault)
        {
            _defaultCategory = inserted;
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

        if (string.IsNullOrWhiteSpace(category.Name))
        {
            throw new ArgumentException("Category name cannot be empty.", nameof(category));
        }

        var trimmedName = category.Name.Trim();
        category.Name = trimmedName;

        EnsureCacheInitialized();
        _logger.Info("Updating category: {0}", category.Name);
        var existing = _repository.Get(category.Id);

        if (existing != null && !string.Equals(existing.Name, trimmedName, StringComparison.OrdinalIgnoreCase))
        {
            if (_nameCache.TryGetValue(trimmedName.ToLowerInvariant(), out var duplicate) &&
                !ReferenceEquals(duplicate, NotFoundCategory) && duplicate.Id != category.Id)
            {
                throw new InvalidOperationException($"A category with the name '{trimmedName}' already exists.");
            }
        }

        if (!string.IsNullOrWhiteSpace(category.SavePath))
        {
            ValidateSavePath(category.SavePath);
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
            SyncAutomationScriptsOnCategoryRename(existing.Name, updated.Name);
            _nameCache.TryRemove(existing.Name.Trim().ToLowerInvariant(), out _);
        }

        _nameCache[updated.Name.Trim().ToLowerInvariant()] = updated;
        _idCache[updated.Id] = updated;
        if (updated.IsDefault)
        {
            _defaultCategory = updated;
        }

        _eventAggregator?.PublishEvent(new CategoryUpdatedEvent { Category = updated });
        return updated;
    }

    private void ClearExistingDefaults(int currentCategoryId)
    {
        _repository.SetExclusiveDefault(currentCategoryId);
        foreach (var c in _idCache.Values)
        {
            if (!ReferenceEquals(c, NotFoundCategory) && c.Id != currentCategoryId)
            {
                c.IsDefault = false;
            }
        }
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

            SyncAutomationScriptsOnCategoryDelete(cat.Name);
        }

        _repository.Delete(id);
        _idCache.TryRemove(id, out _);
        if (!string.IsNullOrWhiteSpace(cat.Name))
        {
            _nameCache.TryRemove(cat.Name.Trim().ToLowerInvariant(), out _);
        }

        if (cat.IsDefault || (_defaultCategory != null && _defaultCategory.Id == id))
        {
            _defaultCategory = null;
        }

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

    private void SyncAutomationScriptsOnCategoryRename(string oldName, string newName)
    {
        if (_automationScriptRepository == null || string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        try
        {
            var scripts = _automationScriptRepository.All()?.ToList() ?? new List<AutomationScript>();
            var updatedScripts = new List<AutomationScript>();

            foreach (var script in scripts)
            {
                if (script.TargetCategories == null || script.TargetCategories.Count == 0)
                {
                    continue;
                }

                var changed = false;
                for (var i = 0; i < script.TargetCategories.Count; i++)
                {
                    if (string.Equals(script.TargetCategories[i]?.Trim(), oldName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        script.TargetCategories[i] = newName.Trim();
                        changed = true;
                    }
                }

                if (changed)
                {
                    script.TargetCategories = script.TargetCategories
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    updatedScripts.Add(script);
                }
            }

            if (updatedScripts.Count > 0)
            {
                _automationScriptRepository.UpdateMany(updatedScripts);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to cascade category rename from '{0}' to '{1}' for automation scripts.", oldName, newName);
        }
    }

    private void SyncAutomationScriptsOnCategoryDelete(string categoryName)
    {
        if (_automationScriptRepository == null || string.IsNullOrWhiteSpace(categoryName))
        {
            return;
        }

        try
        {
            var scripts = _automationScriptRepository.All()?.ToList() ?? new List<AutomationScript>();
            var updatedScripts = new List<AutomationScript>();

            foreach (var script in scripts)
            {
                if (script.TargetCategories == null || script.TargetCategories.Count == 0)
                {
                    continue;
                }

                var removedCount = script.TargetCategories.RemoveAll(c =>
                    string.Equals(c?.Trim(), categoryName.Trim(), StringComparison.OrdinalIgnoreCase));

                if (removedCount > 0)
                {
                    updatedScripts.Add(script);
                }
            }

            if (updatedScripts.Count > 0)
            {
                _automationScriptRepository.UpdateMany(updatedScripts);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to cascade category delete for '{0}' to automation scripts.", categoryName);
        }
    }

    private IEnumerable<Torrent> GetTorrents()
    {
        if (_torrentService?.Value != null)
        {
            return _torrentService.Value.GetAll();
        }

        if (_torrentRepository != null)
        {
            return _torrentRepository.All().ToList();
        }

        return Enumerable.Empty<Torrent>();
    }

    private void UpdateTorrent(Torrent torrent)
    {
        if (_torrentService?.Value != null)
        {
            _torrentService.Value.Update(torrent);
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
            var cat = GetByName(categoryName);
            if (cat != null && !string.IsNullOrWhiteSpace(cat.SavePath))
            {
                return cat.SavePath;
            }
        }

        EnsureCacheInitialized();
        var defaultCat = _defaultCategory ?? _repository.GetDefault();
        if (defaultCat != null && !string.IsNullOrWhiteSpace(defaultCat.SavePath))
        {
            return defaultCat.SavePath;
        }

        return defaultPath;
    }

    public static void ValidateSavePath(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        if (savePath.Contains('\0'))
        {
            throw new ArgumentException("Save path cannot contain null bytes.", nameof(savePath));
        }

        if (savePath.Contains(".."))
        {
            throw new ArgumentException("Save path cannot contain directory traversal sequences ('..').", nameof(savePath));
        }

        if (savePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException("Save path contains invalid path characters.", nameof(savePath));
        }

        if (!IsPathRooted(savePath))
        {
            throw new ArgumentException("Save path must be an absolute path (e.g. '/downloads/movies' or 'D:\\Downloads\\Movies').", nameof(savePath));
        }

        try
        {
            _ = Path.GetFullPath(savePath);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid save path format: {ex.Message}", nameof(savePath), ex);
        }
    }

    public static bool IsPathRooted(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (Path.IsPathRooted(path))
        {
            return true;
        }

        // Support Windows drive roots (e.g. D:\path or C:/path) on non-Windows platforms
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        {
            return true;
        }

        // Support UNC paths (\\server\share) on non-Windows platforms
        if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
        {
            return true;
        }

        return false;
    }

    private static void VerifySavePathAccess(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        if (!OperatingSystem.IsWindows() &&
            ((savePath.Length >= 2 && char.IsLetter(savePath[0]) && savePath[1] == ':') ||
                (savePath.Length >= 2 && savePath[0] == '\\' && savePath[1] == '\\')))
        {
            return;
        }

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

        var isWindowsOnUnix = !OperatingSystem.IsWindows() &&
            ((trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':') ||
                (trimmed.Length >= 2 && trimmed[0] == '\\' && trimmed[1] == '\\'));

        if (isWindowsOnUnix)
        {
            var normalized = trimmed.Replace('/', '\\');
            var driveRoot = normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':';
            if (driveRoot && (normalized.Length == 2 || (normalized.Length == 3 && normalized[2] == '\\')))
            {
                return normalized.Length == 2 ? normalized + "\\" : normalized;
            }

            return normalized.TrimEnd('\\');
        }

        try
        {
            var fullPath = Path.GetFullPath(trimmed);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            var root = Path.GetPathRoot(trimmed);
            if (!string.IsNullOrEmpty(root) && string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return trimmed.TrimEnd('/', '\\');
        }
    }

    private static bool IsSlotConsuming(Torrent torrent, bool ignoreSlowTorrents = true)
    {
        if (torrent == null)
        {
            return false;
        }

        if (torrent.IsExtinct || torrent.Status == TorrentStatus.StalledNoSeeds)
        {
            return false;
        }

        if (ignoreSlowTorrents)
        {
            if (torrent.Status == TorrentStatus.Downloading && torrent.DownloadSpeed < SlowTorrentThresholdBytesPerSec)
            {
                return false;
            }

            if (torrent.DownloadSpeed > 0 && torrent.DownloadSpeed < SlowTorrentThresholdBytesPerSec)
            {
                return false;
            }
        }

        return true;
    }

    public bool CanDownload(Torrent torrent, IEnumerable<Torrent> activeTorrents, int? globalMaxActiveDownloads = null, bool ignoreSlowTorrents = true)
    {
        if (torrent == null)
        {
            return false;
        }

        var activeList = (activeTorrents as IList<Torrent> ?? activeTorrents?.ToList() ?? new List<Torrent>())
            .Where(t => IsSlotConsuming(t, ignoreSlowTorrents))
            .ToList();

        if (globalMaxActiveDownloads.HasValue && globalMaxActiveDownloads.Value > 0 && activeList.Count >= globalMaxActiveDownloads.Value)
        {
            return false;
        }

        var category = GetByName(torrent.Category);
        if (category != null && category.MaxActiveDownloads.HasValue && category.MaxActiveDownloads.Value > 0)
        {
            var categoryName = category.Name;
            var activeInCategory = activeList.Count(t =>
            {
                var cat = GetByName(t.Category);
                return cat != null && string.Equals(cat.Name, categoryName, StringComparison.OrdinalIgnoreCase);
            });

            if (activeInCategory >= category.MaxActiveDownloads.Value)
            {
                return false;
            }
        }

        if (globalMaxActiveDownloads.HasValue && globalMaxActiveDownloads.Value > 0)
        {
            var g = globalMaxActiveDownloads.Value;
            var reserved = category != null ? Math.Max(0, category.ReservedDownloadSlots) : 0;
            if (category?.MaxActiveDownloads.HasValue == true && category.MaxActiveDownloads.Value > 0)
            {
                reserved = Math.Min(reserved, category.MaxActiveDownloads.Value);
            }

            var categoryName = category?.Name;
            var activeInCategory = categoryName != null
                ? activeList.Count(t =>
                {
                    var cat = GetByName(t.Category);
                    return cat != null && string.Equals(cat.Name, categoryName, StringComparison.OrdinalIgnoreCase);
                })
                : 0;

            if (activeInCategory < reserved)
            {
                return true;
            }

            var allCats = GetAll()?.ToList() ?? new List<Category>();
            var totalReserved = allCats.Sum(c =>
            {
                var r = Math.Max(0, c.ReservedDownloadSlots);
                if (c.MaxActiveDownloads.HasValue && c.MaxActiveDownloads.Value > 0)
                {
                    r = Math.Min(r, c.MaxActiveDownloads.Value);
                }

                return r;
            });

            var generalCapacity = Math.Max(0, g - totalReserved);
            var generalUsed = 0;
            foreach (var c in allCats)
            {
                var r = Math.Max(0, c.ReservedDownloadSlots);
                if (c.MaxActiveDownloads.HasValue && c.MaxActiveDownloads.Value > 0)
                {
                    r = Math.Min(r, c.MaxActiveDownloads.Value);
                }

                var act = activeList.Count(t =>
                {
                    var cat = GetByName(t.Category);
                    return cat != null && string.Equals(cat.Name, c.Name, StringComparison.OrdinalIgnoreCase);
                });
                generalUsed += Math.Max(0, act - r);
            }

            var uncategorizedActive = activeList.Count(t => GetByName(t.Category) == null);
            generalUsed += uncategorizedActive;

            if (generalUsed >= generalCapacity)
            {
                return false;
            }
        }

        return true;
    }

    public bool CanUpload(Torrent torrent, IEnumerable<Torrent> activeTorrents, int? globalMaxActiveUploads = null)
    {
        if (torrent == null)
        {
            return false;
        }

        var activeList = activeTorrents as IList<Torrent> ?? activeTorrents?.ToList() ?? new List<Torrent>();

        if (globalMaxActiveUploads.HasValue && globalMaxActiveUploads.Value > 0 && activeList.Count >= globalMaxActiveUploads.Value)
        {
            return false;
        }

        var category = GetByName(torrent.Category);
        if (category != null && category.MaxActiveUploads.HasValue && category.MaxActiveUploads.Value > 0)
        {
            var categoryName = category.Name;
            var activeInCategory = activeList.Count(t =>
            {
                var cat = GetByName(t.Category);
                return cat != null && string.Equals(cat.Name, categoryName, StringComparison.OrdinalIgnoreCase);
            });

            if (activeInCategory >= category.MaxActiveUploads.Value)
            {
                return false;
            }
        }

        return true;
    }

    public List<Torrent> EvaluateDownloadQueue(IEnumerable<Torrent> allTorrents, int? globalMaxActiveDownloads = null, bool ignoreSlowTorrents = true)
    {
        if (allTorrents == null)
        {
            return new List<Torrent>();
        }

        var list = allTorrents.ToList();
        var activeTorrents = list.Where(t => (t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.StalledNoSeeds) && !t.IsExtinct).ToList();
        var queuedTorrents = list.Where(t => t.Status == TorrentStatus.Queued).OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();

        return EvaluateDownloadQueue(queuedTorrents, activeTorrents, globalMaxActiveDownloads, ignoreSlowTorrents);
    }

    public List<Torrent> EvaluateDownloadQueue(IEnumerable<Torrent> queuedTorrents, IEnumerable<Torrent> activeTorrents, int? globalMaxActiveDownloads = null, bool ignoreSlowTorrents = true)
    {
        var promoted = new List<Torrent>();
        if (queuedTorrents == null)
        {
            return promoted;
        }

        var queuedList = queuedTorrents.ToList();
        if (queuedList.Count == 0)
        {
            return promoted;
        }

        var activeList = activeTorrents?
            .Where(t => IsSlotConsuming(t, ignoreSlowTorrents))
            .ToList() ?? new List<Torrent>();
        var totalActiveCount = activeList.Count;

        // Map active counts by category
        var activeCountByCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in activeList)
        {
            var cat = GetByName(t.Category);
            if (cat != null && !string.IsNullOrWhiteSpace(cat.Name))
            {
                var key = cat.Name;
                activeCountByCategory[key] = activeCountByCategory.GetValueOrDefault(key, 0) + 1;
            }
        }

        var allCategories = GetAll()?.ToList() ?? new List<Category>();

        // Calculate available reserved download slots per category
        var availableReservedSlots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalReservedCapacity = 0;
        foreach (var cat in allCategories)
        {
            if (cat.ReservedDownloadSlots > 0)
            {
                var key = cat.Name;
                var currentActive = activeCountByCategory.GetValueOrDefault(key, 0);
                var effectiveReserved = cat.ReservedDownloadSlots;
                if (cat.MaxActiveDownloads.HasValue && cat.MaxActiveDownloads.Value > 0)
                {
                    effectiveReserved = Math.Min(effectiveReserved, cat.MaxActiveDownloads.Value);
                }

                totalReservedCapacity += effectiveReserved;
                var available = Math.Max(0, effectiveReserved - currentActive);
                if (available > 0)
                {
                    availableReservedSlots[key] = available;
                }
            }
        }

        // Calculate general pool capacity and usage if a global limit is specified
        var hasGlobalLimit = globalMaxActiveDownloads.HasValue && globalMaxActiveDownloads.Value > 0;
        var globalLimit = hasGlobalLimit ? globalMaxActiveDownloads.Value : 0;
        var generalCapacity = hasGlobalLimit ? Math.Max(0, globalLimit - totalReservedCapacity) : int.MaxValue;

        var generalUsed = 0;
        if (hasGlobalLimit)
        {
            foreach (var cat in allCategories)
            {
                var key = cat.Name;
                var effectiveReserved = cat.ReservedDownloadSlots > 0 ? cat.ReservedDownloadSlots : 0;
                if (cat.MaxActiveDownloads.HasValue && cat.MaxActiveDownloads.Value > 0)
                {
                    effectiveReserved = Math.Min(effectiveReserved, cat.MaxActiveDownloads.Value);
                }

                var currentActive = activeCountByCategory.GetValueOrDefault(key, 0);
                generalUsed += Math.Max(0, currentActive - effectiveReserved);
            }

            var uncategorizedActive = activeList.Count(t => GetByName(t.Category) == null);
            generalUsed += uncategorizedActive;
        }

        var promotedIds = new HashSet<int>();

        // Phase 1: Allocate reserved slots to matching queued torrents in sort order
        if (availableReservedSlots.Count > 0)
        {
            foreach (var torrent in queuedList)
            {
                if (hasGlobalLimit && totalActiveCount >= globalLimit)
                {
                    break;
                }

                var cat = GetByName(torrent.Category);
                if (cat != null && !string.IsNullOrWhiteSpace(cat.Name))
                {
                    var key = cat.Name;
                    if (availableReservedSlots.TryGetValue(key, out var avail) && avail > 0)
                    {
                        var currentActive = activeCountByCategory.GetValueOrDefault(key, 0);
                        if (!cat.MaxActiveDownloads.HasValue || cat.MaxActiveDownloads.Value <= 0 || currentActive < cat.MaxActiveDownloads.Value)
                        {
                            promoted.Add(torrent);
                            promotedIds.Add(torrent.Id);
                            availableReservedSlots[key] = avail - 1;
                            activeCountByCategory[key] = currentActive + 1;
                            totalActiveCount++;
                        }
                    }
                }
            }
        }

        // Phase 2: Allocate general / remaining global slots in sort order
        foreach (var torrent in queuedList)
        {
            if (promotedIds.Contains(torrent.Id))
            {
                continue;
            }

            if (hasGlobalLimit && (totalActiveCount >= globalLimit || generalUsed >= generalCapacity))
            {
                break;
            }

            var cat = GetByName(torrent.Category);
            if (cat != null && !string.IsNullOrWhiteSpace(cat.Name))
            {
                var key = cat.Name;
                var currentActive = activeCountByCategory.GetValueOrDefault(key, 0);
                if (cat.MaxActiveDownloads.HasValue && cat.MaxActiveDownloads.Value > 0 && currentActive >= cat.MaxActiveDownloads.Value)
                {
                    continue;
                }

                activeCountByCategory[key] = currentActive + 1;
            }

            promoted.Add(torrent);
            promotedIds.Add(torrent.Id);
            totalActiveCount++;
            if (hasGlobalLimit)
            {
                generalUsed++;
            }
        }

        return promoted;
    }
}
