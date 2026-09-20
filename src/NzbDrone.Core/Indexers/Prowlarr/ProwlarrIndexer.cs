using System;
using System.Collections.Generic;
using System.Net.Http;
using NLog;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Prowlarr;

public class ProwlarrIndexer : IIndexer
{
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public string Name => "Prowlarr";
    public string IndexerType => "Prowlarr";

    public ProwlarrIndexer(HttpClient httpClient = null)
    {
        _httpClient = httpClient ?? DefaultClient;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool TestConnection(IndexerDefinition definition)
    {
        return TestConnectionDetailed(definition).Success;
    }

    public IndexerTestResult TestConnectionDetailed(IndexerDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Url))
        {
            return new IndexerTestResult { Success = false, Message = "URL is required." };
        }

        try
        {
            var url = $"{definition.Url.TrimEnd('/')}/api/v1/health";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(definition.ApiKey))
            {
                request.Headers.Add("X-Api-Key", definition.ApiKey);
            }

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = _httpClient.Send(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                return new IndexerTestResult
                {
                    Success = true,
                    Message = $"Successfully connected to Prowlarr at {definition.Url}"
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new IndexerTestResult
                {
                    Success = false,
                    Message = "Authentication failed: Invalid API Key.",
                    StatusCode = (int)response.StatusCode
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new IndexerTestResult
                {
                    Success = false,
                    Message = $"Prowlarr health endpoint not found at {url}. Please verify the host and port.",
                    StatusCode = (int)response.StatusCode
                };
            }

            return new IndexerTestResult
            {
                Success = false,
                Message = $"Prowlarr returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                StatusCode = (int)response.StatusCode
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Failed to test Prowlarr connection at {0}", definition.Url);
            return new IndexerTestResult
            {
                Success = false,
                Message = $"Unable to connect to Prowlarr at {definition.Url}: {ex.Message}",
                StatusCode = (int?)ex.StatusCode
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to test Prowlarr connection at {0}", definition.Url);
            return new IndexerTestResult
            {
                Success = false,
                Message = $"Unable to connect to Prowlarr at {definition.Url}: {ex.Message}"
            };
        }
    }

    public byte[] FetchTorrentByHash(IndexerDefinition definition, string infoHash)
    {
        try
        {
            var url = $"{definition.Url.TrimEnd('/')}/api/v1/search?query={infoHash}&type=search";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(definition.ApiKey))
            {
                request.Headers.Add("X-Api-Key", definition.ApiKey);
            }

            using var response = _httpClient.Send(request);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = response.Content.ReadAsStream();
            using var document = System.Text.Json.JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("downloadUrl", out var downloadUrlProp) && downloadUrlProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var downloadUrl = downloadUrlProp.GetString();
                        if (!string.IsNullOrEmpty(downloadUrl) && !downloadUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!UrlValidator.IsSafeUrl(downloadUrl))
                            {
                                _logger.Warn("Unsafe download URL detected in Prowlarr search result: {0}", downloadUrl);
                                continue;
                            }

                            using var dlRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                            if (!string.IsNullOrWhiteSpace(definition.ApiKey) &&
                                Uri.TryCreate(downloadUrl, UriKind.Absolute, out var dlUri) &&
                                !string.IsNullOrWhiteSpace(definition.Url) &&
                                Uri.TryCreate(definition.Url, UriKind.Absolute, out var indUri) &&
                                string.Equals(dlUri.Host, indUri.Host, StringComparison.OrdinalIgnoreCase))
                            {
                                dlRequest.Headers.Add("X-Api-Key", definition.ApiKey);
                            }

                            using var dlResponse = _httpClient.Send(dlRequest);
                            if (dlResponse.IsSuccessStatusCode)
                            {
                                using var ms = new System.IO.MemoryStream();
                                dlResponse.Content.ReadAsStream().CopyTo(ms);
                                return ms.ToArray();
                            }
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch torrent by hash from Prowlarr at {0}", definition.Url);
            return null;
        }
    }

    public List<ReleaseInfo> Search(IndexerDefinition definition, TorznabSearchCriteria criteria)
    {
        return Search(definition, criteria?.ToSearchQuery());
    }

    public List<ReleaseInfo> Search(IndexerDefinition definition, SearchQuery searchQuery)
    {
        if (definition == null || searchQuery == null)
        {
            return new List<ReleaseInfo>();
        }

        var mode = searchQuery.Mode;
        if (mode == SearchMode.Default)
        {
            if (searchQuery.Season.HasValue || searchQuery.Episode.HasValue || !string.IsNullOrWhiteSpace(searchQuery.TvdbId) || !string.IsNullOrWhiteSpace(searchQuery.Rid))
            {
                mode = SearchMode.TvSearch;
            }
            else if (!string.IsNullOrWhiteSpace(searchQuery.ImdbId) || !string.IsNullOrWhiteSpace(searchQuery.TmdbId))
            {
                mode = SearchMode.Movie;
            }
            else if (!string.IsNullOrWhiteSpace(searchQuery.Artist) || !string.IsNullOrWhiteSpace(searchQuery.Album))
            {
                mode = SearchMode.Music;
            }
            else if (!string.IsNullOrWhiteSpace(searchQuery.Author))
            {
                mode = SearchMode.Book;
            }
        }

        var query = !string.IsNullOrWhiteSpace(searchQuery.Query) ? searchQuery.Query : searchQuery.Title;
        if (string.IsNullOrWhiteSpace(query))
        {
            if (!string.IsNullOrWhiteSpace(searchQuery.Artist))
            {
                query = !string.IsNullOrWhiteSpace(searchQuery.Album) ? $"{searchQuery.Artist} {searchQuery.Album}" : searchQuery.Artist;
            }
            else if (!string.IsNullOrWhiteSpace(searchQuery.Author))
            {
                query = searchQuery.Author;
            }
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<ReleaseInfo>();
        }

        if (mode == SearchMode.TvSearch && searchQuery.Season.HasValue)
        {
            if (searchQuery.Episode.HasValue)
            {
                query = $"{query} S{searchQuery.Season.Value:D2}E{searchQuery.Episode.Value:D2}";
            }
            else
            {
                query = $"{query} S{searchQuery.Season.Value:D2}";
            }
        }

        var cat = !string.IsNullOrWhiteSpace(searchQuery.Category) ? searchQuery.Category : searchQuery.Categories;
        return Search(definition, query, cat, searchQuery.Offset, searchQuery.Limit);
    }

    public List<ReleaseInfo> Search(IndexerDefinition definition, string query, string category = null, int offset = 0, int limit = 50)
    {
        var results = new System.Collections.Generic.List<ReleaseInfo>();
        if (definition == null || string.IsNullOrWhiteSpace(definition.Url) || string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        try
        {
            var url = $"{definition.Url.TrimEnd('/')}/api/v1/search?query={Uri.EscapeDataString(query)}&type=search";
            if (!string.IsNullOrWhiteSpace(category))
            {
                url += $"&categories={Uri.EscapeDataString(category)}";
            }

            if (offset > 0)
            {
                url += $"&offset={offset}";
            }

            if (limit > 0)
            {
                url += $"&limit={limit}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(definition.ApiKey))
            {
                request.Headers.Add("X-Api-Key", definition.ApiKey);
            }

            using var response = _httpClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Prowlarr search returned status code {0}", response.StatusCode);
                var ex = new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
                if (response.Headers.RetryAfter != null)
                {
                    if (response.Headers.RetryAfter.Delta.HasValue)
                    {
                        ex.Data["RetryAfter"] = response.Headers.RetryAfter.Delta.Value;
                    }
                    else if (response.Headers.RetryAfter.Date.HasValue)
                    {
                        var diff = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                        ex.Data["RetryAfter"] = diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
                    }
                }

                throw ex;
            }

            using var searchStream = response.Content.ReadAsStream();
            using var document = System.Text.Json.JsonDocument.Parse(searchStream);

            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return results;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var release = new ReleaseInfo
                {
                    IndexerId = definition.Id,
                    Indexer = definition.Name
                };

                if (element.TryGetProperty("guid", out var guidProp) && guidProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    release.Guid = guidProp.GetString();
                }

                if (element.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    release.Title = titleProp.GetString();
                }

                if (element.TryGetProperty("indexer", out var indexerProp) && indexerProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    release.Indexer = indexerProp.GetString();
                }

                if (element.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var sizeVal))
                {
                    release.Size = sizeVal;
                }

                if (element.TryGetProperty("seeders", out var seedersProp) && seedersProp.TryGetInt32(out var seedersVal))
                {
                    release.Seeders = seedersVal;
                }

                if (element.TryGetProperty("leechers", out var leechersProp) && leechersProp.TryGetInt32(out var leechersVal))
                {
                    release.Leechers = leechersVal;
                }

                if (element.TryGetProperty("publishDate", out var pubDateProp) && pubDateProp.TryGetDateTime(out var pubDateVal))
                {
                    release.PublishDate = pubDateVal;
                }

                if (element.TryGetProperty("downloadUrl", out var dlProp) && dlProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    release.DownloadUrl = dlProp.GetString();
                }

                if (element.TryGetProperty("magnetUrl", out var magProp) && magProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    release.MagnetUrl = magProp.GetString();
                }

                if (element.TryGetProperty("infoHash", out var hashProp) && hashProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    release.InfoHash = hashProp.GetString();
                }

                if (element.TryGetProperty("protocol", out var protoProp) && protoProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    release.Protocol = protoProp.GetString();
                }

                if (!string.IsNullOrWhiteSpace(release.Title))
                {
                    results.Add(release);
                }
            }
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to search Prowlarr at {0} for query '{1}'", definition.Url, query);
        }

        return results;
    }
}
