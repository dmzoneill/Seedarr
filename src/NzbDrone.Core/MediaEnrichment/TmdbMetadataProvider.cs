using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaEnrichment;

public class TokenBucketRateLimiter
{
    private readonly double _capacity;
    private readonly double _refillRatePerSecond;
    private readonly object _lock = new();
    private double _availableTokens;
    private DateTime _lastRefillUtc;

    public TokenBucketRateLimiter(double capacity = 40, double refillRatePerSecond = 4)
    {
        _capacity = capacity;
        _refillRatePerSecond = refillRatePerSecond;
        _availableTokens = capacity;
        _lastRefillUtc = DateTime.UtcNow;
    }

    public async Task WaitForTokenAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var delay = TimeSpan.Zero;

            lock (_lock)
            {
                Refill();
                if (_availableTokens >= 1.0)
                {
                    _availableTokens -= 1.0;
                    return;
                }

                var needed = 1.0 - _availableTokens;
                var seconds = needed / _refillRatePerSecond;
                delay = TimeSpan.FromSeconds(Math.Max(seconds, 0.05));
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void Refill()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRefillUtc).TotalSeconds;
        if (elapsed > 0)
        {
            _availableTokens = Math.Min(_capacity, _availableTokens + (elapsed * _refillRatePerSecond));
            _lastRefillUtc = now;
        }
    }
}

public class TmdbMetadataProvider : ITmdbMetadataProvider
{
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private readonly IConfigService _configService;
    private readonly HttpClient _explicitClient;
    private readonly Logger _logger;

    public TmdbMetadataProvider(IConfigService configService = null, HttpClient httpClient = null)
    {
        _configService = configService;
        _explicitClient = httpClient;
        _logger = LogManager.GetCurrentClassLogger();
        RateLimiter = new TokenBucketRateLimiter(40, 4);
    }

    public string ApiKey { get; set; }

    public TokenBucketRateLimiter RateLimiter { get; set; }

    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } = Task.Delay;

    private HttpClient Client => _explicitClient ?? ArrConnectionResources.SharedClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<TorrentMediaMetadata> SearchMovieAsync(string cleanTitle, int? year = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            return null;
        }

        var query = $"query={Uri.EscapeDataString(cleanTitle)}";
        if (year.HasValue && year.Value > 0)
        {
            query += $"&year={year.Value}";
        }

        var url = BuildUrl("/search/movie", query);
        using var response = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        if (response == null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                return null;
            }

            var first = results[0];
            if (first.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
            {
                var details = await GetMovieDetailsAsync(id, cancellationToken).ConfigureAwait(false);
                if (details != null)
                {
                    return details;
                }
            }

            return ParseMovieMetadata(first);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to parse TMDb search movie response for {0}", cleanTitle);
            return null;
        }
    }

    public async Task<TorrentMediaMetadata> SearchTvAsync(string cleanTitle, int? year = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            return null;
        }

        var query = $"query={Uri.EscapeDataString(cleanTitle)}";
        if (year.HasValue && year.Value > 0)
        {
            query += $"&first_air_date_year={year.Value}";
        }

        var url = BuildUrl("/search/tv", query);
        using var response = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        if (response == null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                return null;
            }

            var first = results[0];
            if (first.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
            {
                var details = await GetTvDetailsAsync(id, cancellationToken).ConfigureAwait(false);
                if (details != null)
                {
                    return details;
                }
            }

            return ParseTvMetadata(first);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to parse TMDb search TV response for {0}", cleanTitle);
            return null;
        }
    }

    public async Task<TorrentMediaMetadata> FindByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return null;
        }

        var url = BuildUrl($"/find/{Uri.EscapeDataString(imdbId)}", "external_source=imdb_id");
        using var response = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        if (response == null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("movie_results", out var movieResults) && movieResults.GetArrayLength() > 0)
            {
                var first = movieResults[0];
                if (first.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
                {
                    var details = await GetMovieDetailsAsync(id, cancellationToken).ConfigureAwait(false);
                    if (details != null)
                    {
                        if (string.IsNullOrEmpty(details.ImdbId))
                        {
                            details.ImdbId = imdbId;
                        }

                        return details;
                    }
                }

                var meta = ParseMovieMetadata(first);
                if (string.IsNullOrEmpty(meta.ImdbId))
                {
                    meta.ImdbId = imdbId;
                }

                return meta;
            }

            if (root.TryGetProperty("tv_results", out var tvResults) && tvResults.GetArrayLength() > 0)
            {
                var first = tvResults[0];
                if (first.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
                {
                    var details = await GetTvDetailsAsync(id, cancellationToken).ConfigureAwait(false);
                    if (details != null)
                    {
                        if (string.IsNullOrEmpty(details.ImdbId))
                        {
                            details.ImdbId = imdbId;
                        }

                        return details;
                    }
                }

                var meta = ParseTvMetadata(first);
                if (string.IsNullOrEmpty(meta.ImdbId))
                {
                    meta.ImdbId = imdbId;
                }

                return meta;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to parse TMDb find response for IMDb ID {0}", imdbId);
            return null;
        }
    }

    public async Task<TorrentMediaMetadata> GetMovieDetailsAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        var url = BuildUrl($"/movie/{tmdbId}", "append_to_response=credits");
        using var response = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        if (response == null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(content);
            return ParseMovieMetadata(doc.RootElement);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to parse TMDb movie details for ID {0}", tmdbId);
            return null;
        }
    }

    public async Task<TorrentMediaMetadata> GetTvDetailsAsync(int tmdbId, CancellationToken cancellationToken = default)
    {
        var url = BuildUrl($"/tv/{tmdbId}", "append_to_response=credits");
        using var response = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
        if (response == null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(content);
            return ParseTvMetadata(doc.RootElement);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to parse TMDb TV details for ID {0}", tmdbId);
            return null;
        }
    }

    public async Task<TorrentMediaMetadata> LookupMediaAsync(
        string title,
        int? year = null,
        string imdbId = null,
        string mediaType = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            var byImdb = await FindByImdbIdAsync(imdbId, cancellationToken).ConfigureAwait(false);
            if (byImdb != null)
            {
                return byImdb;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var isTv = string.Equals(mediaType, "Sonarr", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mediaType, "tv", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mediaType, "series", StringComparison.OrdinalIgnoreCase);

        if (isTv)
        {
            var tv = await SearchTvAsync(title, year, cancellationToken).ConfigureAwait(false);
            if (tv != null)
            {
                return tv;
            }

            return await SearchMovieAsync(title, year, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var movie = await SearchMovieAsync(title, year, cancellationToken).ConfigureAwait(false);
            if (movie != null)
            {
                return movie;
            }

            return await SearchTvAsync(title, year, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TorrentMediaMetadata ParseMovieMetadata(JsonElement root)
    {
        var metadata = new TorrentMediaMetadata
        {
            ArrType = "Radarr",
        };

        if (root.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
        {
            metadata.TmdbId = id.ToString();
        }

        if (root.TryGetProperty("title", out var titleProp) && !string.IsNullOrWhiteSpace(titleProp.GetString()))
        {
            metadata.Title = titleProp.GetString();
        }
        else if (root.TryGetProperty("original_title", out var origTitleProp) && !string.IsNullOrWhiteSpace(origTitleProp.GetString()))
        {
            metadata.Title = origTitleProp.GetString();
        }

        if (root.TryGetProperty("overview", out var overviewProp))
        {
            metadata.Overview = overviewProp.GetString();
        }

        if (root.TryGetProperty("vote_average", out var ratingProp) && ratingProp.TryGetDouble(out var rating))
        {
            metadata.Rating = rating;
        }

        if (root.TryGetProperty("release_date", out var releaseDateProp))
        {
            var dateStr = releaseDateProp.GetString();
            if (!string.IsNullOrWhiteSpace(dateStr))
            {
                if (dateStr.Length >= 4 && int.TryParse(dateStr.AsSpan(0, 4), out var parsedYear))
                {
                    metadata.Year = parsedYear;
                }

                if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate))
                {
                    metadata.ReleaseDate = releaseDate;
                }
            }
        }

        if (root.TryGetProperty("poster_path", out var posterProp) && !string.IsNullOrWhiteSpace(posterProp.GetString()))
        {
            var path = posterProp.GetString();
            metadata.PosterUrl = $"https://image.tmdb.org/t/p/w500{(path.StartsWith('/') ? path : "/" + path)}";
        }

        if (root.TryGetProperty("backdrop_path", out var backdropProp) && !string.IsNullOrWhiteSpace(backdropProp.GetString()))
        {
            var path = backdropProp.GetString();
            metadata.BackdropUrl = $"https://image.tmdb.org/t/p/original{(path.StartsWith('/') ? path : "/" + path)}";
        }

        if (root.TryGetProperty("imdb_id", out var imdbProp) && !string.IsNullOrWhiteSpace(imdbProp.GetString()))
        {
            metadata.ImdbId = imdbProp.GetString();
        }
        else if (root.TryGetProperty("external_ids", out var extIds) && extIds.TryGetProperty("imdb_id", out var extImdb) && !string.IsNullOrWhiteSpace(extImdb.GetString()))
        {
            metadata.ImdbId = extImdb.GetString();
        }

        // Genres
        if (root.TryGetProperty("genres", out var genresProp) && genresProp.ValueKind == JsonValueKind.Array)
        {
            var genres = new List<string>();
            foreach (var g in genresProp.EnumerateArray())
            {
                if (g.ValueKind == JsonValueKind.Object && g.TryGetProperty("name", out var gName))
                {
                    var name = gName.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        genres.Add(name);
                    }
                }
                else if (g.ValueKind == JsonValueKind.String)
                {
                    var name = g.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        genres.Add(name);
                    }
                }
            }

            if (genres.Count > 0)
            {
                metadata.Genres = string.Join(", ", genres);
            }
        }

        // Cast
        var castContainer = default(JsonElement);
        if (root.TryGetProperty("credits", out var creditsProp) && creditsProp.TryGetProperty("cast", out var creditsCast))
        {
            castContainer = creditsCast;
        }
        else if (root.TryGetProperty("cast", out var directCast))
        {
            castContainer = directCast;
        }

        if (castContainer.ValueKind == JsonValueKind.Array)
        {
            var castList = new List<string>();
            foreach (var member in castContainer.EnumerateArray())
            {
                string actorName = null;
                if (member.ValueKind == JsonValueKind.Object && member.TryGetProperty("name", out var nProp))
                {
                    actorName = nProp.GetString();
                }
                else if (member.ValueKind == JsonValueKind.String)
                {
                    actorName = member.GetString();
                }

                if (!string.IsNullOrWhiteSpace(actorName))
                {
                    castList.Add(actorName);
                }

                if (castList.Count >= 10)
                {
                    break;
                }
            }

            if (castList.Count > 0)
            {
                metadata.Cast = string.Join(", ", castList);
            }
        }

        return metadata;
    }

    private static TorrentMediaMetadata ParseTvMetadata(JsonElement root)
    {
        var metadata = new TorrentMediaMetadata
        {
            ArrType = "Sonarr",
        };

        if (root.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
        {
            metadata.TmdbId = id.ToString();
        }

        if (root.TryGetProperty("name", out var titleProp) && !string.IsNullOrWhiteSpace(titleProp.GetString()))
        {
            metadata.Title = titleProp.GetString();
        }
        else if (root.TryGetProperty("original_name", out var origTitleProp) && !string.IsNullOrWhiteSpace(origTitleProp.GetString()))
        {
            metadata.Title = origTitleProp.GetString();
        }

        if (root.TryGetProperty("overview", out var overviewProp))
        {
            metadata.Overview = overviewProp.GetString();
        }

        if (root.TryGetProperty("vote_average", out var ratingProp) && ratingProp.TryGetDouble(out var rating))
        {
            metadata.Rating = rating;
        }

        if (root.TryGetProperty("first_air_date", out var airDateProp))
        {
            var dateStr = airDateProp.GetString();
            if (!string.IsNullOrWhiteSpace(dateStr))
            {
                if (dateStr.Length >= 4 && int.TryParse(dateStr.AsSpan(0, 4), out var parsedYear))
                {
                    metadata.Year = parsedYear;
                }

                if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate))
                {
                    metadata.ReleaseDate = releaseDate;
                }
            }
        }

        if (root.TryGetProperty("poster_path", out var posterProp) && !string.IsNullOrWhiteSpace(posterProp.GetString()))
        {
            var path = posterProp.GetString();
            metadata.PosterUrl = $"https://image.tmdb.org/t/p/w500{(path.StartsWith('/') ? path : "/" + path)}";
        }

        if (root.TryGetProperty("backdrop_path", out var backdropProp) && !string.IsNullOrWhiteSpace(backdropProp.GetString()))
        {
            var path = backdropProp.GetString();
            metadata.BackdropUrl = $"https://image.tmdb.org/t/p/original{(path.StartsWith('/') ? path : "/" + path)}";
        }

        if (root.TryGetProperty("external_ids", out var extIds) && extIds.TryGetProperty("imdb_id", out var extImdb) && !string.IsNullOrWhiteSpace(extImdb.GetString()))
        {
            metadata.ImdbId = extImdb.GetString();
        }
        else if (root.TryGetProperty("imdb_id", out var imdbProp) && !string.IsNullOrWhiteSpace(imdbProp.GetString()))
        {
            metadata.ImdbId = imdbProp.GetString();
        }

        // Genres
        if (root.TryGetProperty("genres", out var genresProp) && genresProp.ValueKind == JsonValueKind.Array)
        {
            var genres = new List<string>();
            foreach (var g in genresProp.EnumerateArray())
            {
                if (g.ValueKind == JsonValueKind.Object && g.TryGetProperty("name", out var gName))
                {
                    var name = gName.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        genres.Add(name);
                    }
                }
                else if (g.ValueKind == JsonValueKind.String)
                {
                    var name = g.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        genres.Add(name);
                    }
                }
            }

            if (genres.Count > 0)
            {
                metadata.Genres = string.Join(", ", genres);
            }
        }

        // Cast
        var castContainer = default(JsonElement);
        if (root.TryGetProperty("credits", out var creditsProp) && creditsProp.TryGetProperty("cast", out var creditsCast))
        {
            castContainer = creditsCast;
        }
        else if (root.TryGetProperty("cast", out var directCast))
        {
            castContainer = directCast;
        }

        if (castContainer.ValueKind == JsonValueKind.Array)
        {
            var castList = new List<string>();
            foreach (var member in castContainer.EnumerateArray())
            {
                string actorName = null;
                if (member.ValueKind == JsonValueKind.Object && member.TryGetProperty("name", out var nProp))
                {
                    actorName = nProp.GetString();
                }
                else if (member.ValueKind == JsonValueKind.String)
                {
                    actorName = member.GetString();
                }

                if (!string.IsNullOrWhiteSpace(actorName))
                {
                    castList.Add(actorName);
                }

                if (castList.Count >= 10)
                {
                    break;
                }
            }

            if (castList.Count > 0)
            {
                metadata.Cast = string.Join(", ", castList);
            }
        }

        return metadata;
    }

    private string BuildUrl(string path, string queryParams = null)
    {
        var apiKey = GetEffectiveApiKey();
        var separator = path.Contains('?') ? "&" : "?";
        var sb = new StringBuilder();
        sb.Append(BaseUrl);
        sb.Append(path.StartsWith('/') ? path : "/" + path);

        if (!string.IsNullOrEmpty(queryParams))
        {
            sb.Append(separator);
            sb.Append(queryParams);
            sb.Append("&api_key=");
            sb.Append(Uri.EscapeDataString(apiKey));
        }
        else
        {
            sb.Append(separator);
            sb.Append("api_key=");
            sb.Append(Uri.EscapeDataString(apiKey));
        }

        return sb.ToString();
    }

    private string GetEffectiveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey;
        }

        var key = _configService?.TmdbApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        return ConfigService.DefaultTmdbApiKey;
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (RateLimiter != null)
            {
                await RateLimiter.WaitForTokenAsync(cancellationToken).ConfigureAwait(false);
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            HttpResponseMessage response;
            try
            {
                response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxRetries && ex is not OperationCanceledException)
            {
                _logger.Warn(ex, "TMDb request to {0} failed on attempt {1}", url, attempt + 1);
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(Random.Shared.Next(50, 200));
                await DelayAsync(backoff, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (attempt == maxRetries)
                {
                    _logger.Warn("TMDb rate limit exceeded, max retries reached for {0}", url);
                    return response;
                }

                var delay = GetRetryAfterDelay(response, attempt);
                _logger.Warn(
                    "TMDb returned 429 Too Many Requests. Backing off for {0}ms (attempt {1}/{2})",
                    delay.TotalMilliseconds,
                    attempt + 1,
                    maxRetries);

                response.Dispose();
                await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return response;
        }

        return null;
    }

    private TimeSpan GetRetryAfterDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue)
            {
                var sec = response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                if (sec > 0)
                {
                    var jitterMs = Random.Shared.Next(50, 250);
                    return TimeSpan.FromSeconds(sec) + TimeSpan.FromMilliseconds(jitterMs);
                }

                return TimeSpan.Zero;
            }

            if (response.Headers.RetryAfter.Date.HasValue)
            {
                var diff = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                if (diff > TimeSpan.Zero)
                {
                    var jitterMs = Random.Shared.Next(50, 250);
                    return diff + TimeSpan.FromMilliseconds(jitterMs);
                }

                return TimeSpan.Zero;
            }
        }

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var sec))
            {
                if (sec > 0)
                {
                    var jitterMs = Random.Shared.Next(50, 250);
                    return TimeSpan.FromSeconds(sec) + TimeSpan.FromMilliseconds(jitterMs);
                }

                return TimeSpan.Zero;
            }
        }

        var baseSec = Math.Pow(2, attempt);
        var jitter = Random.Shared.Next(100, 500);
        return TimeSpan.FromSeconds(baseSec) + TimeSpan.FromMilliseconds(jitter);
    }
}
