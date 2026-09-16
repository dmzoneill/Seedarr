using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Xml;
using NLog;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Torznab;

public class TorznabIndexer : IIndexer
{
    private static readonly HttpClient DefaultClient = new();
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

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

    public TorznabIndexer(HttpClient httpClient = null)
    {
        _httpClient = httpClient ?? DefaultClient;
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
                    Message = "Authentication failed: Invalid API Key."
                };
            }

            return new IndexerTestResult
            {
                Success = false,
                Message = $"Torznab returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
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

    public System.Collections.Generic.List<ReleaseInfo> Search(IndexerDefinition definition, string query, string category = null, int offset = 0, int limit = 50)
    {
        var results = new System.Collections.Generic.List<ReleaseInfo>();
        if (definition == null || string.IsNullOrWhiteSpace(definition.Url) || string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        try
        {
            var apiPath = string.IsNullOrEmpty(definition.ApiPath) ? "/api" : definition.ApiPath;
            var mappedCat = MapFriendlyCategory(category);
            var url = $"{definition.Url.TrimEnd('/')}{apiPath}?t=search&q={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrWhiteSpace(mappedCat))
            {
                url += $"&cat={Uri.EscapeDataString(mappedCat)}";
            }
            else if (!string.IsNullOrWhiteSpace(definition.Categories))
            {
                var defCat = MapFriendlyCategory(definition.Categories);
                if (!string.IsNullOrWhiteSpace(defCat))
                {
                    url += $"&cat={Uri.EscapeDataString(defCat)}";
                }
            }

            if (offset > 0)
            {
                url += $"&offset={offset}";
            }

            if (limit > 0)
            {
                url += $"&limit={limit}";
            }

            if (!string.IsNullOrWhiteSpace(definition.ApiKey))
            {
                url += $"&apikey={Uri.EscapeDataString(definition.ApiKey)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(definition.ApiKey))
            {
                request.Headers.Add("X-Api-Key", definition.ApiKey);
            }

            using var response = _httpClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Torznab search returned status code {0}", response.StatusCode);
                if ((int)response.StatusCode == 429 || (int)response.StatusCode == 503)
                {
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
                }

                return results;
            }

            using var reader = new System.IO.StreamReader(response.Content.ReadAsStream());
            var xml = reader.ReadToEnd();
            return ParseResponse(xml, definition);
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to search Torznab at {0} for query '{1}'", definition.Url, query);
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

        var doc = new XmlDocument();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024
        };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        doc.Load(reader);

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

                var release = new ReleaseInfo
                {
                    IndexerId = definition?.Id ?? 0,
                    Indexer = definition?.Name,
                    Title = titleNode?.InnerText ?? string.Empty,
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

                if (pubDateNode != null && DateTime.TryParse(pubDateNode.InnerText, out var pDate))
                {
                    release.PublishDate = pDate;
                }

                var freeleechNode = item.SelectSingleNode("*[local-name()='freeleech']");
                if (freeleechNode != null)
                {
                    var flVal = freeleechNode.InnerText?.Trim();
                    if (flVal == "1" || string.Equals(flVal, "true", StringComparison.OrdinalIgnoreCase))
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
                        }
                        else if (name == "uploadvolumefactor")
                        {
                            if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var uvf)
                                && double.IsFinite(uvf) && uvf >= 0.0)
                            {
                                release.UploadVolumeFactor = Math.Clamp(uvf, 0.0, 10.0);
                            }
                        }
                        else if (name == "freeleech")
                        {
                            var trimmed = val?.Trim();
                            if (trimmed == "1" || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
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

                if (!string.IsNullOrWhiteSpace(release.Title))
                {
                    results.Add(release);
                }
            }
        }

        return results;
    }
}
