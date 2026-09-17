using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Polly;

namespace NzbDrone.Core.ArrIntegration;

public class ReadarrConnection : IArrConnection
{
    private readonly HttpClient _explicitClient;
    private readonly ResiliencePipeline _policy;
    private readonly Logger _logger;
    private string _url = "http://localhost:8787";

    public ReadarrConnection(HttpClient client = null, ResiliencePipeline policy = null)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _explicitClient = client;
        _policy = policy ?? ArrConnectionResources.SharedPolicy;
    }

    public string Name => "Readarr";
    public string ArrType => "Readarr";
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
        GetDownloadHistory(250);

    public List<ArrDownloadRecord> GetDownloadHistory(int pageSize) =>
        GetDownloadHistoryAsync(pageSize).GetAwaiter().GetResult();

    public Task<List<ArrDownloadRecord>> GetDownloadHistoryAsync(CancellationToken cancellationToken = default) =>
        GetDownloadHistoryAsync(250, cancellationToken);

    public async Task<List<ArrDownloadRecord>> GetDownloadHistoryAsync(int pageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var effectivePageSize = Math.Max(1, pageSize);
            var result = await _policy.ExecuteAsync<string>(
                async ct =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url.TrimEnd('/')}/api/v1/history?pageSize={effectivePageSize}&sortKey=date&sortDirection=descending");
                    request.Headers.Add("X-Api-Key", ApiKey ?? "");

                    using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Warn("Readarr API returned {0}", response.StatusCode);
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
            _logger.Error(ex, "Failed to fetch Readarr history");
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

                if (record.TryGetProperty("bookId", out var bId) && bId.TryGetInt32(out var bookIdVal))
                {
                    downloadRecord.MediaId = bookIdVal;
                    downloadRecord.MediaType = "book";
                }
                else if (record.TryGetProperty("authorId", out var aId) && aId.TryGetInt32(out var authorIdVal))
                {
                    downloadRecord.MediaId = authorIdVal;
                    downloadRecord.MediaType = "author";
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

        _logger.Debug("Fetched {0} download records from Readarr", records.Count);
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
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url.TrimEnd('/')}/api/v1/book/{mediaId}");
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
            _logger.Warn(ex, "Failed to get book media details for id {0}", mediaId);
            return null;
        }
    }

    private MediaMetadata ParseMediaDetails(string json, int mediaId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var bookTitle = root.TryGetProperty("title", out var title) ? title.GetString() : null;
        var metadata = new MediaMetadata
        {
            MediaType = "book",
            MediaId = mediaId,
            Title = bookTitle,
            BookTitle = bookTitle,
            Overview = root.TryGetProperty("overview", out var ov) ? ov.GetString() : null
        };

        if (root.TryGetProperty("releaseDate", out var rd) && rd.TryGetDateTime(out var rdVal))
        {
            metadata.ReleaseDate = rdVal;
            metadata.Year = rdVal.Year;
        }

        if (root.TryGetProperty("author", out var authorElem))
        {
            var authorName = authorElem.TryGetProperty("authorName", out var an) ? an.GetString()
                : (authorElem.TryGetProperty("name", out var n) ? n.GetString() : null);
            metadata.Author = authorName;
            metadata.StudioOrNetwork = authorName;
            metadata.Studio = authorName;
        }
        else if (root.TryGetProperty("authorTitle", out var atProp))
        {
            var authorName = atProp.GetString();
            metadata.Author = authorName;
            metadata.StudioOrNetwork = authorName;
            metadata.Studio = authorName;
        }

        ExtractBookFields(root, metadata);
        ExtractImages(root, metadata);
        ExtractGenres(root, metadata);

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
            _logger.Warn(ex, "Failed to lookup book by term '{0}' on Readarr", title);
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
            var bookElem = item.TryGetProperty("book", out var b) ? b : item;

            var id = bookElem.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var idVal) ? idVal : 0;
            var bookTitle = bookElem.TryGetProperty("title", out var tProp) ? tProp.GetString() : title;
            var metadata = new MediaMetadata
            {
                MediaType = "book",
                MediaId = id,
                Title = bookTitle,
                BookTitle = bookTitle,
                Overview = bookElem.TryGetProperty("overview", out var ov) ? ov.GetString() : null
            };

            if (bookElem.TryGetProperty("releaseDate", out var rd) && rd.TryGetDateTime(out var rdVal))
            {
                metadata.ReleaseDate = rdVal;
                metadata.Year = rdVal.Year;
            }

            if (bookElem.TryGetProperty("author", out var authorElem))
            {
                var authorName = authorElem.TryGetProperty("authorName", out var an) ? an.GetString()
                    : (authorElem.TryGetProperty("name", out var n) ? n.GetString() : null);
                metadata.Author = authorName;
                metadata.StudioOrNetwork = authorName;
                metadata.Studio = authorName;
            }
            else if (bookElem.TryGetProperty("authorTitle", out var atProp))
            {
                var authorName = atProp.GetString();
                metadata.Author = authorName;
                metadata.StudioOrNetwork = authorName;
                metadata.Studio = authorName;
            }

            ExtractBookFields(bookElem, metadata);
            ExtractImages(bookElem, metadata);
            ExtractGenres(bookElem, metadata);

            return metadata;
        }

        return null;
    }

    private static void ExtractBookFields(JsonElement root, MediaMetadata metadata)
    {
        if (root.TryGetProperty("isbn", out var isbnProp))
        {
            metadata.Isbn = isbnProp.GetString();
        }
        else if (root.TryGetProperty("isbn13", out var isbn13Prop))
        {
            metadata.Isbn = isbn13Prop.GetString();
        }

        if (root.TryGetProperty("publisher", out var pubProp))
        {
            metadata.Publisher = pubProp.GetString();
        }

        if (root.TryGetProperty("pageCount", out var pcProp) && pcProp.TryGetInt32(out var pcVal))
        {
            metadata.PageCount = pcVal;
        }

        if (root.TryGetProperty("packagingFormat", out var pfProp))
        {
            metadata.PackagingFormat = pfProp.GetString();
        }
        else if (root.TryGetProperty("format", out var formatProp))
        {
            metadata.PackagingFormat = formatProp.GetString();
        }

        if (root.TryGetProperty("asin", out var asinProp))
        {
            metadata.Asin = asinProp.GetString();
        }

        if (root.TryGetProperty("seriesTitle", out var stProp))
        {
            metadata.SeriesName = stProp.GetString();
        }

        if (root.TryGetProperty("seriesPosition", out var spProp))
        {
            metadata.SeriesPosition = spProp.GetString();
        }

        if (root.TryGetProperty("editions", out var editionsArray) && editionsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var edition in editionsArray.EnumerateArray())
            {
                if (string.IsNullOrEmpty(metadata.Isbn))
                {
                    if (edition.TryGetProperty("isbn13", out var edIsbn13))
                    {
                        metadata.Isbn = edIsbn13.GetString();
                    }
                    else if (edition.TryGetProperty("isbn", out var edIsbn))
                    {
                        metadata.Isbn = edIsbn.GetString();
                    }
                    else if (edition.TryGetProperty("isbn10", out var edIsbn10))
                    {
                        metadata.Isbn = edIsbn10.GetString();
                    }
                }

                if (string.IsNullOrEmpty(metadata.Publisher) && edition.TryGetProperty("publisher", out var edPub))
                {
                    metadata.Publisher = edPub.GetString();
                }

                if (!metadata.PageCount.HasValue && edition.TryGetProperty("pageCount", out var edPc) && edPc.TryGetInt32(out var edPcVal))
                {
                    metadata.PageCount = edPcVal;
                }

                if (string.IsNullOrEmpty(metadata.PackagingFormat) && edition.TryGetProperty("format", out var edFmt))
                {
                    metadata.PackagingFormat = edFmt.GetString();
                }

                if (string.IsNullOrEmpty(metadata.Asin) && edition.TryGetProperty("asin", out var edAsin))
                {
                    metadata.Asin = edAsin.GetString();
                }

                break;
            }
        }

        if (root.TryGetProperty("series", out var seriesProp))
        {
            if (seriesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var sItem in seriesProp.EnumerateArray())
                {
                    if (string.IsNullOrEmpty(metadata.SeriesName))
                    {
                        if (sItem.TryGetProperty("series", out var innerSeries) && innerSeries.TryGetProperty("title", out var innerTitle))
                        {
                            metadata.SeriesName = innerTitle.GetString();
                        }
                        else if (sItem.TryGetProperty("title", out var sTitle))
                        {
                            metadata.SeriesName = sTitle.GetString();
                        }
                    }

                    if (string.IsNullOrEmpty(metadata.SeriesPosition))
                    {
                        if (sItem.TryGetProperty("position", out var posProp))
                        {
                            metadata.SeriesPosition = posProp.GetString();
                        }
                    }

                    break;
                }
            }
            else if (seriesProp.ValueKind == JsonValueKind.Object)
            {
                if (string.IsNullOrEmpty(metadata.SeriesName) && seriesProp.TryGetProperty("title", out var sTitle))
                {
                    metadata.SeriesName = sTitle.GetString();
                }
            }
        }

        if (root.TryGetProperty("ratings", out var ratingsProp))
        {
            if (ratingsProp.TryGetProperty("value", out var rVal) && rVal.TryGetDouble(out var ratingVal))
            {
                metadata.Rating = ratingVal;
            }
        }
        else if (root.TryGetProperty("rating", out var ratingProp) && ratingProp.TryGetDouble(out var singleRating))
        {
            metadata.Rating = singleRating;
        }
    }

    private void ExtractImages(JsonElement root, MediaMetadata metadata)
    {
        if (root.TryGetProperty("images", out var imagesArray) && imagesArray.ValueKind == JsonValueKind.Array)
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

                if (coverType.Equals("cover", StringComparison.OrdinalIgnoreCase) ||
                    coverType.Equals("poster", StringComparison.OrdinalIgnoreCase) ||
                    coverType.Equals("bookCover", StringComparison.OrdinalIgnoreCase))
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
    }

    private static void ExtractGenres(JsonElement root, MediaMetadata metadata)
    {
        if (root.TryGetProperty("genres", out var genresArray) && genresArray.ValueKind == JsonValueKind.Array)
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
                return ArrTestResult.Ok($"Successfully connected to Readarr at {normalizedUrl}");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return ArrTestResult.Fail("Authentication failed (HTTP 401 Unauthorized). Please check your API key.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ArrTestResult.Fail($"Endpoint not found (HTTP 404 Not Found) at {normalizedUrl}. Verify the URL and port.");
            }

            return ArrTestResult.Fail($"Readarr returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Readarr connection test failed: {0}", ex.Message);
            return ArrTestResult.Fail($"Network error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            _logger.Error("Readarr connection test timed out");
            return ArrTestResult.Fail($"Connection timed out connecting to {normalizedUrl} (exceeded 10s)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Readarr connection test failed");
            return ArrTestResult.Fail($"Connection failed: {ex.Message}");
        }
    }
}
