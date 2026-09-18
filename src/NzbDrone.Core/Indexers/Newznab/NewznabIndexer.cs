using System;
using System.IO;
using System.Net.Http;
using System.Xml;
using NLog;
using NzbDrone.Core.Indexers.Torznab;

namespace NzbDrone.Core.Indexers.Newznab;

public class NewznabIndexer : IIndexer
{
    private static readonly HttpClient DefaultClient = new();
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;
    private readonly IIndexerStatusService _indexerStatusService;

    public string Name => "Newznab";
    public string IndexerType => "Newznab";

    public NewznabIndexer(HttpClient httpClient = null, IIndexerStatusService indexerStatusService = null)
    {
        _httpClient = httpClient ?? DefaultClient;
        _indexerStatusService = indexerStatusService;
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
            var apiPath = string.IsNullOrEmpty(definition.ApiPath) ? "/api" : definition.ApiPath;
            var url = $"{definition.Url.TrimEnd('/')}{apiPath}?t=caps";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(definition.ApiKey))
            {
                request.Headers.Add("X-Api-Key", definition.ApiKey);
            }

            using var response = _httpClient.Send(request);

            string content = null;
            if (response.Content != null)
            {
                using var stream = response.Content.ReadAsStream();
                using var reader = new StreamReader(stream);
                content = reader.ReadToEnd();
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    var doc = new XmlDocument();
                    var settings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        MaxCharactersFromEntities = 1024
                    };
                    using var xmlReader = XmlReader.Create(new StringReader(content), settings);
                    doc.Load(xmlReader);

                    var errorNode = doc.SelectSingleNode("//error") ?? doc.SelectSingleNode("//*[local-name()='error']");
                    if (errorNode != null)
                    {
                        var code = errorNode.Attributes?["code"]?.Value ?? "unknown";
                        var desc = errorNode.Attributes?["description"]?.Value ?? (string.IsNullOrWhiteSpace(errorNode.InnerText) ? "Unknown indexer error" : errorNode.InnerText.Trim());
                        return new IndexerTestResult
                        {
                            Success = false,
                            Message = $"Indexer authentication/access error (code {code}): {desc}",
                            StatusCode = int.TryParse(code, out var c) ? c : (int)response.StatusCode
                        };
                    }
                }
                catch (XmlException)
                {
                }
            }

            if (response.IsSuccessStatusCode)
            {
                return new IndexerTestResult
                {
                    Success = true,
                    Message = $"Successfully connected to Newznab indexer at {definition.Url}"
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

            return new IndexerTestResult
            {
                Success = false,
                Message = $"Newznab returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                StatusCode = (int)response.StatusCode
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Failed to test Newznab connection at {0}", definition.Url);
            return new IndexerTestResult
            {
                Success = false,
                Message = $"Unable to connect to Newznab at {definition.Url}: {ex.Message}",
                StatusCode = (int?)ex.StatusCode
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to test Newznab connection at {0}", definition.Url);
            return new IndexerTestResult
            {
                Success = false,
                Message = $"Unable to connect to Newznab at {definition.Url}: {ex.Message}"
            };
        }
    }

    public byte[] FetchTorrentByHash(IndexerDefinition definition, string infoHash)
    {
        // Newznab is typically Usenet and doesn't support infohash searches,
        // so we just return null to fallback/skip.
        return null;
    }

    public string BuildSearchUrl(IndexerDefinition definition, SearchQuery searchQuery)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Url))
        {
            return string.Empty;
        }

        searchQuery ??= new SearchQuery();
        var apiPath = string.IsNullOrEmpty(definition.ApiPath) ? "/api" : definition.ApiPath;
        var baseUrl = $"{definition.Url.TrimEnd('/')}{apiPath}";

        string tParam;
        var queryParams = new System.Collections.Generic.List<string>();

        switch (searchQuery.Mode)
        {
            case SearchMode.TvSearch:
                tParam = "tvsearch";
                if (!string.IsNullOrWhiteSpace(searchQuery.Query))
                {
                    queryParams.Add($"q={Uri.EscapeDataString(searchQuery.Query)}");
                }

                if (searchQuery.Season.HasValue)
                {
                    queryParams.Add($"season={searchQuery.Season.Value}");
                }

                if (searchQuery.Episode.HasValue)
                {
                    queryParams.Add($"ep={searchQuery.Episode.Value}");
                }

                if (!string.IsNullOrWhiteSpace(searchQuery.TvdbId))
                {
                    queryParams.Add($"tvdbid={Uri.EscapeDataString(searchQuery.TvdbId)}");
                }

                if (!string.IsNullOrWhiteSpace(searchQuery.Rid))
                {
                    queryParams.Add($"rid={Uri.EscapeDataString(searchQuery.Rid)}");
                }

                if (!string.IsNullOrWhiteSpace(searchQuery.ImdbId))
                {
                    queryParams.Add($"imdbid={Uri.EscapeDataString(searchQuery.ImdbId)}");
                }

                break;

            case SearchMode.Movie:
                tParam = "movie";
                if (!string.IsNullOrWhiteSpace(searchQuery.Query))
                {
                    queryParams.Add($"q={Uri.EscapeDataString(searchQuery.Query)}");
                }

                if (!string.IsNullOrWhiteSpace(searchQuery.ImdbId))
                {
                    queryParams.Add($"imdbid={Uri.EscapeDataString(searchQuery.ImdbId)}");
                }

                if (!string.IsNullOrWhiteSpace(searchQuery.TmdbId))
                {
                    queryParams.Add($"tmdbid={Uri.EscapeDataString(searchQuery.TmdbId)}");
                }

                break;

            case SearchMode.Music:
                tParam = "music";
                if (!string.IsNullOrWhiteSpace(searchQuery.Query))
                {
                    queryParams.Add($"q={Uri.EscapeDataString(searchQuery.Query)}");
                }

                if (!string.IsNullOrWhiteSpace(searchQuery.Artist))
                {
                    queryParams.Add($"artist={Uri.EscapeDataString(searchQuery.Artist)}");
                }

                if (!string.IsNullOrWhiteSpace(searchQuery.Album))
                {
                    queryParams.Add($"album={Uri.EscapeDataString(searchQuery.Album)}");
                }

                break;

            case SearchMode.Book:
                tParam = "book";
                if (!string.IsNullOrWhiteSpace(searchQuery.Title))
                {
                    queryParams.Add($"title={Uri.EscapeDataString(searchQuery.Title)}");
                }
                else if (!string.IsNullOrWhiteSpace(searchQuery.Query))
                {
                    queryParams.Add($"title={Uri.EscapeDataString(searchQuery.Query)}");
                }

                if (!string.IsNullOrWhiteSpace(searchQuery.Author))
                {
                    queryParams.Add($"author={Uri.EscapeDataString(searchQuery.Author)}");
                }

                break;

            default:
                tParam = "search";
                if (!string.IsNullOrWhiteSpace(searchQuery.Query))
                {
                    queryParams.Add($"q={Uri.EscapeDataString(searchQuery.Query)}");
                }

                break;
        }

        var mappedCat = TorznabIndexer.MapFriendlyCategory(searchQuery.Category);
        if (!string.IsNullOrWhiteSpace(mappedCat))
        {
            queryParams.Add($"cat={Uri.EscapeDataString(mappedCat)}");
        }
        else if (!string.IsNullOrWhiteSpace(definition.Categories))
        {
            var defCat = TorznabIndexer.MapFriendlyCategory(definition.Categories);
            if (!string.IsNullOrWhiteSpace(defCat))
            {
                queryParams.Add($"cat={Uri.EscapeDataString(defCat)}");
            }
        }

        if (searchQuery.Offset > 0)
        {
            queryParams.Add($"offset={searchQuery.Offset}");
        }

        if (searchQuery.Limit > 0)
        {
            queryParams.Add($"limit={searchQuery.Limit}");
        }

        if (!string.IsNullOrWhiteSpace(definition.ApiKey))
        {
            queryParams.Add($"apikey={Uri.EscapeDataString(definition.ApiKey)}");
        }

        var queryString = string.Join("&", queryParams);
        return string.IsNullOrEmpty(queryString) ? $"{baseUrl}?t={tParam}" : $"{baseUrl}?t={tParam}&{queryString}";
    }

    public System.Collections.Generic.List<ReleaseInfo> Search(IndexerDefinition definition, string query, string category = null, int offset = 0, int limit = 50)
    {
        return Search(definition, new SearchQuery
        {
            Query = query,
            Category = category,
            Offset = offset,
            Limit = limit,
        });
    }

    public System.Collections.Generic.List<ReleaseInfo> Search(IndexerDefinition definition, SearchQuery searchQuery)
    {
        var results = new System.Collections.Generic.List<ReleaseInfo>();
        if (definition == null || string.IsNullOrWhiteSpace(definition.Url) || searchQuery == null)
        {
            return results;
        }

        if (searchQuery.Mode == SearchMode.Default &&
            string.IsNullOrWhiteSpace(searchQuery.Query) &&
            string.IsNullOrWhiteSpace(searchQuery.ImdbId) &&
            string.IsNullOrWhiteSpace(searchQuery.TvdbId))
        {
            return results;
        }

        try
        {
            var url = BuildSearchUrl(definition, searchQuery);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(definition.ApiKey))
            {
                request.Headers.Add("X-Api-Key", definition.ApiKey);
            }

            using var response = _httpClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Newznab search returned status code {0}", response.StatusCode);
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

            using var reader = new System.IO.StreamReader(response.Content.ReadAsStream());
            var xml = reader.ReadToEnd();
            return ParseResponse(xml, definition);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (IndexerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to search Newznab at {0} for query '{1}'", definition.Url, searchQuery.Query);
        }

        return results;
    }

    public System.Collections.Generic.List<ReleaseInfo> ParseResponse(string xml, IndexerDefinition definition = null)
    {
        var results = new System.Collections.Generic.List<ReleaseInfo>();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return results;
        }

        if (xml.Contains("&nbsp;"))
        {
            xml = xml.Replace("&nbsp;", "&#160;");
        }

        var doc = new XmlDocument();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024
        };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        doc.Load(reader);

        var errorNode = doc.SelectSingleNode("//error") ?? doc.SelectSingleNode("//*[local-name()='error']");
        if (errorNode != null)
        {
            var code = errorNode.Attributes?["code"]?.Value ?? "unknown";
            var desc = errorNode.Attributes?["description"]?.Value ?? (string.IsNullOrWhiteSpace(errorNode.InnerText) ? "Unknown indexer error" : errorNode.InnerText.Trim());
            var errorMsg = $"Indexer authentication/access error (code {code}): {desc}";
            _logger.Warn("Newznab returned error: {0}", errorMsg);

            int? statusCode = int.TryParse(code, out var c) ? c : null;
            if (definition != null && definition.Id > 0)
            {
                _indexerStatusService?.RecordFailure(definition.Id, statusCode, errorMsg);
            }

            throw new IndexerException(errorMsg, code, statusCode) { Recorded = _indexerStatusService != null && definition != null && definition.Id > 0 };
        }

        int? responseOffset = null;
        int? responseTotal = null;

        var responseNode = doc.SelectSingleNode("//*[local-name()='response']");
        if (responseNode != null)
        {
            if (responseNode.Attributes?["offset"] != null && int.TryParse(responseNode.Attributes["offset"].Value, out var offsetVal))
            {
                responseOffset = offsetVal;
            }

            if (responseNode.Attributes?["total"] != null && int.TryParse(responseNode.Attributes["total"].Value, out var totalVal))
            {
                responseTotal = totalVal;
            }
        }

        var items = doc.SelectNodes("//item");
        if (items != null)
        {
            foreach (System.Xml.XmlNode item in items)
            {
                var titleNode = item.SelectSingleNode("title");
                var descriptionNode = item.SelectSingleNode("description");
                var commentsNode = item.SelectSingleNode("comments") ?? item.SelectSingleNode("details");
                var linkNode = item.SelectSingleNode("link");
                var enclosureNode = item.SelectSingleNode("enclosure");
                var sizeNode = item.SelectSingleNode("size");
                var pubDateNode = item.SelectSingleNode("pubDate");
                var guidNode = item.SelectSingleNode("guid");

                var downloadUrl = enclosureNode?.Attributes?["url"]?.Value ?? linkNode?.InnerText;

                var rawTitle = titleNode?.InnerText ?? string.Empty;
                var decodedTitle = TorznabIndexer.DecodeAndNormalize(rawTitle);

                var rawDescription = descriptionNode?.InnerText;
                var decodedDescription = rawDescription != null ? TorznabIndexer.DecodeAndNormalize(rawDescription) : null;

                var rawComments = commentsNode?.InnerText;
                var decodedComments = rawComments != null ? TorznabIndexer.DecodeAndNormalize(rawComments) : null;

                var release = new ReleaseInfo
                {
                    Guid = guidNode?.InnerText ?? string.Empty,
                    IndexerId = definition?.Id ?? 0,
                    Indexer = definition?.Name,
                    Title = decodedTitle,
                    Description = decodedDescription,
                    Comments = decodedComments,
                    DownloadUrl = downloadUrl,
                    ResponseOffset = responseOffset,
                    ResponseTotal = responseTotal,
                    DownloadVolumeFactor = 1.0,
                    UploadVolumeFactor = 1.0,
                    Protocol = "usenet"
                };

                if (enclosureNode?.Attributes?["length"] != null && long.TryParse(enclosureNode.Attributes["length"].Value, out var encSize))
                {
                    release.Size = encSize;
                }
                else if (sizeNode != null && long.TryParse(sizeNode.InnerText, out var sVal))
                {
                    release.Size = sVal;
                }

                if (pubDateNode != null && DateTime.TryParse(pubDateNode.InnerText, out var pDate))
                {
                    release.PublishDate = pDate;
                }

                var attrNodes = item.SelectNodes("*[local-name()='attr']");
                if (attrNodes != null)
                {
                    foreach (System.Xml.XmlNode attr in attrNodes)
                    {
                        var name = attr.Attributes?["name"]?.Value?.ToLowerInvariant();
                        var val = attr.Attributes?["value"]?.Value;
                        if (name == "category" && !string.IsNullOrEmpty(val))
                        {
                            release.Categories.Add(val);
                        }
                        else if (name == "size" && release.Size == 0 && long.TryParse(val, out var aSize))
                        {
                            release.Size = aSize;
                        }
                        else if (name == "guid" && string.IsNullOrEmpty(release.Guid))
                        {
                            release.Guid = val;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(release.Title))
                {
                    results.Add(release);
                }
            }
        }

        return results;
    }

    public static string DecodeAndNormalize(string input)
    {
        return TorznabIndexer.DecodeAndNormalize(input);
    }
}
