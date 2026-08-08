using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using Polly;

namespace NzbDrone.Core.ArrIntegration;

public class RadarrConnection : IArrConnection
{
    private readonly HttpClient _client;
    private readonly ResiliencePipeline _policy;
    private readonly Logger _logger;

    public string Name => "Radarr";
    public string ArrType => "Radarr";

    public string Url { get; set; } = "http://localhost:7878";
    public string ApiKey { get; set; } = "";

    public RadarrConnection(HttpClient client = null, ResiliencePipeline policy = null)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _client = client ?? ArrConnectionResources.SharedClient;
        _policy = policy ?? ArrConnectionResources.SharedPolicy;
    }

    public List<ArrDownloadRecord> GetDownloadHistory()
    {
        try
        {
            var result = _policy.Execute(ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url.TrimEnd('/')}/api/v3/history?pageSize=50&sortKey=date&sortDirection=descending");
                request.Headers.Add("X-Api-Key", ApiKey);

                using var response = _client.Send(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn("Radarr API returned {0}", response.StatusCode);
                    return (string)null;
                }

                using var stream = response.Content.ReadAsStream(ct);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            });

            if (result == null)
            {
                return new List<ArrDownloadRecord>();
            }

            var json = result;
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
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch Radarr history");
            return new List<ArrDownloadRecord>();
        }
    }

    public MediaMetadata GetMediaDetails(int mediaId)
    {
        try
        {
            var result = _policy.Execute(ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url.TrimEnd('/')}/api/v3/movie/{mediaId}");
                request.Headers.Add("X-Api-Key", ApiKey ?? "");
                using var response = _client.Send(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    return (string)null;
                }

                using var stream = response.Content.ReadAsStream(ct);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            });

            if (result == null)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(result);
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

            if (root.TryGetProperty("credits", out var creditsObj) && creditsObj.TryGetProperty("cast", out var castArray))
            {
                foreach (var actorElem in castArray.EnumerateArray())
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
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to get movie media details for id {0}", mediaId);
            return null;
        }
    }

    public bool TestConnection() => TestConnectionDetailed().Success;

    public ArrTestResult TestConnectionDetailed()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            return ArrTestResult.Fail("URL cannot be empty");
        }

        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url.TrimEnd('/')}/api/v3/system/status");
            request.Headers.Add("X-Api-Key", ApiKey ?? "");
            using var response = _client.Send(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                return ArrTestResult.Ok($"Successfully connected to Radarr at {Url}");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return ArrTestResult.Fail("Authentication failed (HTTP 401 Unauthorized). Please check your API key.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ArrTestResult.Fail($"Endpoint not found (HTTP 404 Not Found) at {Url}. Verify the URL and port.");
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
            return ArrTestResult.Fail($"Connection timed out connecting to {Url} (exceeded 10s)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Radarr connection test failed");
            return ArrTestResult.Fail($"Connection failed: {ex.Message}");
        }
    }
}
