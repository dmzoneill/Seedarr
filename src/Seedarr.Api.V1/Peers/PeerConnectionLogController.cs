using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Peers;
using Seedarr.Http;

namespace Seedarr.Api.V1.Peers;

[V1ApiController("peerlog")]
public class PeerConnectionLogController : Controller
{
    private readonly IPeerConnectionLogService _logService;
    private readonly IConnectionManager _connectionManager;

    public PeerConnectionLogController(IPeerConnectionLogService logService, IConnectionManager connectionManager)
    {
        _logService = logService;
        _connectionManager = connectionManager;
    }

    [HttpGet]
    public ActionResult<List<PeerConnectionLogResource>> GetLogs(
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end,
        [FromQuery] string infoHash)
    {
        var startDate = start ?? DateTime.UtcNow.AddHours(-1);
        var endDate = end ?? DateTime.UtcNow;

        List<PeerConnectionLog> logs;

        if (!string.IsNullOrEmpty(infoHash))
        {
            logs = _logService.GetByInfoHash(infoHash, startDate, endDate);
        }
        else
        {
            logs = _logService.GetByTimeRange(startDate, endDate);
        }

        return Ok(logs.Select(ToResource).ToList());
    }

    [HttpGet("active")]
    public ActionResult<List<PeerConnectionLogResource>> GetActive()
    {
        var now = DateTime.UtcNow;
        var logs = _logService.GetByTimeRange(now.AddHours(-24), now);

        var connected = new Dictionary<string, PeerConnectionLog>();

        foreach (var log in logs.OrderBy(l => l.Timestamp))
        {
            var key = $"{log.RemoteIp}:{log.RemotePort}:{log.InfoHash}";
            if (log.EventType == "Connected")
            {
                connected[key] = log;
            }
            else
            {
                connected.Remove(key);
            }
        }

        return Ok(connected.Values.Select(ToResource).ToList());
    }

    [HttpGet("graph")]
    public ActionResult<PeerGraphResource> GetGraph(
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end)
    {
        var startDate = start ?? DateTime.UtcNow.AddHours(-1);
        var endDate = end ?? DateTime.UtcNow;

        var logs = _logService.GetByTimeRange(startDate, endDate);

        var nodes = new List<PeerGraphNode>();
        var links = new List<PeerGraphLink>();
        var seenTorrents = new HashSet<string>();
        var seenPeers = new HashSet<string>();

        nodes.Add(new PeerGraphNode
        {
            Id = "seedarr",
            Label = "Seedarr",
            Type = "center",
        });

        foreach (var log in logs.Where(l => l.EventType == "Connected"))
        {
            if (!string.IsNullOrEmpty(log.InfoHash) && seenTorrents.Add(log.InfoHash))
            {
                nodes.Add(new PeerGraphNode
                {
                    Id = $"torrent:{log.InfoHash}",
                    Label = log.TorrentName ?? log.InfoHash[..8],
                    Type = "torrent",
                    InfoHash = log.InfoHash,
                });

                links.Add(new PeerGraphLink
                {
                    Source = "seedarr",
                    Target = $"torrent:{log.InfoHash}",
                    Type = "seeds",
                });
            }

            var peerId = $"{log.RemoteIp}:{log.RemotePort}";
            if (seenPeers.Add($"{peerId}:{log.InfoHash}"))
            {
                nodes.Add(new PeerGraphNode
                {
                    Id = $"peer:{peerId}:{log.InfoHash}",
                    Label = log.RemoteIp,
                    Type = "peer",
                    IsEncrypted = log.IsEncrypted,
                });

                var torrentNodeId = !string.IsNullOrEmpty(log.InfoHash)
                    ? $"torrent:{log.InfoHash}"
                    : "seedarr";

                links.Add(new PeerGraphLink
                {
                    Source = torrentNodeId,
                    Target = $"peer:{peerId}:{log.InfoHash}",
                    Type = log.IsEncrypted ? "encrypted" : "plain",
                });
            }
        }

        return Ok(new PeerGraphResource
        {
            Nodes = nodes,
            Links = links,
        });
    }

    [HttpDelete]
    public ActionResult Purge([FromQuery] DateTime? before)
    {
        var purgeDate = before ?? DateTime.UtcNow.AddDays(-30);
        _logService.Purge(purgeDate);
        return Ok();
    }

    private static PeerConnectionLogResource ToResource(PeerConnectionLog log)
    {
        return new PeerConnectionLogResource
        {
            Id = log.Id,
            InfoHash = log.InfoHash,
            TorrentName = log.TorrentName,
            RemoteIp = log.RemoteIp,
            RemotePort = log.RemotePort,
            PeerId = log.PeerId,
            IsEncrypted = log.IsEncrypted,
            EventType = log.EventType,
            Timestamp = log.Timestamp,
        };
    }
}

public class PeerGraphResource
{
    public List<PeerGraphNode> Nodes { get; set; }
    public List<PeerGraphLink> Links { get; set; }
}

public class PeerGraphNode
{
    public string Id { get; set; }
    public string Label { get; set; }
    public string Type { get; set; }
    public string InfoHash { get; set; }
    public bool IsEncrypted { get; set; }
}

public class PeerGraphLink
{
    public string Source { get; set; }
    public string Target { get; set; }
    public string Type { get; set; }
}
