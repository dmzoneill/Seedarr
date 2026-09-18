using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml;
using NLog;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Torznab;

public class TorznabIndexer : IIndexer
{
    private static readonly HttpClient DefaultClient = new();
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;
    private readonly IIndexerStatusService _indexerStatusService;

    public string Name => "Torznab";
    public string IndexerType => "Torznab";

    public static readonly Dictionary<int, List<int>> CategoryHierarchy = new()
    {
        { 1000, new List<int> { 1000, 1010, 1020, 1030, 1040, 1050, 1060, 1070, 1080, 1090, 1110, 1120, 1130, 1140, 1150, 1180 } }, // Console / Games
        { 2000, new List<int> { 2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060, 2070, 2080, 2090 } }, // Movies
        { 3000, new List<int> { 3000, 3010, 3020, 3030, 3040, 3050, 3060 } }, // Audio / Music
        { 4000, new List<int> { 4000, 4010, 4020, 4030, 4040, 4050, 4060, 4070 } }, // PC / Apps
        { 5000, new List<int> { 5000, 5010, 5020, 5030, 5040, 5045, 5050, 5060, 5070, 5080 } }, // TV
        { 6000, new List<int> { 6000, 6010, 6020, 6030, 6040, 6050, 6060, 6070, 6080, 6090 } }, // XXX
        { 7000, new List<int> { 7000, 7010, 7020, 7030, 7040, 7050, 7060 } }, // Books
        { 8000, new List<int> { 8000, 8010, 8020 } } // Other
    };

    public TorznabIndexer(HttpClient httpClient = null, IIndexerStatusService indexerStatusService = null)
    {
        _httpClient = httpClient ?? DefaultClient;
        _indexerStatusService = indexerStatusService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public static string MapFriendlyCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        var trimmed = category.Trim();
        if (int.TryParse(trimmed, out var numericCat))
        {
            if (CategoryHierarchy.TryGetValue(numericCat, out var subcats))
            {
                return string.Join(",", subcats);
            }

            return trimmed;
        }

        if (trimmed.Contains(','))
        {
            var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var mappedList = new List<string>();
            foreach (var part in parts)
            {
                var mapped = MapFriendlyCategory(part);
                if (!string.IsNullOrEmpty(mapped))
                {
                    mappedList.Add(mapped);
                }
            }

            return mappedList.Count > 0 ? string.Join(",", mappedList) : null;
        }

        return trimmed.ToLowerInvariant() switch
        {
            "movies" or "movie" => string.Join(",", CategoryHierarchy[2000]),
            "tv" or "television" or "series" or "shows" or "show" => string.Join(",", CategoryHierarchy[5000]),
            "music" or "audio" => string.Join(",", CategoryHierarchy[3000]),
            "apps" or "software" or "pc" => string.Join(",", CategoryHierarchy[4000]),
            "games" or "game" or "console" => string.Join(",", CategoryHierarchy[1000]),
            "books" or "book" or "ebook" or "ebooks" => string.Join(",", CategoryHierarchy[7000]),
            "anime" => "5070",
            "xxx" or "adult" => string.Join(",", CategoryHierarchy[6000]),
            "other" or "misc" => string.Join(",", CategoryHierarchy[8000]),
            _ => trimmed
        };
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
                    Message = $"Successfully connected to Torznab indexer at {definition.Url}"
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
                Message = $"Torznab returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                StatusCode = (int)response.StatusCode
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Failed to test Torznab connection at {0}", definition.Url);
            return new IndexerTestResult
            {
                Success = false,
                Message = $"Unable to connect to Torznab at {definition.Url}: {ex.Message}",
                StatusCode = (int?)ex.StatusCode
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to test Torznab connection at {0}", definition.Url);
            return new IndexerTestResult
            {
                Success = false,
                Message = $"Unable to connect to Torznab at {definition.Url}: {ex.Message}"
            };
        }
    }

    public byte[] FetchTorrentByHash(IndexerDefinition definition, string infoHash)
    {
        try
        {
            var apiPath = string.IsNullOrEmpty(definition.ApiPath) ? "/api" : definition.ApiPath;
            var url = $"{definition.Url.TrimEnd('/')}{apiPath}?t=search&infohash={infoHash}";

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
            var doc = new XmlDocument();
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 1024
            };
            using var reader = XmlReader.Create(stream, settings);
            doc.Load(reader);

            var enclosure = doc.SelectSingleNode("//item/enclosure");
            if (enclosure != null && enclosure.Attributes["url"] != null)
            {
                var downloadUrl = enclosure.Attributes["url"].Value;
                if (!string.IsNullOrEmpty(downloadUrl) && !downloadUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!UrlValidator.IsSafeUrl(downloadUrl))
                    {
                        _logger.Warn("Unsafe download URL detected in Torznab enclosure: {0}", downloadUrl);
                        return null;
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
                        using var ms = new MemoryStream();
                        dlResponse.Content.ReadAsStream().CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch torrent by hash from Torznab at {0}", definition.Url);
            return null;
        }
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

        var mappedCat = MapFriendlyCategory(searchQuery.Category);
        if (!string.IsNullOrWhiteSpace(mappedCat))
        {
            queryParams.Add($"cat={Uri.EscapeDataString(mappedCat)}");
        }
        else if (!string.IsNullOrWhiteSpace(definition.Categories))
        {
            var defCat = MapFriendlyCategory(definition.Categories);
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
                _logger.Warn("Torznab search returned status code {0}", response.StatusCode);
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
            _logger.Error(ex, "Failed to search Torznab at {0} for query '{1}'", definition.Url, searchQuery.Query);
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
            _logger.Warn("Torznab returned error: {0}", errorMsg);

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

                var downloadUrl = enclosureNode?.Attributes?["url"]?.Value ?? linkNode?.InnerText;
                var magnetUrl = string.Empty;

                if (!string.IsNullOrEmpty(downloadUrl) && downloadUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    magnetUrl = downloadUrl;
                }
                else if (linkNode != null && !string.IsNullOrEmpty(linkNode.InnerText) && linkNode.InnerText.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    magnetUrl = linkNode.InnerText;
                }

                var rawTitle = titleNode?.InnerText ?? string.Empty;
                var decodedTitle = DecodeAndNormalize(rawTitle);

                var rawDescription = descriptionNode?.InnerText;
                var decodedDescription = rawDescription != null ? DecodeAndNormalize(rawDescription) : null;

                var rawComments = commentsNode?.InnerText;
                var decodedComments = rawComments != null ? DecodeAndNormalize(rawComments) : null;

                var release = new ReleaseInfo
                {
                    IndexerId = definition?.Id ?? 0,
                    Indexer = definition?.Name,
                    Title = decodedTitle,
                    Description = decodedDescription,
                    Comments = decodedComments,
                    DownloadUrl = downloadUrl,
                    MagnetUrl = magnetUrl,
                    ResponseOffset = responseOffset,
                    ResponseTotal = responseTotal,
                    DownloadVolumeFactor = 1.0,
                    UploadVolumeFactor = 1.0,
                    Protocol = "torrent"
                };

                if (enclosureNode?.Attributes?["length"] != null && long.TryParse(enclosureNode.Attributes["length"].Value, out var encSize))
                {
                    release.Size = encSize;
                }
                else if (sizeNode != null && long.TryParse(sizeNode.InnerText, out var sVal))
                {
                    release.Size = sVal;
                }

                if (pubDateNode != null && !string.IsNullOrWhiteSpace(pubDateNode.InnerText))
                {
                    var dateText = pubDateNode.InnerText.Trim();
                    if (DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                    {
                        release.PublishDate = dto.UtcDateTime;
                    }
                    else
                    {
                        var commaIdx = dateText.IndexOf(',');
                        var stripped = commaIdx >= 0 ? dateText[(commaIdx + 1)..].Trim() : dateText;
                        if (DateTimeOffset.TryParse(stripped, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtoStripped))
                        {
                            release.PublishDate = dtoStripped.UtcDateTime;
                        }
                        else if (DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var pDate))
                        {
                            release.PublishDate = DateTime.SpecifyKind(pDate, DateTimeKind.Utc);
                        }
                    }
                }

                var freeleechNode = item.SelectSingleNode("*[local-name()='freeleech']");
                if (freeleechNode != null)
                {
                    var flVal = freeleechNode.InnerText;
                    var flAttr = freeleechNode.Attributes?["value"]?.Value;
                    if (IsFreeleechValue(flVal) || IsFreeleechValue(flAttr))
                    {
                        release.DownloadVolumeFactor = 0.0;
                    }
                }

                var attrNodes = item.SelectNodes("*[local-name()='attr']");
                if (attrNodes != null)
                {
                    int? rawPeers = null;
                    var explicitLeechers = false;

                    foreach (System.Xml.XmlNode attr in attrNodes)
                    {
                        var name = attr.Attributes?["name"]?.Value?.ToLowerInvariant();
                        var val = attr.Attributes?["value"]?.Value;
                        if (name == "seeders" && int.TryParse(val, out var seeds))
                        {
                            release.Seeders = Math.Max(0, seeds);
                        }
                        else if (name == "leechers" && int.TryParse(val, out var leechers))
                        {
                            release.Leechers = Math.Max(0, leechers);
                            explicitLeechers = true;
                        }
                        else if (name == "peers" && int.TryParse(val, out var peers))
                        {
                            rawPeers = Math.Max(0, peers);
                        }
                        else if (name == "infohash")
                        {
                            release.InfoHash = val;
                        }
                        else if (name == "magneturl")
                        {
                            release.MagnetUrl = val;
                        }
                        else if (name == "category" && !string.IsNullOrEmpty(val))
                        {
                            release.Categories.Add(val);
                        }
                        else if (name == "downloadvolumefactor")
                        {
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dvf)
                                && double.IsFinite(dvf) && dvf >= 0.0)
                            {
                                release.DownloadVolumeFactor = Math.Clamp(dvf, 0.0, 10.0);
                            }
                            else
                            {
                                release.DownloadVolumeFactor = null;
                            }
                        }
                        else if (name == "uploadvolumefactor")
                        {
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var uvf)
                                && double.IsFinite(uvf) && uvf >= 0.0)
                            {
                                release.UploadVolumeFactor = Math.Clamp(uvf, 0.0, 10.0);
                            }
                            else
                            {
                                release.UploadVolumeFactor = null;
                            }
                        }
                        else if (name == "freeleech")
                        {
                            if (IsFreeleechValue(val) || IsFreeleechValue(attr.InnerText))
                            {
                                release.DownloadVolumeFactor = 0.0;
                            }
                        }
                    }

                    if (!explicitLeechers && rawPeers.HasValue)
                    {
                        release.Leechers = Math.Max(0, rawPeers.Value - (release.Seeders ?? 0));
                    }
                }

                if (HasFreeleechTitleTag(release.Title))
                {
                    release.DownloadVolumeFactor = 0.0;
                }

                if (!string.IsNullOrWhiteSpace(release.Title))
                {
                    results.Add(release);
                }
            }
        }

        return results;
    }

    private static bool IsFreeleechValue(string val)
    {
        if (string.IsNullOrWhiteSpace(val))
        {
            return false;
        }

        var trimmed = val.Trim();
        return trimmed == "1"
            || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "free", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFreeleechTitleTag(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return title.IndexOf("[Freeleech]", StringComparison.OrdinalIgnoreCase) >= 0
            || title.IndexOf("[FL]", StringComparison.OrdinalIgnoreCase) >= 0
            || title.IndexOf("(Freeleech)", StringComparison.OrdinalIgnoreCase) >= 0
            || title.IndexOf("(FL)", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string DecodeAndNormalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var decoded = input;
        for (var i = 0; i < 2; i++)
        {
            var next = WebUtility.HtmlDecode(decoded);
            if (next.Contains("&apos;"))
            {
                next = next.Replace("&apos;", "'");
            }

            if (next == decoded)
            {
                break;
            }

            decoded = next;
        }

        if (decoded.Contains("&nbsp;"))
        {
            decoded = decoded.Replace("&nbsp;", " ");
        }

        decoded = decoded.Replace('\u00A0', ' ');

        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
