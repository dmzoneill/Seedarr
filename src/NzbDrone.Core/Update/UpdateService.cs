using System;
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
}

public interface IUpdateService
{
    UpdateInfo CheckForUpdate();
    Version GetLatestVersion();
}

public class UpdateService : IUpdateService
{
    private const string GitHubReleasesUrl = "https://api.github.com/repos/dmzoneill/Seedarr/releases/latest";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private static readonly HttpClient Client = CreateHttpClient();
    private readonly Logger _logger;
    private readonly object _cacheLock = new();
    private UpdateInfo _cachedResult;
    private DateTime _cacheExpiry = DateTime.MinValue;

    public UpdateService()
    {
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
            _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
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
            var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesUrl);
            var response = Client.Send(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("GitHub releases API returned {0}", response.StatusCode);
                return BuildResult(currentVersion, null, null, null);
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;
            var body = root.TryGetProperty("body", out var notes) ? notes.GetString() : null;

            var latestVersionString = tagName?.TrimStart('v', 'V');

            if (!Version.TryParse(latestVersionString, out var latestVersion))
            {
                _logger.Warn("Unable to parse version from tag: {0}", tagName);
                return BuildResult(currentVersion, null, htmlUrl, body);
            }

            _logger.Debug("Current: {0}, Latest: {1}", currentVersion, latestVersion);
            return BuildResult(currentVersion, latestVersion, htmlUrl, body);
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Failed to check for updates");
            return BuildResult(currentVersion, null, null, null);
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to parse GitHub releases response");
            return BuildResult(currentVersion, null, null, null);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error checking for updates");
            return BuildResult(currentVersion, null, null, null);
        }
    }

    private static UpdateInfo BuildResult(Version currentVersion, Version latestVersion, string releaseUrl, string releaseNotes)
    {
        var updateAvailable = latestVersion != null && latestVersion > currentVersion;

        return new UpdateInfo
        {
            CurrentVersion = currentVersion.ToString(),
            LatestVersion = latestVersion?.ToString(),
            UpdateAvailable = updateAvailable,
            ReleaseUrl = releaseUrl,
            ReleaseNotes = releaseNotes
        };
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Seedarr", BuildInfo.Version.ToString()));
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }
}
