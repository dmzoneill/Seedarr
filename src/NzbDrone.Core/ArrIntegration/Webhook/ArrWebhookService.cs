using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;
using Polly;

namespace NzbDrone.Core.ArrIntegration.Webhook;

public interface IArrWebhookService
{
    ArrWebhookResult ProcessWebhook(ArrWebhookPayload payload);
}

public class ArrWebhookResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string InfoHash { get; set; }
}

public class ArrWebhookService : IArrWebhookService
{
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    });
    private static readonly ResiliencePipeline SharedPolicy = ResiliencePolicies.GetArrApiPolicy();

    private readonly HttpClient _client;
    private readonly ResiliencePipeline _policy;
    private readonly IArrConnectionFactory _connectionFactory;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly Logger _logger;

    public ArrWebhookService(
        IArrConnectionFactory connectionFactory,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser,
        HttpClient client = null,
        ResiliencePipeline policy = null)
    {
        _connectionFactory = connectionFactory;
        _torrentService = torrentService;
        _torrentFileParser = torrentFileParser;
        _logger = LogManager.GetCurrentClassLogger();
        _client = client ?? SharedClient;
        _policy = policy ?? SharedPolicy;
    }

    public ArrWebhookResult ProcessWebhook(ArrWebhookPayload payload)
    {
        if (payload.EventType != "Grab")
        {
            return new ArrWebhookResult { Success = true, Message = $"Ignored event type: {payload.EventType}" };
        }

        var downloadId = payload.DownloadId;
        if (string.IsNullOrEmpty(downloadId))
        {
            return new ArrWebhookResult { Success = false, Message = "No downloadId in webhook payload" };
        }

        var infoHash = downloadId.ToLowerInvariant();

        var existing = _torrentService.GetAll().FirstOrDefault(t =>
            string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return new ArrWebhookResult { Success = true, Message = "Torrent already exists", InfoHash = infoHash };
        }

        _logger.Info(
            "Webhook received: {0} grabbed '{1}' from {2}",
            payload.InstanceName,
            payload.Release?.ReleaseTitle,
            payload.Release?.Indexer);

        var torrent = new Torrent
        {
            Name = payload.Release?.ReleaseTitle ?? infoHash,
            InfoHash = infoHash,
            TotalSize = payload.Release?.Size ?? 0,
            DateAdded = DateTime.UtcNow,
            Status = TorrentStatus.Queued
        };

        _torrentService.Add(torrent);
        _logger.Info("Webhook: added '{0}' with basic metadata", torrent.Name);

        var connection = FindConnection(payload);
        if (connection != null)
        {
            _ = EnrichTorrentFromHistoryAsync(torrent.Id, infoHash, downloadId, connection, payload.InstanceName, CancellationToken.None);
        }

        return new ArrWebhookResult { Success = true, Message = "Added with basic metadata", InfoHash = infoHash };
    }

    private async Task EnrichTorrentFromHistoryAsync(int torrentId, string infoHash, string downloadId, ArrConnectionDefinition connection, string instanceName, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(5000, cancellationToken);

            var downloadUrl = await GetDownloadUrlFromHistoryAsync(connection, downloadId, cancellationToken);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                _logger.Warn("Enrich: could not find downloadUrl in {0} history for {1}", connection.ArrType, downloadId);
                return;
            }

            var torrentBytes = FetchTorrentFile(downloadUrl);
            if (torrentBytes == null || torrentBytes.Length == 0)
            {
                _logger.Warn("Enrich: failed to fetch .torrent from {0}", downloadUrl);
                return;
            }

            using var stream = new MemoryStream(torrentBytes);
            var parsed = _torrentFileParser.Parse(stream);

            var torrent = _torrentService.Get(torrentId);
            if (torrent == null)
            {
                return;
            }

            torrent.Name = parsed.Name;
            torrent.InfoHash = parsed.InfoHash.ToLowerInvariant();
            torrent.TotalSize = parsed.TotalSize;
            torrent.PieceCount = parsed.PieceCount;
            torrent.PieceLength = parsed.PieceLength;
            torrent.Comment = parsed.Comment;
            torrent.IsPrivate = parsed.IsPrivate;

            _torrentService.Update(torrent);
            _logger.Info(
                "Enrich: upgraded '{0}' ({1}) with full metadata from {2}",
                torrent.Name,
                torrent.InfoHash,
                instanceName);
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Enrich: cancelled for torrent {0}", infoHash);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Enrich: failed to upgrade torrent {0}", infoHash);
        }
    }

    private ArrConnectionDefinition FindConnection(ArrWebhookPayload payload)
    {
        var definitions = _connectionFactory.All();

        if (!string.IsNullOrEmpty(payload.ApplicationUrl))
        {
            var match = definitions.FirstOrDefault(d =>
                d.Enable && !string.IsNullOrEmpty(d.Url) &&
                payload.ApplicationUrl.TrimEnd('/').Equals(d.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match;
            }
        }

        if (!string.IsNullOrEmpty(payload.InstanceName))
        {
            var match = definitions.FirstOrDefault(d =>
                d.Enable && !string.IsNullOrEmpty(d.ArrType) &&
                payload.InstanceName.Contains(d.ArrType, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match;
            }
        }

        return definitions.FirstOrDefault(d => d.Enable);
    }

    private async Task<string> GetDownloadUrlFromHistoryAsync(ArrConnectionDefinition connection, string downloadId, CancellationToken cancellationToken)
    {
        var apiVersion = connection.ArrType == "Lidarr" ? "v1" : "v3";
        var variants = new[] { downloadId, downloadId.ToUpperInvariant() };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(2000, cancellationToken);
                _logger.Debug("Retrying history query for {0}, attempt {1}", downloadId, attempt + 1);
            }

            foreach (var id in variants)
            {
                var result = QueryHistoryForDownloadUrl(connection, apiVersion, id);
                if (result != null)
                {
                    return result;
                }
            }
        }

        _logger.Warn("DownloadUrl not found in {0} history after retries for {1}", connection.ArrType, downloadId);
        return null;
    }

    private string QueryHistoryForDownloadUrl(ArrConnectionDefinition connection, string apiVersion, string downloadId)
    {
        try
        {
            return _policy.Execute(ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{connection.Url}/api/{apiVersion}/history?downloadId={downloadId}&pageSize=1");
                request.Headers.Add("X-Api-Key", connection.ApiKey);

                using var response = _client.Send(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn("Failed to query {0} history: {1}", connection.ArrType, response.StatusCode);
                    return null;
                }

                var json = response.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("records", out var records))
                {
                    return null;
                }

                foreach (var record in records.EnumerateArray())
                {
                    if (record.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("downloadUrl", out var url))
                    {
                        return url.GetString();
                    }
                }

                return null;
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get downloadUrl from {0} history", connection.ArrType);
            return null;
        }
    }

    private byte[] FetchTorrentFile(string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.Warn("Blocked non-HTTP torrent fetch URL: {0}", downloadUrl);
            return null;
        }

        try
        {
            return _policy.Execute(ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-bittorrent"));

                using var response = _client.Send(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn("Failed to fetch .torrent from {0}: {1}", downloadUrl, response.StatusCode);
                    return null;
                }

                return response.Content.ReadAsByteArrayAsync(ct).GetAwaiter().GetResult();
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch .torrent from {0}", downloadUrl);
            return null;
        }
    }
}
