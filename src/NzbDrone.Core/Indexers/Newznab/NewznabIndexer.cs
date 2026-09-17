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

    public string Name => "Newznab";
    public string IndexerType => "Newznab";

    public NewznabIndexer(HttpClient httpClient = null)
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
            var mappedCat = TorznabIndexer.MapFriendlyCategory(category);
            var url = $"{definition.Url.TrimEnd('/')}{apiPath}?t=search&q={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrWhiteSpace(mappedCat))
            {
                url += $"&cat={Uri.EscapeDataString(mappedCat)}";
            }
            else if (!string.IsNullOrWhiteSpace(definition.Categories))
            {
                var defCat = TorznabIndexer.MapFriendlyCategory(definition.Categories);
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
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to search Newznab at {0} for query '{1}'", definition.Url, query);
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
                var guidNode = item.SelectSingleNode("guid");

                var downloadUrl = enclosureNode?.Attributes?["url"]?.Value ?? linkNode?.InnerText;

                var release = new ReleaseInfo
                {
                    Guid = guidNode?.InnerText ?? string.Empty,
                    IndexerId = definition?.Id ?? 0,
                    Indexer = definition?.Name,
                    Title = titleNode?.InnerText ?? string.Empty,
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
}
