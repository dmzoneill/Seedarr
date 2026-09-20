using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

    public const int DefaultTtlMinutes = 15;
    public const int MinimumPollingIntervalMinutes = 10;

    public string Name => "Torznab";
    public string IndexerType => "Torznab";

    public static int ParseTtl(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return DefaultTtlMinutes;
        }

        try
        {
            var doc = new XmlDocument();
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 1024
            };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            doc.Load(reader);
            return ParseTtl(doc);
        }
        catch (XmlException)
        {
            return DefaultTtlMinutes;
        }
    }

    public static int ParseTtl(XmlDocument doc)
    {
        if (doc == null)
        {
            return DefaultTtlMinutes;
        }

        var ttlNode = doc.SelectSingleNode("//channel/ttl") ?? doc.SelectSingleNode("//*[local-name()='ttl']");
        if (ttlNode != null && !string.IsNullOrWhiteSpace(ttlNode.InnerText))
        {
            if (int.TryParse(ttlNode.InnerText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ttl) && ttl > 0)
            {
                return ttl;
            }
        }

        return DefaultTtlMinutes;
    }

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

    private static readonly ConcurrentDictionary<string, TorznabCapabilities> CapabilitiesCache = new();

    public static TorznabCapabilities GetCachedCapabilities(IndexerDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        if (definition.Capabilities != null)
        {
            return definition.Capabilities;
        }

        if (definition.Id > 0 && CapabilitiesCache.TryGetValue($"id:{definition.Id}", out var capsById))
        {
            return capsById;
        }

        if (!string.IsNullOrWhiteSpace(definition.Url) && CapabilitiesCache.TryGetValue($"url:{definition.Url.TrimEnd('/')}", out var capsByUrl))
        {
            return capsByUrl;
        }

        return null;
    }

    public static void CacheCapabilities(IndexerDefinition definition, TorznabCapabilities caps)
    {
        if (definition == null || caps == null)
        {
            return;
        }

        if (definition.Id > 0)
        {
            CapabilitiesCache[$"id:{definition.Id}"] = caps;
        }

        if (!string.IsNullOrWhiteSpace(definition.Url))
        {
            CapabilitiesCache[$"url:{definition.Url.TrimEnd('/')}"] = caps;
        }
    }

    public static void ClearCapabilitiesCache()
    {
        CapabilitiesCache.Clear();
    }

    public TorznabIndexer(HttpClient httpClient = null, IIndexerStatusService indexerStatusService = null)
    {
        _httpClient = httpClient ?? DefaultClient;
        _indexerStatusService = indexerStatusService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public static string MapFriendlyCategory(string category)
    {
        return MapFriendlyCategory(category, (TorznabCapabilities)null);
    }

    public static string MapFriendlyCategory(string category, TorznabCapabilities capabilities)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        var trimmed = category.Trim();
        if (capabilities != null && capabilities.Categories != null && capabilities.Categories.Count > 0)
        {
            if (int.TryParse(trimmed, out var numericCat))
            {
                var foundCat = capabilities.Categories.FirstOrDefault(c => c.Id == numericCat);
                if (foundCat != null && foundCat.Subcategories != null && foundCat.Subcategories.Count > 0)
                {
                    var ids = new List<int> { foundCat.Id };
                    ids.AddRange(foundCat.Subcategories.Select(s => s.Id));
                    return string.Join(",", ids.Distinct());
                }

                var foundSub = capabilities.Categories.SelectMany(c => c.Subcategories ?? Enumerable.Empty<TorznabSubcategory>()).FirstOrDefault(s => s.Id == numericCat);
                if (foundSub != null)
                {
                    return foundSub.Id.ToString(CultureInfo.InvariantCulture);
                }

                if (CategoryHierarchy.TryGetValue(numericCat, out var standardSubcats))
                {
                    return string.Join(",", standardSubcats);
                }

                return trimmed;
            }

            if (trimmed.Contains(','))
            {
                var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var mappedList = new List<string>();
                foreach (var part in parts)
                {
                    var mapped = MapFriendlyCategory(part, capabilities);
                    if (!string.IsNullOrEmpty(mapped))
                    {
                        mappedList.Add(mapped);
                    }
                }

                return mappedList.Count > 0 ? string.Join(",", mappedList.Distinct()) : null;
            }

            var lower = trimmed.ToLowerInvariant();
            var matched = capabilities.Categories.Where(c =>
                c.Name.Equals(lower, StringComparison.OrdinalIgnoreCase) ||
                ((lower == "movies" || lower == "movie") && (c.Name.Contains("movie", StringComparison.OrdinalIgnoreCase) || c.Id == 2000)) ||
                ((lower == "tv" || lower == "television" || lower == "series" || lower == "shows" || lower == "show") && (c.Name.Contains("tv", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("series", StringComparison.OrdinalIgnoreCase) || c.Id == 5000)) ||
                ((lower == "music" || lower == "audio") && (c.Name.Contains("music", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("audio", StringComparison.OrdinalIgnoreCase) || c.Id == 3000)) ||
                ((lower == "apps" || lower == "software" || lower == "pc") && (c.Name.Contains("app", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("pc", StringComparison.OrdinalIgnoreCase) || c.Id == 4000)) ||
                ((lower == "games" || lower == "game" || lower == "console") && (c.Name.Contains("game", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("console", StringComparison.OrdinalIgnoreCase) || c.Id == 1000)) ||
                ((lower == "books" || lower == "book" || lower == "ebook" || lower == "ebooks") && (c.Name.Contains("book", StringComparison.OrdinalIgnoreCase) || c.Id == 7000)) ||
                (lower == "anime" && (c.Name.Contains("anime", StringComparison.OrdinalIgnoreCase) || (c.Subcategories != null && c.Subcategories.Any(s => s.Id == 5070 || s.Name.Contains("anime", StringComparison.OrdinalIgnoreCase))))) ||
                ((lower == "xxx" || lower == "adult") && (c.Name.Contains("xxx", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("adult", StringComparison.OrdinalIgnoreCase) || c.Id == 6000)) ||
                ((lower == "other" || lower == "misc") && (c.Name.Contains("other", StringComparison.OrdinalIgnoreCase) || c.Id == 8000))).ToList();

            if (matched.Count > 0)
            {
                var allIds = new List<int>();
                foreach (var m in matched)
                {
                    allIds.Add(m.Id);
                    if (m.Subcategories != null)
                    {
                        allIds.AddRange(m.Subcategories.Select(s => s.Id));
                    }
                }

                return string.Join(",", allIds.Distinct());
            }
        }

        if (int.TryParse(trimmed, out var numericCategory))
        {
            if (CategoryHierarchy.TryGetValue(numericCategory, out var subcats))
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

    public TorznabCapabilities GetCapabilities(IndexerDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Url))
        {
            return new TorznabCapabilities();
        }

        var cached = GetCachedCapabilities(definition);
        if (cached != null)
        {
            return cached;
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
            if (response.IsSuccessStatusCode)
            {
                using var stream = response.Content.ReadAsStream();
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                var caps = TorznabCapsParser.Parse(content);
                if (caps != null)
                {
                    definition.Capabilities = caps;
                    CacheCapabilities(definition, caps);
                    return caps;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to retrieve capabilities from Torznab at {0}", definition.Url);
        }

        return new TorznabCapabilities();
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
                TorznabCapabilities capabilities = null;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    capabilities = TorznabCapsParser.Parse(content);
                    if (capabilities != null)
                    {
                        if (definition != null)
                        {
                            definition.Capabilities = capabilities;
                        }

                        CacheCapabilities(definition, capabilities);
                    }
                }

                return new IndexerTestResult
                {
                    Success = true,
                    Message = $"Successfully connected to Torznab indexer at {definition.Url}",
                    Capabilities = capabilities
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

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                TimeSpan? retryAfter = null;
                if (response.Headers.RetryAfter != null)
                {
                    if (response.Headers.RetryAfter.Delta.HasValue)
                    {
                        retryAfter = response.Headers.RetryAfter.Delta.Value;
                    }
                    else if (response.Headers.RetryAfter.Date.HasValue)
                    {
                        var diff = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                        retryAfter = diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
                    }
                }

                if (!retryAfter.HasValue && response.Headers.TryGetValues("Retry-After", out var rawValues))
                {
                    retryAfter = IndexerStatusService.ParseRetryAfter(string.Join(",", rawValues));
                }

                return new IndexerTestResult
                {
                    Success = false,
                    Message = $"Torznab returned HTTP 429 Too Many Requests.",
                    StatusCode = 429,
                    RetryAfter = retryAfter
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

        var caps = definition?.Capabilities ?? GetCachedCapabilities(definition);
        var mappedCat = MapFriendlyCategory(searchQuery.Category, caps);
        if (!string.IsNullOrWhiteSpace(mappedCat))
        {
            queryParams.Add($"cat={Uri.EscapeDataString(mappedCat)}");
        }
        else if (!string.IsNullOrWhiteSpace(definition.Categories))
        {
            var defCat = MapFriendlyCategory(definition.Categories, caps);
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
                TimeSpan? retryAfter = null;
                if (response.Headers.RetryAfter != null)
                {
                    if (response.Headers.RetryAfter.Delta.HasValue)
                    {
                        retryAfter = response.Headers.RetryAfter.Delta.Value;
                    }
                    else if (response.Headers.RetryAfter.Date.HasValue)
                    {
                        var diff = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                        retryAfter = diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
                    }
                }

                if (!retryAfter.HasValue && response.Headers.TryGetValues("Retry-After", out var rawValues))
                {
                    retryAfter = IndexerStatusService.ParseRetryAfter(string.Join(",", rawValues));
                }

                if (retryAfter.HasValue)
                {
                    ex.Data["RetryAfter"] = retryAfter.Value;
                }

                if (definition != null && definition.Id > 0 && _indexerStatusService != null)
                {
                    _indexerStatusService.RecordFailure(definition.Id, (int)response.StatusCode, ex.Message, ex, retryAfter);
                    ex.Data["Recorded"] = true;
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
            var retryAfter = IndexerStatusService.ParseRetryAfter(desc);
            if (definition != null && definition.Id > 0)
            {
                _indexerStatusService?.RecordFailure(definition.Id, statusCode, errorMsg, retryAfter: retryAfter);
            }

            throw new IndexerException(errorMsg, code, statusCode, retryAfter) { Recorded = _indexerStatusService != null && definition != null && definition.Id > 0 };
        }

        var ttlMinutes = ParseTtl(doc);
        if (definition != null && definition.Id > 0)
        {
            _indexerStatusService?.RecordRssSync(definition.Id, ttlMinutes);
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
                        else if ((name == "imdbid" || name == "imdb") && !string.IsNullOrWhiteSpace(val))
                        {
                            release.ImdbId = val;
                        }
                        else if ((name == "tmdbid" || name == "tmdb") && int.TryParse(val, out var tmdb))
                        {
                            release.TmdbId = tmdb;
                        }
                        else if ((name == "tvdbid" || name == "tvdb") && int.TryParse(val, out var tvdb))
                        {
                            release.TvdbId = tvdb;
                        }
                        else if (name == "resolution" && !string.IsNullOrWhiteSpace(val))
                        {
                            release.Resolution = val.Trim();
                        }
                        else if ((name == "video" || name == "videoquality" || name == "videocodec") && !string.IsNullOrWhiteSpace(val))
                        {
                            release.VideoCodec = val.Trim();
                        }
                        else if ((name == "audio" || name == "audiocodec") && !string.IsNullOrWhiteSpace(val))
                        {
                            release.AudioCodec = val.Trim();
                        }
                        else if (name == "minimumratio")
                        {
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minRatio)
                                && double.IsFinite(minRatio) && minRatio >= 0.0)
                            {
                                release.MinimumRatio = minRatio;
                            }
                        }
                        else if (name == "minimumseedtime")
                        {
                            if (long.TryParse(val, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var minSeedTime)
                                && minSeedTime >= 0)
                            {
                                release.MinimumSeedTime = minSeedTime;
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
