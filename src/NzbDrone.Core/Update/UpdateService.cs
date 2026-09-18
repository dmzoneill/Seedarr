using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Update;

public class UpdateInfo
{
    public string CurrentVersion { get; set; }
    public string LatestVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public string ReleaseUrl { get; set; }
    public string ReleaseNotes { get; set; }
    public List<ReleaseInfo> Releases { get; set; } = new();
    public bool IsContainerized { get; set; }
}

public class ReleaseInfo
{
    public string Version { get; set; }
    public DateTime PublishedAt { get; set; }
    public string Body { get; set; }
    public string Url { get; set; }
}

public interface IUpdateService
{
    UpdateInfo CheckForUpdate(bool force = false);
    Task<UpdateInfo> CheckForUpdateAsync(bool force = false, CancellationToken cancellationToken = default);
    Version GetLatestVersion();
    UpdateInfo CachedUpdateInfo { get; }
    bool IsCacheExpired { get; }
}

public class UpdateService : IUpdateService
{
    private const string GitHubReleasesUrl = "https://api.github.com/repos/dmzoneill/Seedarr/releases?per_page=100";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _client;
    private readonly ISystemClock _clock;
    private readonly Logger _logger;
    private readonly object _cacheLock = new();
    private UpdateInfo _cachedResult;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private Task<UpdateInfo> _ongoingAsyncFetch;

    internal static Func<List<ReleaseInfo>> ChangelogProvider { get; set; }

    public UpdateService(HttpClient httpClient = null, ISystemClock clock = null)
    {
        _client = httpClient ?? new HttpClient { Timeout = DefaultTimeout };
        _clock = clock ?? new SystemClock();
        _logger = LogManager.GetCurrentClassLogger();
    }

    public UpdateInfo CachedUpdateInfo
    {
        get
        {
            lock (_cacheLock)
            {
                return _cachedResult;
            }
        }
    }

    public bool IsCacheExpired
    {
        get
        {
            lock (_cacheLock)
            {
                return _cachedResult == null || _clock.UtcNow >= _cacheExpiry;
            }
        }
    }

    public UpdateInfo CheckForUpdate(bool force = false)
    {
        lock (_cacheLock)
        {
            if (!force && _cachedResult != null && _clock.UtcNow < _cacheExpiry)
            {
                return _cachedResult;
            }
        }

        var fetchResult = FetchUpdateInfo();
        return UpdateCacheWithFetchResult(fetchResult);
    }

    public Task<UpdateInfo> CheckForUpdateAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            if (!force && _cachedResult != null && _clock.UtcNow < _cacheExpiry)
            {
                return Task.FromResult(_cachedResult);
            }

            if (_ongoingAsyncFetch != null && !_ongoingAsyncFetch.IsCompleted)
            {
                return _ongoingAsyncFetch;
            }

            _ongoingAsyncFetch = ExecuteAsyncFetch(cancellationToken);
            return _ongoingAsyncFetch;
        }
    }

    private async Task<UpdateInfo> ExecuteAsyncFetch(CancellationToken cancellationToken)
    {
        try
        {
            var fetchResult = await FetchUpdateInfoAsync(cancellationToken).ConfigureAwait(false);
            return UpdateCacheWithFetchResult(fetchResult);
        }
        finally
        {
            lock (_cacheLock)
            {
                _ongoingAsyncFetch = null;
            }
        }
    }

    private UpdateInfo UpdateCacheWithFetchResult(FetchResult fetchResult)
    {
        lock (_cacheLock)
        {
            if (fetchResult.IsSuccess && fetchResult.Info.Releases.Count > 0)
            {
                _cachedResult = fetchResult.Info;
                _cacheExpiry = _clock.UtcNow.Add(CacheDuration);
                return _cachedResult;
            }

            if (fetchResult.IsRateLimited || !fetchResult.IsSuccess)
            {
                _cacheExpiry = fetchResult.RateLimitExpiry ?? _clock.UtcNow.AddMinutes(30);

                if (_cachedResult != null && _cachedResult.Releases.Count > 0)
                {
                    return _cachedResult;
                }

                _cachedResult = fetchResult.Info;
                return _cachedResult;
            }

            _cachedResult = fetchResult.Info;
            _cacheExpiry = _clock.UtcNow.Add(CacheDuration);
            return _cachedResult;
        }
    }

    public Version GetLatestVersion()
    {
        var info = CheckForUpdate();
        if (info == null || string.IsNullOrWhiteSpace(info.LatestVersion))
        {
            return null;
        }

        if (SemVersion.TryParse(info.LatestVersion, out var semVer))
        {
            return semVer.Core;
        }

        var clean = info.LatestVersion.Trim().TrimStart('v', 'V');
        var match = Regex.Match(clean, @"^([0-9]+(?:\.[0-9]+)*)");
        if (match.Success)
        {
            var verStr = match.Groups[1].Value;
            if (!verStr.Contains('.'))
            {
                verStr += ".0";
            }

            if (Version.TryParse(verStr, out var v))
            {
                return v;
            }
        }

        return Version.TryParse(info.LatestVersion, out var version) ? version : null;
    }

    private DateTime CalculateRateLimitExpiry(HttpResponseMessage response)
    {
        if (response?.Headers == null)
        {
            return _clock.UtcNow.AddMinutes(30);
        }

        if (response.Headers.TryGetValues("x-ratelimit-reset", out var resetValues))
        {
            var resetStr = resetValues.FirstOrDefault();
            if (long.TryParse(resetStr, out var resetEpoch))
            {
                var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetEpoch).UtcDateTime;
                return resetTime > _clock.UtcNow ? resetTime : _clock.UtcNow.AddMinutes(30);
            }
        }

        if (response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                return _clock.UtcNow.Add(response.Headers.RetryAfter.Delta.Value);
            }

            if (response.Headers.RetryAfter.Date.HasValue)
            {
                var retryDate = response.Headers.RetryAfter.Date.Value.UtcDateTime;
                return retryDate > _clock.UtcNow ? retryDate : _clock.UtcNow.AddMinutes(30);
            }
        }

        if (response.Headers.TryGetValues("retry-after", out var retryAfterValues))
        {
            var retryStr = retryAfterValues.FirstOrDefault();
            if (int.TryParse(retryStr, out var retrySeconds) && retrySeconds > 0)
            {
                return _clock.UtcNow.AddSeconds(retrySeconds);
            }
        }

        return _clock.UtcNow.AddMinutes(30);
    }

    private class FetchResult
    {
        public UpdateInfo Info { get; set; }
        public bool IsSuccess { get; set; }
        public bool IsRateLimited { get; set; }
        public DateTime? RateLimitExpiry { get; set; }
    }

    private FetchResult FetchUpdateInfo()
    {
        var currentVersion = BuildInfo.Version?.ToString() ?? "1.0.0";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesUrl);
            var versionStr = BuildInfo.Version?.ToString() ?? "1.0.0";
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Seedarr", versionStr));
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = _client.Send(request);

            if (!response.IsSuccessStatusCode)
            {
                var isRateLimited = response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                                    response.StatusCode == (System.Net.HttpStatusCode)429;

                if (!isRateLimited && response.Headers.TryGetValues("x-ratelimit-remaining", out var remValues))
                {
                    if (int.TryParse(remValues.FirstOrDefault(), out var rem) && rem <= 0)
                    {
                        isRateLimited = true;
                    }
                }

                if (isRateLimited)
                {
                    _logger.Debug("GitHub releases API rate limit reached ({0})", response.StatusCode);
                }
                else
                {
                    _logger.Warn("GitHub releases API returned {0}", response.StatusCode);
                }

                var expiry = CalculateRateLimitExpiry(response);
                var fallbackInfo = LoadFromChangelogOrFallback(currentVersion);

                return new FetchResult
                {
                    Info = fallbackInfo,
                    IsSuccess = false,
                    IsRateLimited = isRateLimited,
                    RateLimitExpiry = expiry
                };
            }

            using var stream = response.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);
            var info = ParseReleasesDocument(doc, currentVersion);

            return new FetchResult
            {
                Info = info,
                IsSuccess = true,
                IsRateLimited = false,
                RateLimitExpiry = null
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Failed to check for updates");
            return new FetchResult
            {
                Info = LoadFromChangelogOrFallback(currentVersion),
                IsSuccess = false,
                IsRateLimited = false,
                RateLimitExpiry = _clock.UtcNow.AddMinutes(30)
            };
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to parse GitHub releases response");
            return new FetchResult
            {
                Info = LoadFromChangelogOrFallback(currentVersion),
                IsSuccess = false,
                IsRateLimited = false,
                RateLimitExpiry = _clock.UtcNow.AddMinutes(30)
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error checking for updates");
            return new FetchResult
            {
                Info = LoadFromChangelogOrFallback(currentVersion),
                IsSuccess = false,
                IsRateLimited = false,
                RateLimitExpiry = _clock.UtcNow.AddMinutes(30)
            };
        }
    }

    private async Task<FetchResult> FetchUpdateInfoAsync(CancellationToken cancellationToken)
    {
        var currentVersion = BuildInfo.Version?.ToString() ?? "1.0.0";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(DefaultTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesUrl);
            var versionStr = BuildInfo.Version?.ToString() ?? "1.0.0";
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Seedarr", versionStr));
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _client.SendAsync(request, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var isRateLimited = response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                                    response.StatusCode == (System.Net.HttpStatusCode)429;

                if (!isRateLimited && response.Headers.TryGetValues("x-ratelimit-remaining", out var remValues))
                {
                    if (int.TryParse(remValues.FirstOrDefault(), out var rem) && rem <= 0)
                    {
                        isRateLimited = true;
                    }
                }

                if (isRateLimited)
                {
                    _logger.Debug("GitHub releases API rate limit reached ({0})", response.StatusCode);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    _logger.Warn("GitHub releases API returned {0}: {1}", response.StatusCode, errorBody);
                }

                var expiry = CalculateRateLimitExpiry(response);
                var fallbackInfo = LoadFromChangelogOrFallback(currentVersion);

                return new FetchResult
                {
                    Info = fallbackInfo,
                    IsSuccess = false,
                    IsRateLimited = isRateLimited,
                    RateLimitExpiry = expiry
                };
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var info = ParseReleasesDocument(doc, currentVersion);

            return new FetchResult
            {
                Info = info,
                IsSuccess = true,
                IsRateLimited = false,
                RateLimitExpiry = null
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Failed to check for updates");
            return new FetchResult
            {
                Info = LoadFromChangelogOrFallback(currentVersion),
                IsSuccess = false,
                IsRateLimited = false,
                RateLimitExpiry = _clock.UtcNow.AddMinutes(30)
            };
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to parse GitHub releases response");
            return new FetchResult
            {
                Info = LoadFromChangelogOrFallback(currentVersion),
                IsSuccess = false,
                IsRateLimited = false,
                RateLimitExpiry = _clock.UtcNow.AddMinutes(30)
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error checking for updates");
            return new FetchResult
            {
                Info = LoadFromChangelogOrFallback(currentVersion),
                IsSuccess = false,
                IsRateLimited = false,
                RateLimitExpiry = _clock.UtcNow.AddMinutes(30)
            };
        }
    }

    private static UpdateInfo LoadFromChangelogOrFallback(string currentVersion)
    {
        var changelogReleases = LoadFromChangelog();
        if (changelogReleases != null && changelogReleases.Count > 0)
        {
            SemVersion? latestSemVer = null;
            string latestVersionStr = null;
            foreach (var r in changelogReleases)
            {
                if (SemVersion.TryParse(r.Version, out var v) && (latestSemVer == null || v > latestSemVer.Value))
                {
                    latestSemVer = v;
                    latestVersionStr = r.Version;
                }
            }

            return BuildResult(currentVersion, latestVersionStr, changelogReleases);
        }

        return BuildResult(currentVersion, null, new List<ReleaseInfo>());
    }

    public static List<ReleaseInfo> LoadFromChangelog()
    {
        if (ChangelogProvider != null)
        {
            return ChangelogProvider();
        }

        var searchPaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG.md"),
            "/app/CHANGELOG.md",
            Path.Combine(Directory.GetCurrentDirectory(), "CHANGELOG.md"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "CHANGELOG.md"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "CHANGELOG.md"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "CHANGELOG.md"),
        };

        foreach (var path in searchPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path);
                    var releases = ParseChangelogMarkdown(content);
                    if (releases.Count > 0)
                    {
                        return releases;
                    }
                }
            }
            catch
            {
                // Continue searching
            }
        }

        return new List<ReleaseInfo>();
    }

    public static List<ReleaseInfo> ParseChangelogMarkdown(string content)
    {
        var releaseList = new List<ReleaseInfo>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return releaseList;
        }

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        ReleaseInfo currentRelease = null;
        var currentBody = new List<string>();

        var headerRegex = new Regex(@"^##\s+\[?v?([0-9]+(?:\.[0-9]+)+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?)\]?\s*(?:\(([^)]+)\))?(?:\s*-\s*([0-9]{4}-[0-9]{2}-[0-9]{2}))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        foreach (var line in lines)
        {
            var match = headerRegex.Match(line.Trim());
            if (match.Success)
            {
                if (currentRelease != null)
                {
                    currentRelease.Body = string.Join("\n", currentBody).Trim();
                    releaseList.Add(currentRelease);
                    currentBody.Clear();
                }

                var ver = match.Groups[1].Value.Trim();
                string url = null;
                var pubDate = DateTime.UtcNow;

                if (match.Groups[2].Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
                {
                    var val = match.Groups[2].Value.Trim();
                    if (Uri.TryCreate(val, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        url = val;
                    }
                    else if (DateTime.TryParse(val, out var parsedDate))
                    {
                        pubDate = parsedDate;
                    }
                }

                if (match.Groups[3].Success && DateTime.TryParse(match.Groups[3].Value, out var dt))
                {
                    pubDate = dt;
                }

                if (string.IsNullOrWhiteSpace(url))
                {
                    url = $"https://github.com/dmzoneill/Seedarr/releases/tag/v{ver}";
                }

                currentRelease = new ReleaseInfo
                {
                    Version = ver,
                    Url = url,
                    PublishedAt = pubDate,
                };
            }
            else if (currentRelease != null)
            {
                currentBody.Add(line);
            }
        }

        if (currentRelease != null)
        {
            currentRelease.Body = string.Join("\n", currentBody).Trim();
            releaseList.Add(currentRelease);
        }

        return releaseList;
    }

    private static UpdateInfo ParseReleasesDocument(JsonDocument doc, string currentVersion)
    {
        var releases = doc.RootElement;
        if (releases.ValueKind != JsonValueKind.Array)
        {
            return BuildResult(currentVersion, null, new List<ReleaseInfo>());
        }

        var releaseList = new List<ReleaseInfo>();
        SemVersion? latestSemVer = null;
        string latestVersionString = null;

        foreach (var release in releases.EnumerateArray())
        {
            var tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            var versionString = tagName.TrimStart('v', 'V');

            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                continue;
            }

            if (!SemVersion.TryParse(versionString, out var parsedVer))
            {
                var clean = versionString.Trim();
                var plusIdx = clean.IndexOf('+');
                if (plusIdx >= 0)
                {
                    clean = clean[..plusIdx];
                }

                var dashIdx = clean.IndexOf('-');
                var pre = string.Empty;
                if (dashIdx >= 0)
                {
                    pre = clean[(dashIdx + 1)..];
                    clean = clean[..dashIdx];
                }
                else
                {
                    var preMatch = Regex.Match(clean, @"^([0-9]+(?:\.[0-9]+)*)[-_.]?([a-zA-Z]+.*)$");
                    if (preMatch.Success)
                    {
                        clean = preMatch.Groups[1].Value;
                        pre = preMatch.Groups[2].Value;
                    }
                }

                clean = Regex.Replace(clean, @"[-_.](beta|rc|alpha|preview).*$", "", RegexOptions.IgnoreCase);

                if (!clean.Contains('.'))
                {
                    clean += ".0";
                }

                if (!Version.TryParse(clean, out var baseVer))
                {
                    continue;
                }

                parsedVer = new SemVersion(baseVer, pre, originalString: versionString);
            }

            var publishedAt = release.TryGetProperty("published_at", out var pub) && (pub.TryGetDateTime(out var dt) || (pub.ValueKind == JsonValueKind.String && DateTime.TryParse(pub.GetString(), out dt)))
                ? dt
                : (release.TryGetProperty("created_at", out var createProp) && (createProp.TryGetDateTime(out var cdt) || (createProp.ValueKind == JsonValueKind.String && DateTime.TryParse(createProp.GetString(), out cdt))) ? cdt : DateTime.UtcNow);

            var body = release.TryGetProperty("body", out var notes) ? notes.GetString() : string.Empty;
            var htmlUrl = release.TryGetProperty("html_url", out var url) ? url.GetString() : null;

            releaseList.Add(new ReleaseInfo
            {
                Version = versionString,
                PublishedAt = publishedAt,
                Body = body,
                Url = htmlUrl,
            });

            if (latestSemVer == null || parsedVer > latestSemVer.Value)
            {
                latestSemVer = parsedVer;
                latestVersionString = versionString;
            }
        }

        return BuildResult(currentVersion, latestVersionString, releaseList);
    }

    private static UpdateInfo BuildResult(string currentVersion, string latestVersion, List<ReleaseInfo> releases)
    {
        var updateAvailable = false;
        if (!string.IsNullOrWhiteSpace(latestVersion) && !string.IsNullOrWhiteSpace(currentVersion))
        {
            if (SemVersion.TryParse(latestVersion, out var latestSem) && SemVersion.TryParse(currentVersion, out var currentSem))
            {
                updateAvailable = latestSem > currentSem;
            }
        }

        return new UpdateInfo
        {
            CurrentVersion = currentVersion ?? "1.0.0",
            LatestVersion = latestVersion,
            UpdateAvailable = updateAvailable,
            Releases = releases,
            IsContainerized = IsRunningInContainer(),
        };
    }

    public static bool IsRunningInContainer()
    {
        try
        {
            if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (File.Exists("/.dockerenv"))
            {
                return true;
            }
        }
        catch
        {
            // Ignore file access or environment exceptions
        }

        return false;
    }
}
