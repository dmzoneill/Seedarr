using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Polly;

namespace NzbDrone.Core.ArrIntegration;

public class RadarrConnection : IArrConnection
{
    private readonly HttpClient _explicitClient;
    private readonly ResiliencePipeline _policy;
    private readonly Logger _logger;
    private string _url = "http://localhost:7878";

    public RadarrConnection(HttpClient client = null, ResiliencePipeline policy = null)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _explicitClient = client;
        _policy = policy ?? ArrConnectionResources.SharedPolicy;
    }

    public string Name => "Radarr";
    public string ArrType => "Radarr";

    public string Url
    {
        get => _url;
        set => _url = ArrConnectionResources.NormalizeUrl(value);
    }

    public string ApiKey { get; set; } = "";
    public bool AcceptInvalidCertificates { get; set; }

    private HttpClient Client => _explicitClient ?? ArrConnectionResources.GetClient(AcceptInvalidCertificates);

    public List<ArrDownloadRecord> GetDownloadHistory() =>
        GetDownloadHistoryAsync().GetAwaiter().GetResult();

    public async Task<List<ArrDownloadRecord>> GetDownloadHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _policy.ExecuteAsync<string>(
                async ct =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url.TrimEnd('/')}/api/v3/history?pageSize=50&sortKey=date&sortDirection=descending");
                    request.Headers.Add("X-Api-Key", ApiKey ?? "");

                    using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Warn("Radarr API returned {0}", response.StatusCode);
                        return null;
                    }

                    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (result == null)
            {
                return new List<ArrDownloadRecord>();
            }

            return ParseDownloadHistory(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch Radarr history");
            return new List<ArrDownloadRecord>();
        }
    }

    private List<ArrDownloadRecord> ParseDownloadHistory(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var records = new List<ArrDownloadRecord>();

        if (doc.RootElement.TryGetProperty("records", out var recordsArray))
        {
            foreach (var record in recordsArray.EnumerateArray())
            {
                if (!record.TryGetProperty("eventType", out var eventTypeElement))
                {
                    continue;
                }

                var eventType = eventTypeElement.GetString();
                if (eventType != "grabbed")
                {
                    continue;
                }

                var downloadRecord = new ArrDownloadRecord
                {
                    Title = record.TryGetProperty("sourceTitle", out var title) ? title.GetString() : "",
                    DownloadId = record.TryGetProperty("downloadId", out var dlId) ? dlId.GetString() : "",
                    Date = record.TryGetProperty("date", out var date) ? date.GetDateTime() : DateTime.UtcNow,
                    MediaType = "movie"
                };

                if (record.TryGetProperty("movieId", out var mId) && mId.TryGetInt32(out var movieIdVal))
                {
                    downloadRecord.MediaId = movieIdVal;
                }

                if (record.TryGetProperty("data", out var data))
                {
                    downloadRecord.InfoHash = data.TryGetProperty("torrentInfoHash", out var hash) ? hash.GetString() : null;
                    downloadRecord.Indexer = data.TryGetProperty("indexer", out var indexer) ? indexer.GetString() : null;
                    downloadRecord.DownloadClient = data.TryGetProperty("downloadClient", out var dc) ? dc.GetString() : null;
                    downloadRecord.DownloadUrl = data.TryGetProperty("downloadUrl", out var dlUrl) ? dlUrl.GetString() : null;
                }

                if (!string.IsNullOrEmpty(downloadRecord.InfoHash))
                {
                    records.Add(downloadRecord);
                }
            }
        }

        _logger.Debug("Fetched {0} download records from Radarr", records.Count);
        return records;
    }

    public MediaMetadata GetMediaDetails(int mediaId) =>
        GetMediaDetailsAsync(mediaId).GetAwaiter().GetResult();

    public async Task<MediaMetadata> GetMediaDetailsAsync(int mediaId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _policy.ExecuteAsync<string>(
                async ct =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url.TrimEnd('/')}/api/v3/movie/{mediaId}");
                    request.Headers.Add("X-Api-Key", ApiKey ?? "");
                    using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (result == null)
            {
                return null;
            }

            return ParseMediaDetails(result, mediaId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to get movie media details for id {0}", mediaId);
            return null;
        }
    }

    private static MediaMetadata ParseMediaDetails(string json, int mediaId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var metadata = new MediaMetadata
        {
            MediaType = "movie",
            MediaId = mediaId,
            Title = root.TryGetProperty("title", out var title) ? title.GetString() : null,
            Year = root.TryGetProperty("year", out var yr) && yr.TryGetInt32(out var yVal) ? yVal : null,
            Overview = root.TryGetProperty("overview", out var ov) ? ov.GetString() : null,
            StudioOrNetwork = root.TryGetProperty("studio", out var std) ? std.GetString() : null
        };

        if (root.TryGetProperty("genres", out var genresArray))
        {
            foreach (var g in genresArray.EnumerateArray())
            {
                var gStr = g.GetString();
                if (!string.IsNullOrEmpty(gStr))
                {
                    metadata.Genres.Add(gStr);
                }
            }
        }

        if (root.TryGetProperty("ratings", out var ratingsElem) && ratingsElem.TryGetProperty("imdb", out var imdbElem) && imdbElem.TryGetProperty("value", out var rVal) && rVal.TryGetDouble(out var dblVal))
        {
            metadata.Rating = Math.Round(dblVal, 1);
        }

        if (root.TryGetProperty("images", out var imagesArray))
        {
            foreach (var img in imagesArray.EnumerateArray())
            {
                var coverType = img.TryGetProperty("coverType", out var ct) ? ct.GetString() : "";
                var remoteUrl = img.TryGetProperty("remoteUrl", out var ru) ? ru.GetString() : null;
                var localUrl = img.TryGetProperty("url", out var lu) ? lu.GetString() : null;
                var imgUrl = !string.IsNullOrEmpty(remoteUrl) ? remoteUrl : localUrl;

                if (coverType.Equals("poster", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.PosterUrl = imgUrl;
                }
                else if (coverType.Equals("fanart", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.FanartUrl = imgUrl;
                }
                else if (coverType.Equals("banner", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.BannerUrl = imgUrl;
                }
            }
        }

        if (root.TryGetProperty("actors", out var actorsArray))
        {
            foreach (var actorElem in actorsArray.EnumerateArray())
            {
                var name = actorElem.TryGetProperty("name", out var an) ? an.GetString() : null;
                var character = actorElem.TryGetProperty("character", out var ac) ? ac.GetString() : null;
                var headshotUrl = (string)null;

                if (actorElem.TryGetProperty("images", out var actorImgs))
                {
                    foreach (var ai in actorImgs.EnumerateArray())
                    {
                        headshotUrl = ai.TryGetProperty("remoteUrl", out var aru) ? aru.GetString() : (ai.TryGetProperty("url", out var alu) ? alu.GetString() : null);
                        if (!string.IsNullOrEmpty(headshotUrl))
                        {
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(name))
                {
                    metadata.Actors.Add(new MediaActor
                    {
                        Name = name,
                        Character = character,
                        ImageUrl = headshotUrl
                    });
                }
            }
        }

        return metadata;
    }

    public MediaMetadata LookupMedia(string title) =>
        LookupMediaAsync(title).GetAwaiter().GetResult();

    public async Task<MediaMetadata> LookupMediaAsync(string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(Url))
        {
            return null;
        }

        try
        {
            var result = await _policy.ExecuteAsync<string>(
                async ct =>
                {
                    var searchUrl = $"{Url.TrimEnd('/')}/api/v3/movie/lookup?term={Uri.EscapeDataString(title.Trim())}";
                    using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                    request.Headers.Add("X-Api-Key", ApiKey ?? "");
                    using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result))
            {
                return null;
            }

            return ParseLookupMedia(result, title);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to lookup movie by term '{0}' on Radarr", title);
            return null;
        }
    }

    private static MediaMetadata ParseLookupMedia(string json, string title)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var root in doc.RootElement.EnumerateArray())
        {
            var id = root.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var idVal) ? idVal : 0;
            var metadata = new MediaMetadata
            {
                MediaType = "movie",
                MediaId = id,
                Title = root.TryGetProperty("title", out var tProp) ? tProp.GetString() : title,
                Year = root.TryGetProperty("year", out var yr) && yr.TryGetInt32(out var yVal) ? yVal : null,
                Overview = root.TryGetProperty("overview", out var ov) ? ov.GetString() : null,
                StudioOrNetwork = root.TryGetProperty("studio", out var std) ? std.GetString() : null
            };

            if (root.TryGetProperty("genres", out var genresArray))
            {
                foreach (var g in genresArray.EnumerateArray())
                {
                    var gStr = g.GetString();
                    if (!string.IsNullOrEmpty(gStr))
                    {
                        metadata.Genres.Add(gStr);
                    }
                }
            }

            if (root.TryGetProperty("ratings", out var ratingsElem) && ratingsElem.TryGetProperty("imdb", out var imdbElem) && imdbElem.TryGetProperty("value", out var rVal) && rVal.TryGetDouble(out var dblVal))
            {
                metadata.Rating = Math.Round(dblVal, 1);
            }

            if (root.TryGetProperty("images", out var imagesArray))
            {
                foreach (var imgElem in imagesArray.EnumerateArray())
                {
                    var coverType = imgElem.TryGetProperty("coverType", out var ctProp) ? ctProp.GetString() ?? "" : "";
                    var imgUrl = imgElem.TryGetProperty("remoteUrl", out var ruProp) ? ruProp.GetString() : (imgElem.TryGetProperty("url", out var luProp) ? luProp.GetString() : null);

                    if (string.IsNullOrEmpty(imgUrl))
                    {
                        continue;
                    }

                    if (coverType.Equals("poster", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.PosterUrl = imgUrl;
                    }
                    else if (coverType.Equals("fanart", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.FanartUrl = imgUrl;
                    }
                    else if (coverType.Equals("banner", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.BannerUrl = imgUrl;
                    }
                }
            }

            return metadata;
        }

        return null;
    }

    public bool TestConnection() => TestConnectionDetailed().Success;

    public ArrTestResult TestConnectionDetailed()
    {
        if (!ArrConnectionResources.TryNormalizeUrl(Url, out var normalizedUrl, out var urlError))
        {
            return ArrTestResult.Fail(urlError);
        }

        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{normalizedUrl}/api/v3/system/status");
            request.Headers.Add("X-Api-Key", ApiKey ?? "");
            using var response = Client.Send(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                return ArrTestResult.Ok($"Successfully connected to Radarr at {normalizedUrl}");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return ArrTestResult.Fail("Authentication failed (HTTP 401 Unauthorized). Please check your API key.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ArrTestResult.Fail($"Endpoint not found (HTTP 404 Not Found) at {normalizedUrl}. Verify the URL and port.");
            }

            return ArrTestResult.Fail($"Radarr returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Radarr connection test failed: {0}", ex.Message);
            return ArrTestResult.Fail($"Network error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            _logger.Error("Radarr connection test timed out");
            return ArrTestResult.Fail($"Connection timed out connecting to {normalizedUrl} (exceeded 10s)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Radarr connection test failed");
            return ArrTestResult.Fail($"Connection failed: {ex.Message}");
        }
    }
}
