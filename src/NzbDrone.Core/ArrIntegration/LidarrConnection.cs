using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Polly;

namespace NzbDrone.Core.ArrIntegration;

public class LidarrConnection : IArrConnection
{
    private readonly HttpClient _explicitClient;
    private readonly ResiliencePipeline _policy;
    private readonly Logger _logger;
    private string _url = "http://localhost:8686";

    public LidarrConnection(HttpClient client = null, ResiliencePipeline policy = null)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _explicitClient = client;
        _policy = policy ?? ArrConnectionResources.SharedPolicy;
    }

    public string Name => "Lidarr";
    public string ArrType => "Lidarr";
    public int ConnectionId { get; set; }

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
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url.TrimEnd('/')}/api/v1/history?pageSize=50&sortKey=date&sortDirection=descending");
                    request.Headers.Add("X-Api-Key", ApiKey ?? "");

                    using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Warn("Lidarr API returned {0}", response.StatusCode);
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
            _logger.Error(ex, "Failed to fetch Lidarr history");
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
                    Date = record.TryGetProperty("date", out var date) ? date.GetDateTime() : DateTime.UtcNow
                };

                if (record.TryGetProperty("albumId", out var aId) && aId.TryGetInt32(out var albumIdVal))
                {
                    downloadRecord.MediaId = albumIdVal;
                    downloadRecord.MediaType = "album";
                }
                else if (record.TryGetProperty("artistId", out var arId) && arId.TryGetInt32(out var artistIdVal))
                {
                    downloadRecord.MediaId = artistIdVal;
                    downloadRecord.MediaType = "artist";
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

        _logger.Debug("Fetched {0} download records from Lidarr", records.Count);
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
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url.TrimEnd('/')}/api/v1/album/{mediaId}");
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
            _logger.Warn(ex, "Failed to get album media details for id {0}", mediaId);
            return null;
        }
    }

    private MediaMetadata ParseMediaDetails(string json, int mediaId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var metadata = new MediaMetadata
        {
            MediaType = "album",
            MediaId = mediaId,
            Title = root.TryGetProperty("title", out var title) ? title.GetString() : null,
            Overview = root.TryGetProperty("overview", out var ov) ? ov.GetString() : null
        };

        if (root.TryGetProperty("foreignAlbumId", out var mbProp))
        {
            metadata.MusicBrainzId = mbProp.GetString();
        }
        else if (root.TryGetProperty("musicBrainzId", out var mbIdProp))
        {
            metadata.MusicBrainzId = mbIdProp.GetString();
        }

        if (root.TryGetProperty("artist", out var artistElem))
        {
            metadata.StudioOrNetwork = artistElem.TryGetProperty("artistName", out var an) ? an.GetString() : null;
        }

        if (root.TryGetProperty("releaseDate", out var rd) && rd.TryGetDateTime(out var rdVal))
        {
            metadata.Year = rdVal.Year;
        }

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

        if (root.TryGetProperty("images", out var imagesArray))
        {
            foreach (var img in imagesArray.EnumerateArray())
            {
                var coverType = img.TryGetProperty("coverType", out var ct) ? ct.GetString() : "";
                var remoteUrl = img.TryGetProperty("remoteUrl", out var ru) ? ru.GetString() : null;
                var localUrl = img.TryGetProperty("url", out var lu) ? lu.GetString() : null;
                var imgUrl = !string.IsNullOrEmpty(remoteUrl) ? remoteUrl : localUrl;

                if (string.IsNullOrEmpty(imgUrl))
                {
                    continue;
                }

                var normalizedUrl = ArrConnectionResources.NormalizeCoverUrl(imgUrl, ConnectionId);

                if (coverType.Equals("cover", StringComparison.OrdinalIgnoreCase) || coverType.Equals("poster", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.PosterUrl = normalizedUrl;
                }
                else if (coverType.Equals("fanart", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.FanartUrl = normalizedUrl;
                }
                else if (coverType.Equals("banner", StringComparison.OrdinalIgnoreCase))
                {
                    metadata.BannerUrl = normalizedUrl;
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
                    var searchUrl = $"{Url.TrimEnd('/')}/api/v1/search?term={Uri.EscapeDataString(title.Trim())}";
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
            _logger.Warn(ex, "Failed to lookup album by term '{0}' on Lidarr", title);
            return null;
        }
    }

    private MediaMetadata ParseLookupMedia(string json, string title)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var foreignElem = item.TryGetProperty("foreignAlbumId", out _) || item.TryGetProperty("album", out _)
                ? (item.TryGetProperty("album", out var alb) ? alb : item)
                : item;

            var id = foreignElem.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var idVal) ? idVal : 0;
            var metadata = new MediaMetadata
            {
                MediaType = "album",
                MediaId = id,
                Title = foreignElem.TryGetProperty("title", out var tProp) ? tProp.GetString() : title,
                Overview = foreignElem.TryGetProperty("overview", out var ov) ? ov.GetString() : null,
                StudioOrNetwork = foreignElem.TryGetProperty("artist", out var art) && art.TryGetProperty("artistName", out var an) ? an.GetString() : null
            };

            if (foreignElem.TryGetProperty("foreignAlbumId", out var mbProp))
            {
                metadata.MusicBrainzId = mbProp.GetString();
            }
            else if (foreignElem.TryGetProperty("musicBrainzId", out var mbIdProp))
            {
                metadata.MusicBrainzId = mbIdProp.GetString();
            }
            else if (item.TryGetProperty("foreignAlbumId", out var itemMbProp))
            {
                metadata.MusicBrainzId = itemMbProp.GetString();
            }

            if (foreignElem.TryGetProperty("images", out var imagesArray))
            {
                foreach (var imgElem in imagesArray.EnumerateArray())
                {
                    var coverType = imgElem.TryGetProperty("coverType", out var ctProp) ? ctProp.GetString() ?? "" : "";
                    var imgUrl = imgElem.TryGetProperty("remoteUrl", out var ruProp) ? ruProp.GetString() : (imgElem.TryGetProperty("url", out var luProp) ? luProp.GetString() : null);

                    if (string.IsNullOrEmpty(imgUrl))
                    {
                        continue;
                    }

                    var normalizedUrl = ArrConnectionResources.NormalizeCoverUrl(imgUrl, ConnectionId);

                    if (coverType.Equals("cover", StringComparison.OrdinalIgnoreCase) || coverType.Equals("poster", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.PosterUrl = normalizedUrl;
                    }
                    else if (coverType.Equals("fanart", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.FanartUrl = normalizedUrl;
                    }
                    else if (coverType.Equals("banner", StringComparison.OrdinalIgnoreCase))
                    {
                        metadata.BannerUrl = normalizedUrl;
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
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{normalizedUrl}/api/v1/system/status");
            request.Headers.Add("X-Api-Key", ApiKey ?? "");
            using var response = Client.Send(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                return ArrTestResult.Ok($"Successfully connected to Lidarr at {normalizedUrl}");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return ArrTestResult.Fail("Authentication failed (HTTP 401 Unauthorized). Please check your API key.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ArrTestResult.Fail($"Endpoint not found (HTTP 404 Not Found) at {normalizedUrl}. Verify the URL and port.");
            }

            return ArrTestResult.Fail($"Lidarr returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Lidarr connection test failed: {0}", ex.Message);
            return ArrTestResult.Fail($"Network error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            _logger.Error("Lidarr connection test timed out");
            return ArrTestResult.Fail($"Connection timed out connecting to {normalizedUrl} (exceeded 10s)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Lidarr connection test failed");
            return ArrTestResult.Fail($"Connection failed: {ex.Message}");
        }
    }
}
