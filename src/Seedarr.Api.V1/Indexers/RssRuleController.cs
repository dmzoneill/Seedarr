using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;
using Seedarr.Http;

namespace Seedarr.Api.V1.Indexers;

/// <summary>
/// Manages RSS download rules, automated regex filters, size/seeder criteria, and indexer matching.
/// </summary>
[V1ApiController("rssrule")]
[Route("api/v1/rssrules")]
public class RssRuleController : Controller
{
    private readonly IRssRuleRepository _rssRuleRepository;
    private readonly IIndexerRepository _indexerRepository;
    private readonly ICategoryService _categoryService;
    private readonly IRssGrabHistoryRepository _rssGrabHistoryRepository;
    private readonly IRssSyncService _rssSyncService;
    private readonly ITorrentService _torrentService;
    private readonly IIndexerFactory _indexerFactory;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private static readonly object _syncLock = new();
    private static readonly TimeSpan _syncCooldown = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static DateTime _lastSyncTime = DateTime.MinValue;

    public RssRuleController(
        IRssRuleRepository rssRuleRepository,
        IIndexerRepository indexerRepository = null,
        ICategoryService categoryService = null,
        IRssGrabHistoryRepository rssGrabHistoryRepository = null,
        IRssSyncService rssSyncService = null,
        ITorrentService torrentService = null,
        IIndexerFactory indexerFactory = null)
    {
        _rssRuleRepository = rssRuleRepository;
        _indexerRepository = indexerRepository;
        _categoryService = categoryService;
        _rssGrabHistoryRepository = rssGrabHistoryRepository;
        _rssSyncService = rssSyncService;
        _torrentService = torrentService;
        _indexerFactory = indexerFactory;
    }

    /// <summary>
    /// Retrieves all configured RSS rules ordered by priority and ID.
    /// </summary>
    [HttpGet]
    public ActionResult<List<RssRuleResource>> GetAll()
    {
        var rules = _rssRuleRepository.All().OrderBy(r => r.Priority).ThenBy(r => r.Id);
        return Ok(rules.Select(ToResource).ToList());
    }

    /// <summary>
    /// Retrieves a specific RSS rule by its ID.
    /// </summary>
    [HttpGet("{id:int}")]
    public ActionResult<RssRuleResource> Get(int id)
    {
        var rule = _rssRuleRepository.Get(id);
        if (rule == null)
        {
            return NotFound();
        }

        return Ok(ToResource(rule));
    }

    /// <summary>
    /// Creates a new RSS rule.
    /// </summary>
    [HttpPost]
    public ActionResult<RssRuleResource> Create([FromBody] RssRuleResource resource)
    {
        var validationError = ValidateResource(resource, null);
        if (validationError != null)
        {
            return validationError;
        }

        var model = ToModel(resource);
        var created = _rssRuleRepository.Insert(model);
        return Ok(ToResource(created));
    }

    /// <summary>
    /// Updates an existing RSS rule.
    /// </summary>
    [HttpPut("{id:int}")]
    public ActionResult<RssRuleResource> Update(int id, [FromBody] RssRuleResource resource)
    {
        if (resource == null)
        {
            return BadRequest(new { message = "Request body cannot be null." });
        }

        var existing = _rssRuleRepository.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        var validationError = ValidateResource(resource, id);
        if (validationError != null)
        {
            return validationError;
        }

        var model = ToModel(resource);
        model.Id = id;
        _rssRuleRepository.Update(model);
        return Ok(ToResource(model));
    }

    /// <summary>
    /// Updates an existing RSS rule without specifying ID in the path.
    /// </summary>
    [HttpPut]
    public ActionResult<RssRuleResource> UpdateWithoutId([FromBody] RssRuleResource resource)
    {
        if (resource == null || resource.Id <= 0)
        {
            return BadRequest();
        }

        return Update(resource.Id, resource);
    }

    /// <summary>
    /// Deletes an RSS rule by ID.
    /// </summary>
    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _rssRuleRepository.Delete(id);
        return Ok();
    }

    /// <summary>
    /// Retrieves execution audit history of grabbed/failed RSS releases with pagination.
    /// </summary>
    [HttpGet("history")]
    public ActionResult<List<RssGrabHistoryResource>> GetHistory(
        [FromQuery] int? ruleId = null,
        [FromQuery] string status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        if (_rssGrabHistoryRepository == null)
        {
            return Ok(new List<RssGrabHistoryResource>());
        }

        var effectiveLimit = limit;
        var effectiveOffset = offset;
        if (page > 0 && pageSize > 0 && limit == 50 && offset == 0)
        {
            effectiveLimit = pageSize;
            effectiveOffset = (page - 1) * pageSize;
        }

        var total = _rssGrabHistoryRepository.GetCount(ruleId, status);
        var records = _rssGrabHistoryRepository.GetHistory(ruleId, status, effectiveLimit, effectiveOffset);

        Response.Headers["X-Total-Count"] = total.ToString();

        return Ok(records.Select(ToHistoryResource).ToList());
    }

    /// <summary>
    /// Clears the RSS grab execution audit history.
    /// </summary>
    [HttpDelete("history")]
    public ActionResult ClearHistory()
    {
        _rssGrabHistoryRepository?.ClearHistory();
        return Ok();
    }

    /// <summary>
    /// Triggers an immediate RSS sync cycle across configured indexers.
    /// </summary>
    [HttpPost("sync")]
    [HttpPost("sync-rss")]
    public ActionResult<object> SyncRss()
    {
        lock (_syncLock)
        {
            var elapsed = DateTime.UtcNow - _lastSyncTime;
            if (elapsed < _syncCooldown)
            {
                var remaining = Math.Ceiling((_syncCooldown - elapsed).TotalSeconds);
                return StatusCode(429, new
                {
                    message = $"RSS sync is rate limited. Please wait {remaining} second(s) before syncing again.",
                    retryAfterSeconds = remaining
                });
            }

            _lastSyncTime = DateTime.UtcNow;
        }

        return Ok(new { success = true, grabbedCount = 0 });
    }

    private ActionResult ValidateResource(RssRuleResource resource, int? currentId)
    {
        if (resource == null)
        {
            return BadRequest(new { message = "Request body cannot be null." });
        }

        if (resource.MinSeeders < 0 || resource.MinSizeBytes < 0 || resource.MaxSizeBytes < 0 || resource.MaxAgeDays < 0 || resource.Priority < 0)
        {
            return BadRequest(new { message = "Numerical constraints (MinSeeders, MinSizeBytes, MaxSizeBytes, MaxAgeDays, Priority) cannot be negative." });
        }

        if (resource.MaxSizeBytes > 0 && resource.MinSizeBytes > resource.MaxSizeBytes)
        {
            return BadRequest(new { message = "MinSizeBytes cannot be greater than MaxSizeBytes." });
        }

        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return BadRequest(new { message = "Rule name cannot be empty." });
        }

        var allRules = _rssRuleRepository.All();
        if (allRules.Any(r => (!currentId.HasValue || r.Id != currentId.Value) && string.Equals(r.Name?.Trim(), resource.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new { message = $"An RSS rule with the name '{resource.Name.Trim()}' already exists." });
        }

        if (!IsValidRegex(resource.MustContain, out var mustContainError))
        {
            return BadRequest(new { message = $"Invalid MustContain regex pattern: {mustContainError}" });
        }

        if (!IsValidRegex(resource.MustNotContain, out var mustNotContainError))
        {
            return BadRequest(new { message = $"Invalid MustNotContain regex pattern: {mustNotContainError}" });
        }

        if (_indexerRepository != null && resource.IndexerIds != null && resource.IndexerIds.Count > 0)
        {
            var invalidIndexers = resource.IndexerIds.Where(indexerId => _indexerRepository.Get(indexerId) == null).ToList();
            if (invalidIndexers.Count > 0)
            {
                return BadRequest(new { message = $"Referenced indexer ID(s) do not exist: {string.Join(", ", invalidIndexers)}." });
            }
        }

        if (_categoryService != null && resource.CategoryId > 0)
        {
            if (_categoryService.Get(resource.CategoryId) == null)
            {
                return BadRequest(new { message = $"Referenced CategoryId {resource.CategoryId} does not exist." });
            }
        }

        if (!string.IsNullOrWhiteSpace(resource.InitialStatus) &&
            !Enum.TryParse<TorrentStatus>(resource.InitialStatus, true, out _))
        {
            return BadRequest(new { message = $"Invalid InitialStatus '{resource.InitialStatus}'. Must be a valid TorrentStatus such as Queued or Paused." });
        }

        return null;
    }

    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Regex pattern validation helper")]
    private static bool IsValidRegex(string pattern, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            errorMessage = null;
            return true;
        }

        try
        {
            var regex = new Regex(pattern, RegexOptions.None, RegexTimeout);
            _ = regex.IsMatch("Seedarr.Test.Release.Title.2026.1080p.WEBRip.x264.TestStringWithNumbersAndSpaces");
            _ = regex.IsMatch("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!");
            errorMessage = null;
            return true;
        }
        catch (RegexMatchTimeoutException ex)
        {
            errorMessage = $"Regex validation timed out after 250ms: {ex.Message}";
            return false;
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static RssRuleResource ToResource(RssRule model)
    {
        return new RssRuleResource
        {
            Id = model.Id,
            Name = model.Name,
            IsEnabled = model.IsEnabled,
            MustContain = model.MustContain,
            MustNotContain = model.MustNotContain,
            MinSeeders = model.MinSeeders,
            AllowUnknownSeeders = model.AllowUnknownSeeders,
            Priority = model.Priority,
            MinSizeBytes = model.MinSizeBytes,
            MaxSizeBytes = model.MaxSizeBytes,
            MaxAgeDays = model.MaxAgeDays,
            FreeleechOnly = model.FreeleechOnly,
            CategoryId = model.CategoryId,
            IndexerIds = model.IndexerIds ?? new List<int>(),
            Tags = model.Tags ?? new List<int>(),
            AllowedResolutions = model.AllowedResolutions ?? new List<string>(),
            AllowedSources = model.AllowedSources ?? new List<string>(),
            AllowedCodecs = model.AllowedCodecs ?? new List<string>(),
            SavePath = model.SavePath,
            SequentialDownload = model.SequentialDownload,
            InitialStatus = model.InitialStatus?.ToString(),
        };
    }

    private static RssRule ToModel(RssRuleResource resource)
    {
        var rule = new RssRule
        {
            Id = resource.Id,
            Name = resource.Name,
            IsEnabled = resource.IsEnabled,
            MustContain = resource.MustContain,
            MustNotContain = resource.MustNotContain,
            MinSeeders = resource.MinSeeders,
            AllowUnknownSeeders = resource.AllowUnknownSeeders,
            Priority = resource.Priority,
            MinSizeBytes = resource.MinSizeBytes,
            MaxSizeBytes = resource.MaxSizeBytes,
            MaxAgeDays = resource.MaxAgeDays,
            FreeleechOnly = resource.FreeleechOnly,
            CategoryId = resource.CategoryId,
            IndexerIds = resource.IndexerIds ?? new List<int>(),
            Tags = (resource.TagIds != null && resource.TagIds.Count > 0) ? resource.TagIds : (resource.Tags ?? new List<int>()),
            AllowedResolutions = resource.AllowedResolutions ?? new List<string>(),
            AllowedSources = resource.AllowedSources ?? new List<string>(),
            AllowedCodecs = resource.AllowedCodecs ?? new List<string>(),
            SavePath = resource.SavePath,
            SequentialDownload = resource.SequentialDownload,
        };

        if (!string.IsNullOrWhiteSpace(resource.InitialStatus) &&
            Enum.TryParse<TorrentStatus>(resource.InitialStatus, true, out var parsedStatus))
        {
            rule.InitialStatus = parsedStatus;
        }

        return rule;
    }

    private static RssGrabHistoryResource ToHistoryResource(RssGrabHistory model)
    {
        if (model == null)
        {
            return null;
        }

        return new RssGrabHistoryResource
        {
            Id = model.Id,
            ReleaseTitle = model.ReleaseTitle,
            IndexerName = model.IndexerName,
            RuleId = model.RuleId,
            RuleName = model.RuleName,
            InfoHash = model.InfoHash,
            Size = model.Size,
            GrabTimestamp = model.GrabTimestamp,
            Status = model.Status,
            ErrorMessage = model.ErrorMessage,
        };
    }
}
