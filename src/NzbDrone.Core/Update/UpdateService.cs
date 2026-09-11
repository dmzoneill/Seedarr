using System;
using System.Collections.Generic;
using System.IO;
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

    public UpdateService(HttpClient httpClient = null, ISystemClock clock = null)
    {
        _client = httpClient ?? new HttpClient { Timeout = DefaultTimeout };
        _clock = clock ?? new SystemClock();
        _logger = LogManager.GetCurrentClassLogger();
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

        var result = FetchUpdateInfo();

        lock (_cacheLock)
        {
            if (result.Releases.Count > 0)
            {
                _cachedResult = result;
                _cacheExpiry = _clock.UtcNow.Add(CacheDuration);
            }
            else if (_cachedResult != null && _cachedResult.Releases.Count > 0)
            {
                return _cachedResult;
            }
            else
            {
                _cachedResult = result;
                _cacheExpiry = _clock.UtcNow.AddSeconds(5);
            }
        }

        return result;
    }

    public async Task<UpdateInfo> CheckForUpdateAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        lock (_cacheLock)
        {
            if (!force && _cachedResult != null && _cachedResult.Releases.Count > 0 && _clock.UtcNow < _cacheExpiry)
            {
                return _cachedResult;
            }
        }

        var result = await FetchUpdateInfoAsync(cancellationToken).ConfigureAwait(false);

        lock (_cacheLock)
        {
            if (result.Releases.Count > 0)
            {
                _cachedResult = result;
                _cacheExpiry = _clock.UtcNow.Add(CacheDuration);
            }
            else if (_cachedResult != null && _cachedResult.Releases.Count > 0)
            {
                return _cachedResult;
            }
            else
            {
                _cachedResult = result;
                _cacheExpiry = _clock.UtcNow.AddSeconds(5);
            }
        }

        return result;
    }

    public Version GetLatestVersion()
    {
        var info = CheckForUpdate();
        return Version.TryParse(info.LatestVersion, out var version) ? version : null;
    }

    private UpdateInfo FetchUpdateInfo()
    {
        var currentVersion = BuildInfo.Version;

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
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == (System.Net.HttpStatusCode)429)
                {
                    _logger.Debug("GitHub releases API rate limit reached ({0})", response.StatusCode);
                }
                else
                {
                    _logger.Warn("GitHub releases API returned {0}", response.StatusCode);
                }

                return BuildResult(currentVersion, null, new List<ReleaseInfo>());
            }

            using var stream = response.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);
            return ParseReleasesDocument(doc, currentVersion);
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Failed to check for updates");
            return BuildResult(currentVersion, null, new List<ReleaseInfo>());
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to parse GitHub releases response");
            return BuildResult(currentVersion, null, new List<ReleaseInfo>());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error checking for updates");
            return BuildResult(currentVersion, null, new List<ReleaseInfo>());
        }
    }

    private async Task<UpdateInfo> FetchUpdateInfoAsync(CancellationToken cancellationToken)
    {
        var currentVersion = BuildInfo.Version;

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
                var errorBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                _logger.Warn("GitHub releases API returned {0}: {1}", response.StatusCode, errorBody);
                return BuildResult(currentVersion, null, new List<ReleaseInfo>());
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            return ParseReleasesDocument(doc, currentVersion);
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Failed to check for updates");
            return BuildResult(currentVersion, null, new List<ReleaseInfo>());
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to parse GitHub releases response");
            return BuildResult(currentVersion, null, new List<ReleaseInfo>());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error checking for updates");
            return BuildResult(currentVersion, null, new List<ReleaseInfo>());
        }
    }

    private static UpdateInfo LoadFromChangelogOrFallback(Version currentVersion)
    {
        var changelogReleases = LoadFromChangelog();
        if (changelogReleases.Count > 0)
        {
            Version latestVersion = null;
            foreach (var r in changelogReleases)
            {
                if (Version.TryParse(r.Version, out var v) && (latestVersion == null || v > latestVersion))
                {
                    latestVersion = v;
                }
            }

            return BuildResult(currentVersion, latestVersion, changelogReleases);
        }

        return BuildResult(currentVersion, null, new List<ReleaseInfo>());
    }

    public static List<ReleaseInfo> LoadFromChangelog()
    {
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

        var headerRegex = new Regex(@"^##\s+\[?v?([0-9]+(?:\.[0-9]+)+)\]?(?:\(([^)]+)\))?(?:\s*-\s*([0-9]{4}-[0-9]{2}-[0-9]{2}))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                var url = match.Groups[2].Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value) ? match.Groups[2].Value : $"https://github.com/dmzoneill/Seedarr/releases/tag/v{ver}";
                var pubDate = match.Groups[3].Success && DateTime.TryParse(match.Groups[3].Value, out var dt) ? dt : DateTime.UtcNow;

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

    private static UpdateInfo ParseReleasesDocument(JsonDocument doc, Version currentVersion)
    {
        var releases = doc.RootElement;
        if (releases.ValueKind != JsonValueKind.Array)
        {
            return BuildResult(currentVersion, null, new List<ReleaseInfo>());
        }

        var releaseList = new List<ReleaseInfo>();
        Version latestVersion = null;

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

            if (!Version.TryParse(versionString, out var parsedVer))
            {
                continue;
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

            if (latestVersion == null || parsedVer > latestVersion)
            {
                latestVersion = parsedVer;
            }
        }

        return BuildResult(currentVersion, latestVersion, releaseList);
    }

    private static UpdateInfo BuildResult(Version currentVersion, Version latestVersion, List<ReleaseInfo> releases)
    {
        var updateAvailable = latestVersion != null && currentVersion != null && latestVersion > currentVersion;

        return new UpdateInfo
        {
            CurrentVersion = currentVersion?.ToString() ?? "1.0.0",
            LatestVersion = latestVersion?.ToString(),
            UpdateAvailable = updateAvailable,
            Releases = releases,
        };
    }
}
