using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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
    UpdateInfo CheckForUpdate();
    Version GetLatestVersion();
}

public class UpdateService : IUpdateService
{
    private const string GitHubReleasesUrl = "https://api.github.com/repos/dmzoneill/Seedarr/releases";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private static readonly HttpClient SharedClient = CreateHttpClient();
    private readonly HttpClient _client;
    private readonly Logger _logger;
    private readonly object _cacheLock = new();
    private UpdateInfo _cachedResult;
    private DateTime _cacheExpiry = DateTime.MinValue;

    public UpdateService(HttpClient httpClient = null)
    {
        _client = httpClient ?? SharedClient;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public UpdateInfo CheckForUpdate()
    {
        lock (_cacheLock)
        {
            if (_cachedResult != null && DateTime.UtcNow < _cacheExpiry)
            {
                return _cachedResult;
            }
        }

        var result = FetchUpdateInfo();

        lock (_cacheLock)
        {
            _cachedResult = result;
            _cacheExpiry = result.Releases.Count > 0
                ? DateTime.UtcNow.Add(CacheDuration)
                : DateTime.UtcNow.AddHours(1);
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
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesUrl + "?per_page=20");
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

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var releases = doc.RootElement;

            if (releases.ValueKind != JsonValueKind.Array)
            {
                _logger.Warn("GitHub releases response was not an array");
                return BuildResult(currentVersion, null, new List<ReleaseInfo>());
            }

            var releaseList = new List<ReleaseInfo>();
            Version latestVersion = null;

            foreach (var release in releases.EnumerateArray())
            {
                var tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
                var versionString = tagName?.TrimStart('v', 'V');

                if (!Version.TryParse(versionString, out var version))
                {
                    continue;
                }

                if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                {
                    continue;
                }

                var publishedAt = release.TryGetProperty("published_at", out var pub) && pub.ValueKind == JsonValueKind.String
                    ? DateTime.Parse(pub.GetString())
                    : DateTime.UtcNow;

                var body = release.TryGetProperty("body", out var notes) ? notes.GetString() : null;
                var htmlUrl = release.TryGetProperty("html_url", out var url) ? url.GetString() : null;

                releaseList.Add(new ReleaseInfo
                {
                    Version = version.ToString(),
                    PublishedAt = publishedAt,
                    Body = body,
                    Url = htmlUrl,
                });

                if (latestVersion == null || version > latestVersion)
                {
                    latestVersion = version;
                }
            }

            _logger.Debug("Current: {0}, Latest: {1}, Releases: {2}", currentVersion, latestVersion, releaseList.Count);
            return BuildResult(currentVersion, latestVersion, releaseList);
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

    private static UpdateInfo BuildResult(Version currentVersion, Version latestVersion, List<ReleaseInfo> releases)
    {
        var updateAvailable = latestVersion != null && latestVersion > currentVersion;

        return new UpdateInfo
        {
            CurrentVersion = currentVersion.ToString(),
            LatestVersion = latestVersion?.ToString(),
            UpdateAvailable = updateAvailable,
            Releases = releases,
        };
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        });
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Seedarr", BuildInfo.Version.ToString()));
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }
}
