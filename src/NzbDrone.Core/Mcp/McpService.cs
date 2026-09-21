using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Mcp;

public class McpService : IMcpService
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentImportService _torrentImportService;
    private readonly ISeedingService _seedingService;
    private readonly ISpeedHistoryService _speedHistoryService;
    private readonly ITagService _tagService;
    private readonly IHealthCheckService _healthCheckService;
    private readonly IDiskSpaceService _diskSpaceService;
    private readonly ITrackerAnnounceService _trackerAnnounceService;
    private readonly ITrackerScrapeService _trackerScrapeService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public McpService(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService = null,
        ITrackerEntryService trackerEntryService = null,
        ITorrentImportService torrentImportService = null,
        ISeedingService seedingService = null,
        ISpeedHistoryService speedHistoryService = null,
        ITagService tagService = null,
        IHealthCheckService healthCheckService = null,
        IDiskSpaceService diskSpaceService = null,
        ITrackerAnnounceService trackerAnnounceService = null,
        ITrackerScrapeService trackerScrapeService = null)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _trackerEntryService = trackerEntryService;
        _torrentImportService = torrentImportService;
        _seedingService = seedingService;
        _speedHistoryService = speedHistoryService;
        _tagService = tagService;
        _healthCheckService = healthCheckService;
        _diskSpaceService = diskSpaceService;
        _trackerAnnounceService = trackerAnnounceService;
        _trackerScrapeService = trackerScrapeService;
    }

    public async Task<JsonRpcResponse> ProcessMessageJsonAsync(string requestJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return JsonRpcResponse.CreateError(null, -32700, "Parse error: empty request");
        }

        JsonRpcRequest request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(requestJson, McpJsonOptions.Default);
            if (request == null)
            {
                return JsonRpcResponse.CreateError(null, -32700, "Parse error: invalid JSON");
            }
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.CreateError(null, -32700, "Parse error: " + ex.Message);
        }

        return await ProcessMessageAsync(request, cancellationToken);
    }

    public async Task<JsonRpcResponse> ProcessMessageAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return JsonRpcResponse.CreateError(null, -32600, "Invalid Request: request is null");
        }

        var method = request.Method?.Trim() ?? string.Empty;

        try
        {
            switch (method)
            {
                case "initialize":
                    return JsonRpcResponse.Success(request.Id, HandleInitialize());

                case "notifications/initialized":
                    return null;

                case "ping":
                    return JsonRpcResponse.Success(request.Id, new { });

                case "tools/list":
                    return JsonRpcResponse.Success(request.Id, HandleToolsList());

                case "tools/call":
                    return JsonRpcResponse.Success(request.Id, await HandleToolCallAsync(request.Params, cancellationToken));

                case "resources/list":
                    return JsonRpcResponse.Success(request.Id, HandleResourcesList());

                case "resources/read":
                    return HandleResourceRead(request.Id, request.Params);

                case "prompts/list":
                    return JsonRpcResponse.Success(request.Id, HandlePromptsList());

                case "prompts/get":
                    return HandlePromptGet(request.Id, request.Params);

                default:
                    return JsonRpcResponse.CreateError(request.Id, -32601, $"Method '{method}' not found.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing MCP method '{0}'", method);
            return JsonRpcResponse.CreateError(request.Id, -32603, "Internal error: " + ex.Message);
        }
    }

    private McpInitializeResult HandleInitialize()
    {
        return new McpInitializeResult
        {
            ProtocolVersion = "2024-11-05",
            Capabilities = new McpServerCapabilities
            {
                Tools = new McpToolsCapability { ListChanged = false },
                Resources = new McpResourcesCapability { Subscribe = false, ListChanged = false },
                Prompts = new McpPromptsCapability { ListChanged = false }
            },
            ServerInfo = new McpImplementation
            {
                Name = "Seedarr MCP Server",
                Version = "1.0.0"
            }
        };
    }

    private McpToolListResult HandleToolsList()
    {
        return new McpToolListResult
        {
            Tools = new List<McpTool>
            {
                new()
                {
                    Name = "list_torrents",
                    Description = "List torrents with status, category, tag, and ratio filters.",
                    InputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            status = new { type = "string", description = "Filter by status: Seeding, Paused, Downloading, Stopped." },
                            category = new { type = "string", description = "Filter by category name." },
                            tag = new { type = "string", description = "Filter by tag name or tag ID." },
                            min_ratio = new { type = "number", description = "Filter by minimum seed ratio." },
                            max_ratio = new { type = "number", description = "Filter by maximum seed ratio." }
                        }
                    }
                },
                new()
                {
                    Name = "get_torrent_details",
                    Description = "Retrieve files, trackers, and peer stats for a given torrent ID or hash.",
                    InputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "integer", description = "Torrent numeric ID." },
                            hash = new { type = "string", description = "Torrent infohash." }
                        }
                    }
                },
                new()
                {
                    Name = "pause_torrent",
                    Description = "Pause seeding or downloading for a torrent.",
                    InputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "integer", description = "Torrent numeric ID." },
                            hash = new { type = "string", description = "Torrent infohash." }
                        }
                    }
                },
                new()
                {
                    Name = "resume_torrent",
                    Description = "Resume seeding or downloading for a torrent.",
                    InputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "integer", description = "Torrent numeric ID." },
                            hash = new { type = "string", description = "Torrent infohash." }
                        }
                    }
                },
                new()
                {
                    Name = "add_torrent",
                    Description = "Ingest magnet URI or torrent file.",
                    InputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            magnet = new { type = "string", description = "Magnet URI link." },
                            torrent_file = new { type = "string", description = "Base64-encoded .torrent file content." },
                            file_name = new { type = "string", description = "Optional filename for the torrent." }
                        }
                    }
                },
                new()
                {
                    Name = "boost_trackers",
                    Description = "Trigger on-demand tracker scraping / reannounce.",
                    InputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "integer", description = "Optional torrent ID." },
                            hash = new { type = "string", description = "Optional torrent infohash." }
                        }
                    }
                },
                new()
                {
                    Name = "get_seeding_metrics",
                    Description = "Retrieve real-time seeding bandwidth and ratio stats.",
                    InputSchema = new
                    {
                        type = "object",
                        properties = new { }
                    }
                }
            }
        };
    }

    private async Task<McpToolCallResult> HandleToolCallAsync(JsonElement? paramsElement, CancellationToken cancellationToken)
    {
        if (!paramsElement.HasValue)
        {
            return McpToolCallResult.Text("Parameters are missing.", isError: true);
        }

        string toolName = null;
        JsonElement arguments = default;

        if (paramsElement.Value.TryGetProperty("name", out var nameProp))
        {
            toolName = nameProp.GetString();
        }

        if (paramsElement.Value.TryGetProperty("arguments", out var argsProp))
        {
            arguments = argsProp;
        }

        switch (toolName)
        {
            case "list_torrents":
                return ExecuteListTorrents(arguments);

            case "get_torrent_details":
                return ExecuteGetTorrentDetails(arguments);

            case "pause_torrent":
                return ExecutePauseTorrent(arguments);

            case "resume_torrent":
                return ExecuteResumeTorrent(arguments);

            case "add_torrent":
                return ExecuteAddTorrent(arguments);

            case "boost_trackers":
                return await ExecuteBoostTrackersAsync(arguments, cancellationToken);

            case "get_seeding_metrics":
                return ExecuteGetSeedingMetrics();

            default:
                return McpToolCallResult.Text($"Unknown tool '{toolName}'.", isError: true);
        }
    }

    private McpToolCallResult ExecuteListTorrents(JsonElement arguments)
    {
        string status = null;
        string category = null;
        string tag = null;
        double? minRatio = null;
        double? maxRatio = null;

        if (arguments.ValueKind == JsonValueKind.Object)
        {
            if (arguments.TryGetProperty("status", out var sProp)) status = sProp.GetString();
            if (arguments.TryGetProperty("category", out var cProp)) category = cProp.GetString();
            if (arguments.TryGetProperty("tag", out var tProp)) tag = tProp.GetString();
            if (arguments.TryGetProperty("min_ratio", out var minProp) && minProp.TryGetDouble(out var minVal)) minRatio = minVal;
            if (arguments.TryGetProperty("max_ratio", out var maxProp) && maxProp.TryGetDouble(out var maxVal)) maxRatio = maxVal;
        }

        var torrents = _torrentService.GetAll() ?? new List<Torrent>();
        var query = torrents.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(t => t.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            if (int.TryParse(tag, out var tagId))
            {
                query = query.Where(t => t.TagIds != null && t.TagIds.Contains(tagId));
            }
            else if (_tagService != null)
            {
                var matchingTag = _tagService.GetAll()?.FirstOrDefault(x => string.Equals(x.Label, tag, StringComparison.OrdinalIgnoreCase));
                if (matchingTag != null)
                {
                    query = query.Where(t => t.TagIds != null && t.TagIds.Contains(matchingTag.Id));
                }
                else
                {
                    query = Enumerable.Empty<Torrent>();
                }
            }
        }

        if (minRatio.HasValue)
        {
            query = query.Where(t => t.Ratio >= minRatio.Value);
        }

        if (maxRatio.HasValue)
        {
            query = query.Where(t => t.Ratio <= maxRatio.Value);
        }

        var result = query.Select(t => new
        {
            id = t.Id,
            name = t.Name,
            infoHash = t.InfoHash,
            status = t.Status.ToString(),
            category = t.Category,
            totalSize = t.TotalSize,
            ratio = t.Ratio,
            progress = t.Progress,
            uploadSpeed = t.UploadSpeed,
            downloadSpeed = t.DownloadSpeed,
            seeders = t.Seeders,
            leechers = t.Leechers
        }).ToList();

        return McpToolCallResult.Text(JsonSerializer.Serialize(result, McpJsonOptions.Default));
    }

    private McpToolCallResult ExecuteGetTorrentDetails(JsonElement arguments)
    {
        var torrent = FindTorrent(arguments);
        if (torrent == null)
        {
            return McpToolCallResult.Text("Torrent not found.", isError: true);
        }

        var files = _torrentFileService != null
            ? _torrentFileService.GetByTorrentId(torrent.Id)?.Select(f => new
            {
                id = f.Id,
                path = f.Path,
                size = f.Size,
                bytesCompleted = f.BytesCompleted,
                wanted = f.Wanted,
                priority = f.Priority
            }).ToList()
            : (object)Array.Empty<object>();

        var trackers = _trackerEntryService != null
            ? _trackerEntryService.GetByTorrentId(torrent.Id)?.Select(tr => new
            {
                id = tr.Id,
                url = tr.Url,
                status = tr.Status.ToString(),
                seeders = tr.Seeders,
                leechers = tr.Leechers,
                lastAnnounce = tr.LastAnnounce,
                errorMessage = tr.ErrorMessage
            }).ToList()
            : (object)Array.Empty<object>();

        var details = new
        {
            id = torrent.Id,
            name = torrent.Name,
            infoHash = torrent.InfoHash,
            status = torrent.Status.ToString(),
            totalSize = torrent.TotalSize,
            progress = torrent.Progress,
            ratio = torrent.Ratio,
            uploaded = torrent.Uploaded,
            downloaded = torrent.Downloaded,
            uploadSpeed = torrent.UploadSpeed,
            downloadSpeed = torrent.DownloadSpeed,
            category = torrent.Category,
            savePath = torrent.SavePath,
            dateAdded = torrent.DateAdded,
            files,
            trackers,
            peerStats = new
            {
                seeders = torrent.Seeders,
                leechers = torrent.Leechers,
                availability = torrent.Availability,
                uploadSpeed = torrent.UploadSpeed,
                downloadSpeed = torrent.DownloadSpeed
            }
        };

        return McpToolCallResult.Text(JsonSerializer.Serialize(details, McpJsonOptions.Default));
    }

    private McpToolCallResult ExecutePauseTorrent(JsonElement arguments)
    {
        var torrent = FindTorrent(arguments);
        if (torrent == null)
        {
            return McpToolCallResult.Text("Torrent not found.", isError: true);
        }

        _torrentService.Pause(torrent.Id);
        return McpToolCallResult.Text($"Torrent '{torrent.Name}' (ID: {torrent.Id}) paused successfully.");
    }

    private McpToolCallResult ExecuteResumeTorrent(JsonElement arguments)
    {
        var torrent = FindTorrent(arguments);
        if (torrent == null)
        {
            return McpToolCallResult.Text("Torrent not found.", isError: true);
        }

        _torrentService.Start(torrent.Id);
        return McpToolCallResult.Text($"Torrent '{torrent.Name}' (ID: {torrent.Id}) resumed successfully.");
    }

    private McpToolCallResult ExecuteAddTorrent(JsonElement arguments)
    {
        if (_torrentImportService == null)
        {
            return McpToolCallResult.Text("Torrent import service is unavailable.", isError: true);
        }

        string magnet = null;
        string torrentFile = null;
        string fileName = null;

        if (arguments.ValueKind == JsonValueKind.Object)
        {
            if (arguments.TryGetProperty("magnet", out var mProp)) magnet = mProp.GetString();
            if (arguments.TryGetProperty("torrent_file", out var tfProp)) torrentFile = tfProp.GetString();
            if (arguments.TryGetProperty("file_name", out var fnProp)) fileName = fnProp.GetString();
        }

        if (!string.IsNullOrWhiteSpace(magnet))
        {
            var added = _torrentImportService.ImportFromMagnet(magnet);
            return McpToolCallResult.Text($"Added torrent '{added?.Name ?? "Unknown"}' (ID: {added?.Id ?? 0}, InfoHash: {added?.InfoHash}) from magnet.");
        }

        if (!string.IsNullOrWhiteSpace(torrentFile))
        {
            byte[] fileBytes;
            try
            {
                fileBytes = Convert.FromBase64String(torrentFile);
            }
            catch (Exception ex)
            {
                return McpToolCallResult.Text("Invalid base64 encoding in torrent_file: " + ex.Message, isError: true);
            }

            using var stream = new MemoryStream(fileBytes);
            var added = _torrentImportService.ImportFromFile(stream, fileName ?? "imported.torrent");
            return McpToolCallResult.Text($"Added torrent '{added?.Name ?? "Unknown"}' (ID: {added?.Id ?? 0}, InfoHash: {added?.InfoHash}) from file.");
        }

        return McpToolCallResult.Text("Either 'magnet' or 'torrent_file' must be provided.", isError: true);
    }

    private async Task<McpToolCallResult> ExecuteBoostTrackersAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var torrent = FindTorrent(arguments);
        if (torrent != null)
        {
            var actions = new List<string>();

            if (_trackerAnnounceService != null)
            {
                try
                {
                    _trackerAnnounceService.AnnounceTorrent(torrent, force: true);
                    actions.Add("announced");
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to announce torrent {0}", torrent.Id);
                }
            }

            if (_trackerScrapeService != null)
            {
                try
                {
                    await _trackerScrapeService.ScrapeTorrentAsync(torrent.Id, cancellationToken);
                    actions.Add("scraped");
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to scrape torrent {0}", torrent.Id);
                }
            }

            return McpToolCallResult.Text($"Boosted trackers for torrent '{torrent.Name}' ({string.Join(" and ", actions)}).");
        }

        if (_trackerScrapeService != null)
        {
            try
            {
                var count = await _trackerScrapeService.ScrapeAllTorrentsAsync(cancellationToken);
                return McpToolCallResult.Text($"Boosted trackers for all active torrents ({count} torrents scraped).");
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to scrape all torrents");
            }
        }

        return McpToolCallResult.Text("Tracker boost triggered for active torrents.");
    }

    private McpToolCallResult ExecuteGetSeedingMetrics()
    {
        var stats = _seedingService?.GetStats() ?? new SeedingStats();
        var history = _speedHistoryService?.GetHistory();
        var latest = history != null && history.Count > 0 ? history[^1] : null;

        var metrics = new
        {
            activeTorrents = stats.ActiveTorrents,
            totalUploaded = stats.TotalUploaded,
            totalDownloaded = stats.TotalDownloaded,
            averageRatio = stats.AverageRatio,
            uploadSpeed = latest?.UploadSpeed ?? 0,
            downloadSpeed = latest?.DownloadSpeed ?? 0
        };

        return McpToolCallResult.Text(JsonSerializer.Serialize(metrics, McpJsonOptions.Default));
    }

    private McpResourceListResult HandleResourcesList()
    {
        return new McpResourceListResult
        {
            Resources = new List<McpResource>
            {
                new()
                {
                    Uri = "seedarr://torrents",
                    Name = "Active Torrents",
                    Description = "Active torrent list with status, progress, and transfer speeds.",
                    MimeType = "application/json"
                },
                new()
                {
                    Uri = "seedarr://system/status",
                    Name = "System Status",
                    Description = "Engine health checks and volume disk space usage.",
                    MimeType = "application/json"
                },
                new()
                {
                    Uri = "seedarr://logs/recent",
                    Name = "Recent Logs",
                    Description = "Recent application log entries.",
                    MimeType = "application/json"
                }
            }
        };
    }

    private JsonRpcResponse HandleResourceRead(object id, JsonElement? paramsElement)
    {
        if (!paramsElement.HasValue || !paramsElement.Value.TryGetProperty("uri", out var uriProp))
        {
            return JsonRpcResponse.CreateError(id, -32602, "Parameter 'uri' is required.");
        }

        var uri = uriProp.GetString()?.Trim() ?? string.Empty;

        switch (uri)
        {
            case "seedarr://torrents":
            {
                var torrents = _torrentService.GetAll() ?? new List<Torrent>();
                var json = JsonSerializer.Serialize(torrents.Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    infoHash = t.InfoHash,
                    status = t.Status.ToString(),
                    size = t.TotalSize,
                    ratio = t.Ratio,
                    progress = t.Progress,
                    uploadSpeed = t.UploadSpeed,
                    downloadSpeed = t.DownloadSpeed,
                    seeders = t.Seeders,
                    leechers = t.Leechers
                }), McpJsonOptions.Default);

                return JsonRpcResponse.Success(id, new McpResourceReadResult
                {
                    Contents = new List<McpResourceContent>
                    {
                        new() { Uri = uri, MimeType = "application/json", Text = json }
                    }
                });
            }

            case "seedarr://system/status":
            {
                var health = _healthCheckService?.PerformChecks()?.Select(h => new
                {
                    source = h.Source,
                    type = h.Type.ToString(),
                    message = h.Message
                }).ToList() ?? (object)Array.Empty<object>();

                var diskSpace = _diskSpaceService?.GetDiskSpace()?.Select(d => new
                {
                    path = d.Path,
                    freeSpace = d.FreeSpace,
                    totalSpace = d.TotalSpace
                }).ToList() ?? (object)Array.Empty<object>();

                var json = JsonSerializer.Serialize(new { health, diskSpace }, McpJsonOptions.Default);

                return JsonRpcResponse.Success(id, new McpResourceReadResult
                {
                    Contents = new List<McpResourceContent>
                    {
                        new() { Uri = uri, MimeType = "application/json", Text = json }
                    }
                });
            }

            case "seedarr://logs/recent":
            {
                var logs = RingBufferTarget.Instance?.GetEntries(100, LogLevel.Trace)?.Select(l => new
                {
                    time = l.Time.ToString("O"),
                    level = l.Level,
                    logger = l.Logger,
                    message = l.Message
                }).ToList() ?? (object)Array.Empty<object>();

                var json = JsonSerializer.Serialize(logs, McpJsonOptions.Default);

                return JsonRpcResponse.Success(id, new McpResourceReadResult
                {
                    Contents = new List<McpResourceContent>
                    {
                        new() { Uri = uri, MimeType = "application/json", Text = json }
                    }
                });
            }

            default:
                return JsonRpcResponse.CreateError(id, -32602, $"Resource '{uri}' not found.");
        }
    }

    private McpPromptListResult HandlePromptsList()
    {
        return new McpPromptListResult
        {
            Prompts = new List<McpPrompt>
            {
                new()
                {
                    Name = "swarm_diagnosis",
                    Description = "Template for BitTorrent swarm health diagnosis and connectivity troubleshooting.",
                    Arguments = new List<McpPromptArgument>
                    {
                        new() { Name = "torrent_id", Description = "Torrent numeric ID or infohash to diagnose.", Required = false }
                    }
                },
                new()
                {
                    Name = "ratio_balancing",
                    Description = "Template for seeding ratio balancing, bandwidth allocation, and upload optimization.",
                    Arguments = new List<McpPromptArgument>
                    {
                        new() { Name = "target_ratio", Description = "Target ratio threshold (defaults to 1.0).", Required = false }
                    }
                },
                new()
                {
                    Name = "arr_config",
                    Description = "Template for Arr integration (Radarr, Sonarr, Lidarr download client configuration).",
                    Arguments = new List<McpPromptArgument>
                    {
                        new() { Name = "arr_type", Description = "Arr application type (e.g. Radarr, Sonarr).", Required = false }
                    }
                }
            }
        };
    }

    private JsonRpcResponse HandlePromptGet(object id, JsonElement? paramsElement)
    {
        if (!paramsElement.HasValue || !paramsElement.Value.TryGetProperty("name", out var nameProp))
        {
            return JsonRpcResponse.CreateError(id, -32602, "Parameter 'name' is required.");
        }

        var promptName = nameProp.GetString()?.Trim() ?? string.Empty;
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (paramsElement.Value.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in argsProp.EnumerateObject())
            {
                arguments[prop.Name] = prop.Value.ToString();
            }
        }

        switch (promptName)
        {
            case "swarm_diagnosis":
            {
                arguments.TryGetValue("torrent_id", out var tid);
                var text = string.IsNullOrWhiteSpace(tid)
                    ? "You are diagnosing BitTorrent swarm health across all torrents in Seedarr. Analyze seed/peer ratios, unchoke limits, tracker scrape health, and DHT connectivity to identify stalled downloads and swarm bottlenecks. Recommend remediation steps."
                    : $"You are diagnosing the BitTorrent swarm for torrent '{tid}' in Seedarr. Analyze peer connectivity, tracker responses, piece availability, and choke/unchoke dynamics to resolve download/seeding stalls.";

                return JsonRpcResponse.Success(id, new McpPromptGetResult
                {
                    Description = "Swarm diagnosis template",
                    Messages = new List<McpPromptMessage>
                    {
                        new() { Role = "user", Content = new McpContent { Type = "text", Text = text } }
                    }
                });
            }

            case "ratio_balancing":
            {
                arguments.TryGetValue("target_ratio", out var target);
                var tr = string.IsNullOrWhiteSpace(target) ? "1.0" : target;
                var text = $"You are optimizing seeding bandwidth and ratio balancing in Seedarr for a target ratio of {tr}. Review torrent seeding times, upload limits, and tracker requirements. Recommend which torrents to pause or re-prioritize to maximize upload efficiency.";

                return JsonRpcResponse.Success(id, new McpPromptGetResult
                {
                    Description = "Ratio balancing template",
                    Messages = new List<McpPromptMessage>
                    {
                        new() { Role = "user", Content = new McpContent { Type = "text", Text = text } }
                    }
                });
            }

            case "arr_config":
            {
                arguments.TryGetValue("arr_type", out var arr);
                var at = string.IsNullOrWhiteSpace(arr) ? "Radarr/Sonarr" : arr;
                var text = $"You are an expert configuring integration between Seedarr and {at}. Detail the required download client connection settings (host, port, API credentials, category paths, and remote path mappings), and verify webhook notifications for automated grabbed and completed release handling.";

                return JsonRpcResponse.Success(id, new McpPromptGetResult
                {
                    Description = "Arr integration config template",
                    Messages = new List<McpPromptMessage>
                    {
                        new() { Role = "user", Content = new McpContent { Type = "text", Text = text } }
                    }
                });
            }

            default:
                return JsonRpcResponse.CreateError(id, -32602, $"Prompt '{promptName}' not found.");
        }
    }

    private Torrent FindTorrent(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (arguments.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id) && id > 0)
        {
            return _torrentService.Get(id);
        }

        if (arguments.TryGetProperty("hash", out var hashProp))
        {
            var hash = hashProp.GetString()?.Trim();
            if (!string.IsNullOrEmpty(hash))
            {
                return _torrentService.FindByInfoHash(hash) ??
                       _torrentService.GetAll()?.FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
            }
        }

        return null;
    }
}
