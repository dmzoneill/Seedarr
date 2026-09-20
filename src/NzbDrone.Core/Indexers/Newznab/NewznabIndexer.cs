using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml;
using NLog;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Indexers.Newznab;

public class NewznabIndexer : IIndexer
{
    private static readonly HttpClient DefaultClient = new();
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;
    private readonly IIndexerStatusService _indexerStatusService;
    private readonly IProxySettingsProvider _proxySettingsProvider;

    private readonly object _syncLock = new();
    private SocketsHttpHandler _proxyHandler;
    private HttpClient _proxyClient;
    private string _lastProxyHost;
    private int _lastProxyPort;
    private ProxyType _lastProxyType;
    private bool _lastProxyEnabled;

    internal HttpMessageHandler Handler
    {
        get
        {
            EnsureProxyClient();
            return _proxyHandler;
        }
    }

    internal HttpClient Client => GetHttpClient();

    public const int DefaultTtlMinutes = 15;
    public const int MinimumPollingIntervalMinutes = 10;

    public string Name => "Newznab";
    public string IndexerType => "Newznab";

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

    public NewznabIndexer(
        HttpClient httpClient = null,
        IIndexerStatusService indexerStatusService = null,
        IProxySettingsProvider proxySettingsProvider = null)
    {
        _httpClient = httpClient;
        _indexerStatusService = indexerStatusService;
        _proxySettingsProvider = proxySettingsProvider;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public NewznabIndexer(IProxySettingsProvider proxySettingsProvider, IIndexerStatusService indexerStatusService = null)
        : this(null, indexerStatusService, proxySettingsProvider)
    {
    }

    private HttpClient GetHttpClient()
    {
        if (_proxySettingsProvider != null && _proxySettingsProvider.IsEnabled)
        {
            EnsureProxyClient();
            lock (_syncLock)
            {
                return _proxyClient ?? DefaultClient;
            }
        }

        return _httpClient ?? DefaultClient;
    }

    private void EnsureProxyClient()
    {
        if (_proxySettingsProvider == null || !_proxySettingsProvider.IsEnabled)
        {
            lock (_syncLock)
            {
                _lastProxyEnabled = false;
                _proxyClient = null;
                _proxyHandler = null;
            }
            return;
        }

        lock (_syncLock)
        {
            var host = _proxySettingsProvider.Host;
            var port = _proxySettingsProvider.Port;
            var type = _proxySettingsProvider.Type;

            if (_proxyClient == null || !_lastProxyEnabled || _lastProxyHost != host || _lastProxyPort != port || _lastProxyType != type)
            {
                _proxyHandler = _proxySettingsProvider.CreateHandler();
                _proxyClient = _proxyHandler != null
                    ? new HttpClient(_proxyHandler)
                    : DefaultClient;
                _lastProxyEnabled = true;
                _lastProxyHost = host;
                _lastProxyPort = port;
                _lastProxyType = type;
            }
        }
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

            using var response = Client.Send(request);

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
                    Message = $"Newznab returned HTTP 429 Too Many Requests.",
                    StatusCode = 429,
                    RetryAfter = retryAfter
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

    public string BuildSearchUrl(IndexerDefinition definition, TorznabSearchCriteria criteria)
    {
        return BuildSearchUrl(definition, criteria?.ToSearchQuery());
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

        string tParam;
        var queryParams = new System.Collections.Generic.List<string>();

        switch (mode)
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

                if (!string.IsNullOrWhiteSpace(searchQuery.TmdbId))
                {
                    queryParams.Add($"tmdbid={Uri.EscapeDataString(searchQuery.TmdbId)}");
                }

                if (searchQuery.Year.HasValue)
                {
                    queryParams.Add($"year={searchQuery.Year.Value}");
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

                if (searchQuery.Year.HasValue)
                {
                    queryParams.Add($"year={searchQuery.Year.Value}");
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

                if (searchQuery.Year.HasValue)
                {
                    queryParams.Add($"year={searchQuery.Year.Value}");
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

                if (searchQuery.Year.HasValue)
                {
                    queryParams.Add($"year={searchQuery.Year.Value}");
                }

                break;

            default:
                tParam = "search";
                if (!string.IsNullOrWhiteSpace(searchQuery.Query))
                {
                    queryParams.Add($"q={Uri.EscapeDataString(searchQuery.Query)}");
                }

                if (searchQuery.Year.HasValue)
                {
                    queryParams.Add($"year={searchQuery.Year.Value}");
                }

                break;
        }

        var catToMap = !string.IsNullOrWhiteSpace(searchQuery.Category) ? searchQuery.Category : searchQuery.Categories;
        var mappedCat = TorznabIndexer.MapFriendlyCategory(catToMap);
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

    public System.Collections.Generic.List<ReleaseInfo> Search(IndexerDefinition definition, TorznabSearchCriteria criteria)
    {
        return Search(definition, criteria?.ToSearchQuery());
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
            else if (!string.IsNullOrWhiteSpace(searchQuery.Author) || !string.IsNullOrWhiteSpace(searchQuery.Title))
            {
                mode = SearchMode.Book;
            }
        }

        if (mode == SearchMode.Default &&
            string.IsNullOrWhiteSpace(searchQuery.Query) &&
            string.IsNullOrWhiteSpace(searchQuery.ImdbId) &&
            string.IsNullOrWhiteSpace(searchQuery.TvdbId) &&
            string.IsNullOrWhiteSpace(searchQuery.TmdbId) &&
            !searchQuery.Season.HasValue &&
            !searchQuery.Episode.HasValue &&
            string.IsNullOrWhiteSpace(searchQuery.Artist) &&
            string.IsNullOrWhiteSpace(searchQuery.Album) &&
            string.IsNullOrWhiteSpace(searchQuery.Author) &&
            string.IsNullOrWhiteSpace(searchQuery.Title))
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

            using var response = Client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Newznab search returned status code {0}", response.StatusCode);
                var ex = new HttpRequestException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}", null, response.StatusCode);
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
                var guidNode = item.SelectSingleNode("guid");

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
                    MagnetUrl = magnetUrl,
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

                if (pubDateNode != null && !string.IsNullOrWhiteSpace(pubDateNode.InnerText))
                {
                    if (DateTime.TryParse(pubDateNode.InnerText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var pDate))
                    {
                        release.PublishDate = DateTime.SpecifyKind(pDate, DateTimeKind.Utc);
                    }
                    else if (DateTimeOffset.TryParse(pubDateNode.InnerText.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                    {
                        release.PublishDate = dto.UtcDateTime;
                    }
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
                        else if (name == "magneturl")
                        {
                            release.MagnetUrl = val;
                        }
                        else if (name == "infohash")
                        {
                            release.InfoHash = val;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(release.InfoHash) && !string.IsNullOrWhiteSpace(release.MagnetUrl))
                {
                    try
                    {
                        release.InfoHash = MagnetLinkParser.Parse(release.MagnetUrl)?.InfoHash;
                    }
                    catch
                    {
                        var match = Regex.Match(release.MagnetUrl, @"xt=urn:btih:([a-fA-F0-9]{40}|[a-zA-Z2-7]{32})", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            var hash = match.Groups[1].Value;
                            if (hash.Length == 32)
                            {
                                var bytes = MagnetLinkParser.Base32Decode(hash);
                                if (bytes != null && bytes.Length == 20)
                                {
                                    release.InfoHash = Convert.ToHexString(bytes).ToLowerInvariant();
                                }
                            }
                            else
                            {
                                release.InfoHash = hash.ToLowerInvariant();
                            }
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
