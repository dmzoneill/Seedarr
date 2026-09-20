using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Blocklist;

public class PeerBlocklistSyncService : IPeerBlocklistSyncService
{
    private static readonly HttpClient DefaultClient = new();
    private static readonly TimeSpan DefaultBaseBackoff = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly IConfigService _configService;
    private readonly Func<DateTime> _nowProvider;
    private readonly Logger _logger;
    private readonly IBlocklistArchiveStreamProvider _streamProvider;
    private readonly object _syncLock = new();

    private readonly BlocklistSyncMetadata _metadata = new();
    private List<string> _rules = new();
    private Ipv6IntervalTree _tree;

    public PeerBlocklistSyncService(
        HttpClient httpClient = null,
        IConfigService configService = null,
        Func<DateTime> nowProvider = null,
        Logger logger = null,
        IBlocklistArchiveStreamProvider streamProvider = null)
    {
        _httpClient = httpClient ?? DefaultClient;
        _configService = configService;
        _nowProvider = nowProvider ?? (() => DateTime.UtcNow);
        _logger = logger ?? LogManager.GetCurrentClassLogger();
        _streamProvider = streamProvider ?? new BlocklistArchiveStreamProvider(logger: _logger);

        if (_configService != null)
        {
            var etag = _configService.BlocklistETag;
            if (!string.IsNullOrWhiteSpace(etag))
            {
                _metadata.BlocklistETag = etag;
            }

            var lastMod = _configService.BlocklistLastModified;
            if (!string.IsNullOrWhiteSpace(lastMod) && DateTimeOffset.TryParse(lastMod, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            {
                _metadata.BlocklistLastModified = dto;
            }
        }
    }

    public BlocklistSyncMetadata Metadata
    {
        get
        {
            lock (_syncLock)
            {
                return new BlocklistSyncMetadata
                {
                    BlocklistETag = _metadata.BlocklistETag,
                    BlocklistLastModified = _metadata.BlocklistLastModified,
                    LastCheckedUtc = _metadata.LastCheckedUtc,
                    LastSyncStatus = _metadata.LastSyncStatus,
                    LastSyncHttpStatus = _metadata.LastSyncHttpStatus,
                    NextAllowedSyncUtc = _metadata.NextAllowedSyncUtc,
                    ConsecutiveFailures = _metadata.ConsecutiveFailures,
                    RuleCount = _rules.Count,
                    LastFailureMessage = _metadata.LastFailureMessage
                };
            }
        }
    }

    public string LastSyncStatus
    {
        get
        {
            lock (_syncLock)
            {
                return _metadata.LastSyncStatus;
            }
        }
    }

    public HttpStatusCode? LastSyncHttpStatus
    {
        get
        {
            lock (_syncLock)
            {
                return _metadata.LastSyncHttpStatus;
            }
        }
    }

    public DateTime? NextAllowedSyncUtc
    {
        get
        {
            lock (_syncLock)
            {
                return _metadata.NextAllowedSyncUtc;
            }
        }
    }

    public IReadOnlyList<string> ActiveRules
    {
        get
        {
            lock (_syncLock)
            {
                return _rules.ToArray();
            }
        }
    }

    public int RuleCount
    {
        get
        {
            lock (_syncLock)
            {
                return _rules.Count;
            }
        }
    }

    public DateTime? LastCheckedUtc
    {
        get
        {
            lock (_syncLock)
            {
                return _metadata.LastCheckedUtc;
            }
        }
    }

    public bool IsSyncAllowed(DateTime? now = null)
    {
        var current = now ?? _nowProvider();
        lock (_syncLock)
        {
            if (_metadata.NextAllowedSyncUtc.HasValue && current < _metadata.NextAllowedSyncUtc.Value)
            {
                return false;
            }

            return true;
        }
    }

    public void ResetBackoff()
    {
        lock (_syncLock)
        {
            _metadata.NextAllowedSyncUtc = null;
            _metadata.ConsecutiveFailures = 0;
        }
    }

    public Ipv6IntervalTree IntervalTree => Volatile.Read(ref _tree);

    public void SetActiveRules(IEnumerable<string> rules)
    {
        var ruleList = rules?.ToList() ?? new List<string>();
        var newTree = ruleList.Count > 0 ? Ipv6IntervalTree.Parse(ruleList) : null;

        lock (_syncLock)
        {
            _rules = ruleList;
            _metadata.RuleCount = _rules.Count;
            Interlocked.Exchange(ref _tree, newTree);
        }
    }

    public bool IsBlocked(IPAddress address)
    {
        if (address == null)
        {
            return false;
        }

        var tree = Volatile.Read(ref _tree);
        return tree?.Contains(address) ?? false;
    }

    public bool IsBlocked(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        if (!IPAddress.TryParse(ip, out var address))
        {
            return false;
        }

        return IsBlocked(address);
    }

    public Task<BlocklistSyncResult> SyncBlocklistAsync(string url = null, bool force = false, CancellationToken cancellationToken = default)
    {
        return SyncAsync(url, force, cancellationToken);
    }

    public async Task<BlocklistSyncResult> SyncAsync(string url = null, bool force = false, CancellationToken cancellationToken = default)
    {
        var effectiveUrl = url;
        if (string.IsNullOrWhiteSpace(effectiveUrl) && _configService != null)
        {
            effectiveUrl = _configService.BlocklistUrl;
        }

        if (string.IsNullOrWhiteSpace(effectiveUrl))
        {
            _logger.Warn("Blocklist synchronization skipped: No blocklist URL configured.");
            return new BlocklistSyncResult
            {
                Success = false,
                Status = "No URL Configured",
                Message = "No blocklist URL configured",
                RuleCount = RuleCount
            };
        }

        var now = _nowProvider();

        lock (_syncLock)
        {
            if (!force && _metadata.NextAllowedSyncUtc.HasValue && now < _metadata.NextAllowedSyncUtc.Value)
            {
                _logger.Warn("Blocklist sync deferred: upstream rate-limit backoff active until {0}.", _metadata.NextAllowedSyncUtc.Value);
                return new BlocklistSyncResult
                {
                    Success = false,
                    IsRateLimited = true,
                    Status = _metadata.LastSyncStatus,
                    HttpStatusCode = _metadata.LastSyncHttpStatus,
                    NextAllowedSyncUtc = _metadata.NextAllowedSyncUtc,
                    RuleCount = _rules.Count,
                    Message = $"Sync deferred due to upstream rate limit backoff until {_metadata.NextAllowedSyncUtc.Value:O}"
                };
            }
        }

        var request = new HttpRequestMessage(HttpMethod.Get, effectiveUrl);

        lock (_syncLock)
        {
            if (!string.IsNullOrWhiteSpace(_metadata.BlocklistETag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", _metadata.BlocklistETag);
            }

            if (_metadata.BlocklistLastModified.HasValue)
            {
                request.Headers.IfModifiedSince = _metadata.BlocklistLastModified;
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            now = _nowProvider();
            lock (_syncLock)
            {
                _metadata.ConsecutiveFailures++;
                _metadata.LastCheckedUtc = now;
                _metadata.LastSyncStatus = $"Failed: {ex.Message}";
                _metadata.LastFailureMessage = ex.Message;
            }

            _logger.Error(ex, "Failed to download blocklist from {0}", effectiveUrl);
            return new BlocklistSyncResult
            {
                Success = false,
                Status = $"Failed: {ex.Message}",
                Message = ex.Message,
                RuleCount = RuleCount
            };
        }

        now = _nowProvider();

        lock (_syncLock)
        {
            _metadata.LastCheckedUtc = now;
            _metadata.LastSyncHttpStatus = response.StatusCode;

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _logger.Info("Blocklist at {0} is unchanged (304 Not Modified). Skipping re-parse.", effectiveUrl);
                _metadata.LastSyncStatus = "Sync Skipped (Not Modified)";
                _metadata.ConsecutiveFailures = 0;

                return new BlocklistSyncResult
                {
                    Success = true,
                    IsNotModified = true,
                    Status = _metadata.LastSyncStatus,
                    HttpStatusCode = HttpStatusCode.NotModified,
                    RuleCount = _rules.Count,
                    Message = "Blocklist is unchanged (304 Not Modified)"
                };
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                _metadata.ConsecutiveFailures++;
                var retrySpan = ParseRetryAfter(response, now);
                if (!retrySpan.HasValue)
                {
                    var retryCount = Math.Max(0, _metadata.ConsecutiveFailures - 1);
                    retrySpan = CalculateExponentialBackoff(retryCount);
                }

                _metadata.NextAllowedSyncUtc = now.Add(retrySpan.Value);
                _metadata.LastSyncStatus = $"Rate Limited by Provider (Retry after {_metadata.NextAllowedSyncUtc.Value:HH:mm})";
                _metadata.LastFailureMessage = $"HTTP {(int)response.StatusCode} {response.StatusCode}";

                _logger.Warn(
                    "Upstream blocklist provider rate-limited ({0}). Backing off until {1}.",
                    response.StatusCode,
                    _metadata.NextAllowedSyncUtc.Value);

                return new BlocklistSyncResult
                {
                    Success = false,
                    IsRateLimited = true,
                    Status = _metadata.LastSyncStatus,
                    HttpStatusCode = response.StatusCode,
                    NextAllowedSyncUtc = _metadata.NextAllowedSyncUtc,
                    RuleCount = _rules.Count,
                    Message = $"Rate limited by upstream provider. Retry after {retrySpan.Value}."
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                _metadata.ConsecutiveFailures++;
                _metadata.LastSyncStatus = $"Failed: {(int)response.StatusCode} {response.StatusCode}";
                _metadata.LastFailureMessage = _metadata.LastSyncStatus;

                _logger.Warn("Blocklist download failed from {0} with status code {1}.", effectiveUrl, response.StatusCode);

                return new BlocklistSyncResult
                {
                    Success = false,
                    Status = _metadata.LastSyncStatus,
                    HttpStatusCode = response.StatusCode,
                    RuleCount = _rules.Count,
                    Message = $"Download failed with HTTP status {(int)response.StatusCode}"
                };
            }
        }

        string newETag = null;
        if (response.Headers.ETag != null)
        {
            newETag = response.Headers.ETag.ToString();
        }
        else if (response.Headers.TryGetValues("ETag", out var etagValues))
        {
            newETag = etagValues.FirstOrDefault();
        }

        DateTimeOffset? newLastModified = null;
        if (response.Content?.Headers.LastModified != null)
        {
            newLastModified = response.Content.Headers.LastModified;
        }
        else if (response.Headers.TryGetValues("Last-Modified", out var lastModValues))
        {
            var rawVal = lastModValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(rawVal) && DateTimeOffset.TryParse(rawVal, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDto))
            {
                newLastModified = parsedDto;
            }
        }

        List<string> parsedRules;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            parsedRules = await _streamProvider.ExtractRulesAsync(
                stream,
                effectiveUrl,
                response.Content.Headers.ContentType?.MediaType,
                response.Content.Headers.ContentEncoding?.FirstOrDefault(),
                leaveOpen: false,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            now = _nowProvider();
            lock (_syncLock)
            {
                _metadata.ConsecutiveFailures++;
                _metadata.LastCheckedUtc = now;
                _metadata.LastSyncStatus = $"Failed: {ex.Message}";
                _metadata.LastFailureMessage = ex.Message;
            }

            _logger.Warn(ex, "Failed to decompress or parse blocklist from {0}: {1}", effectiveUrl, ex.Message);
            return new BlocklistSyncResult
            {
                Success = false,
                Status = $"Failed: {ex.Message}",
                Message = ex.Message,
                RuleCount = RuleCount
            };
        }

        var newTree = parsedRules.Count > 0 ? Ipv6IntervalTree.Parse(parsedRules) : null;

        lock (_syncLock)
        {
            _rules = parsedRules;
            _metadata.RuleCount = _rules.Count;
            Interlocked.Exchange(ref _tree, newTree);

            if (!string.IsNullOrWhiteSpace(newETag))
            {
                _metadata.BlocklistETag = newETag;
                if (_configService != null)
                {
                    _configService.BlocklistETag = newETag;
                }
            }

            if (newLastModified.HasValue)
            {
                _metadata.BlocklistLastModified = newLastModified;
                if (_configService != null)
                {
                    _configService.BlocklistLastModified = newLastModified.Value.ToString("r", CultureInfo.InvariantCulture);
                }
            }

            _metadata.ConsecutiveFailures = 0;
            _metadata.NextAllowedSyncUtc = null;
            _metadata.LastSyncStatus = "Success";
            _metadata.LastFailureMessage = null;
        }

        _logger.Info("Blocklist synchronized successfully from {0}. Parsed {1} rules.", effectiveUrl, parsedRules.Count);

        return new BlocklistSyncResult
        {
            Success = true,
            Status = "Success",
            HttpStatusCode = HttpStatusCode.OK,
            RuleCount = parsedRules.Count,
            Message = "Blocklist updated successfully"
        };
    }

    public static TimeSpan? ParseRetryAfter(HttpResponseMessage response, DateTime now)
    {
        if (response?.Headers == null)
        {
            return null;
        }

        if (response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                return response.Headers.RetryAfter.Delta.Value;
            }

            if (response.Headers.RetryAfter.Date.HasValue)
            {
                var diff = response.Headers.RetryAfter.Date.Value.UtcDateTime - now;
                return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
            }
        }

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            foreach (var val in values)
            {
                if (string.IsNullOrWhiteSpace(val))
                {
                    continue;
                }

                var trimmed = val.Trim();

                if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var directSeconds) && directSeconds >= 0)
                {
                    return TimeSpan.FromSeconds(directSeconds);
                }

                if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var directDate))
                {
                    var diff = directDate.UtcDateTime - now;
                    return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
                }

                var match = Regex.Match(trimmed, @"(?:retry[-_ ]?after[:= ]*|retry in )(?<val>[^\r\n]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var subVal = match.Groups["val"].Value.Trim();
                    var digitsMatch = Regex.Match(subVal, @"^(\d+)");
                    if (digitsMatch.Success && int.TryParse(digitsMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sec) && sec >= 0)
                    {
                        return TimeSpan.FromSeconds(sec);
                    }

                    if (DateTimeOffset.TryParse(subVal, CultureInfo.InvariantCulture, DateTimeStyles.None, out var headerDate))
                    {
                        var diff = headerDate.UtcDateTime - now;
                        return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
                    }
                }
            }
        }

        return null;
    }

    public static TimeSpan CalculateExponentialBackoff(int retryCount, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null)
    {
        var baseSpan = baseDelay ?? DefaultBaseBackoff;
        var maxSpan = maxDelay ?? MaxBackoff;

        var count = Math.Max(0, retryCount);
        var exponent = Math.Min(count, 12);
        var multiplier = Math.Pow(2, exponent);
        var delayMinutes = baseSpan.TotalMinutes * multiplier;

        if (delayMinutes >= maxSpan.TotalMinutes)
        {
            return maxSpan;
        }

        return TimeSpan.FromMinutes(delayMinutes);
    }

    private static List<string> ParseRules(string content)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return list;
        }

        using var reader = new StringReader(content);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            list.Add(trimmed);
        }

        return list;
    }
}
