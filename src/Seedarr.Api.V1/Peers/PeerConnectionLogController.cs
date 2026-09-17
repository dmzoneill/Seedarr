using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer;
using Seedarr.Http;

namespace Seedarr.Api.V1.Peers;

[V1ApiController("peerlog")]
public class PeerConnectionLogController : Controller
{
    private readonly IPeerConnectionLogService _logService;
    private readonly IConnectionManager _connectionManager;
    private readonly ITorrentService _torrentService;
    private readonly IPeerDatabase _peerDatabase;

    public PeerConnectionLogController(
        IPeerConnectionLogService logService,
        IConnectionManager connectionManager,
        ITorrentService torrentService = null,
        IPeerDatabase peerDatabase = null)
    {
        _logService = logService;
        _connectionManager = connectionManager;
        _torrentService = torrentService;
        _peerDatabase = peerDatabase;
    }

    [HttpGet]
    public ActionResult<List<PeerConnectionLogResource>> GetLogs(
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end,
        [FromQuery] string infoHash)
    {
        var startDate = start ?? DateTime.UtcNow.AddHours(-1);
        var endDate = end ?? DateTime.UtcNow;

        var validation = ValidateDateRange(startDate, endDate);
        if (validation != null)
        {
            return validation;
        }

        List<PeerConnectionLog> logs;

        if (!string.IsNullOrEmpty(infoHash))
        {
            logs = _logService.GetByInfoHash(NormalizeInfoHash(infoHash), startDate, endDate);
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
        var activeConnections = _connectionManager?.GetAllConnections() ?? new List<PeerConnection>();

        var resources = activeConnections.Select(conn => new PeerConnectionLogResource
        {
            InfoHash = NormalizeInfoHash(conn.InfoHash),
            RemoteIp = conn.RemoteIp,
            RemotePort = conn.RemotePort,
            PeerId = conn.PeerId,
            IsEncrypted = conn.IsEncrypted,
            EventType = "Connected",
            Timestamp = conn.ConnectedAt != default ? conn.ConnectedAt : DateTime.UtcNow,
        }).ToList();

        return Ok(resources);
    }

    [HttpGet("graph")]
    public ActionResult<PeerGraphResource> GetGraph(
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end)
    {
        var startDate = start ?? DateTime.UtcNow.AddHours(-1);
        var endDate = end ?? DateTime.UtcNow;

        var validation = ValidateDateRange(startDate, endDate);
        if (validation != null)
        {
            return validation;
        }

        var logs = _logService.GetByTimeRange(startDate, endDate);

        var nodes = new List<PeerGraphNode>();
        var links = new List<PeerGraphLink>();
        var seenTorrents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPeers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        nodes.Add(new PeerGraphNode
        {
            Id = "seedarr",
            Label = "Seedarr",
            Type = "center",
            IsActive = true,
        });

        // 1. Process connection and disconnection logs chronologically to reconcile peer state
        var activeLogs = new Dictionary<string, PeerConnectionLog>(StringComparer.OrdinalIgnoreCase);
        var disconnectedPeers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var log in logs.OrderBy(l => l.Timestamp))
        {
            var normalizedHash = NormalizeInfoHash(log.InfoHash);
            if (string.IsNullOrEmpty(normalizedHash))
            {
                continue;
            }

            var key = $"{log.RemoteIp}:{log.RemotePort}:{normalizedHash}";
            if (string.Equals(log.EventType, "Connected", StringComparison.OrdinalIgnoreCase))
            {
                activeLogs[key] = log;
                disconnectedPeers.Remove(key);
            }
            else
            {
                activeLogs.Remove(key);
                disconnectedPeers.Add(key);
            }
        }

        foreach (var log in activeLogs.Values)
        {
            var normalizedHash = NormalizeInfoHash(log.InfoHash);
            if (string.IsNullOrEmpty(normalizedHash))
            {
                continue;
            }

            if (seenTorrents.Add(normalizedHash))
            {
                nodes.Add(new PeerGraphNode
                {
                    Id = $"torrent:{normalizedHash}",
                    Label = log.TorrentName ?? (normalizedHash.Length >= 8 ? normalizedHash[..8] : normalizedHash),
                    Type = "torrent",
                    InfoHash = normalizedHash,
                    IsActive = true,
                });

                links.Add(new PeerGraphLink
                {
                    Source = "seedarr",
                    Target = $"torrent:{normalizedHash}",
                    Type = "seeds",
                });
            }

            var peerId = $"{log.RemoteIp}:{log.RemotePort}";
            var peerKey = $"{peerId}:{normalizedHash}";
            if (seenPeers.Add(peerKey))
            {
                nodes.Add(new PeerGraphNode
                {
                    Id = $"peer:{peerId}:{normalizedHash}",
                    Label = log.RemoteIp,
                    Type = "peer",
                    InfoHash = normalizedHash,
                    IsEncrypted = log.IsEncrypted,
                    IsActive = true,
                });

                links.Add(new PeerGraphLink
                {
                    Source = $"torrent:{normalizedHash}",
                    Target = $"peer:{peerId}:{normalizedHash}",
                    Type = log.IsEncrypted ? "encrypted" : "plain",
                });
            }
        }

        // 2. Include live torrents and tracked peers if active
        if (_torrentService != null)
        {
            var allTorrents = _torrentService.GetAll();
            foreach (var torrent in allTorrents)
            {
                var hash = NormalizeInfoHash(torrent.InfoHash);
                if (string.IsNullOrEmpty(hash))
                {
                    continue;
                }

                if (seenTorrents.Add(hash))
                {
                    nodes.Add(new PeerGraphNode
                    {
                        Id = $"torrent:{hash}",
                        Label = torrent.Name ?? (hash.Length >= 8 ? hash[..8] : hash),
                        Type = "torrent",
                        InfoHash = hash,
                        IsActive = true,
                    });

                    links.Add(new PeerGraphLink
                    {
                        Source = "seedarr",
                        Target = $"torrent:{hash}",
                        Type = "seeds",
                    });
                }

                // Add tracker database peers
                if (_peerDatabase != null)
                {
                    var trackerPeers = _peerDatabase.GetPeers(hash);
                    foreach (var peer in trackerPeers)
                    {
                        var peerKey = $"{peer.Ip}:{peer.Port}:{hash}";
                        if (disconnectedPeers.Contains(peerKey))
                        {
                            continue;
                        }

                        if (seenPeers.Add(peerKey))
                        {
                            nodes.Add(new PeerGraphNode
                            {
                                Id = $"peer:{peer.Ip}:{peer.Port}:{hash}",
                                Label = peer.Ip,
                                Type = "peer",
                                InfoHash = hash,
                                IsEncrypted = false,
                                IsActive = true,
                            });

                            links.Add(new PeerGraphLink
                            {
                                Source = $"torrent:{hash}",
                                Target = $"peer:{peer.Ip}:{peer.Port}:{hash}",
                                Type = "plain",
                            });
                        }
                    }
                }

                // Add in-memory connection manager peers
                if (_connectionManager != null)
                {
                    var conns = _connectionManager.GetConnections(hash);
                    if (conns != null)
                    {
                        foreach (var conn in conns)
                        {
                            var peerKey = $"{conn.RemoteIp}:{conn.RemotePort}:{hash}";
                            if (disconnectedPeers.Contains(peerKey))
                            {
                                continue;
                            }

                            if (seenPeers.Add(peerKey))
                            {
                                nodes.Add(new PeerGraphNode
                                {
                                    Id = $"peer:{conn.RemoteIp}:{conn.RemotePort}:{hash}",
                                    Label = conn.RemoteIp,
                                    Type = "peer",
                                    InfoHash = hash,
                                    IsEncrypted = conn.IsEncrypted,
                                    IsActive = true,
                                });

                                links.Add(new PeerGraphLink
                                {
                                    Source = $"torrent:{hash}",
                                    Target = $"peer:{conn.RemoteIp}:{conn.RemotePort}:{hash}",
                                    Type = conn.IsEncrypted ? "encrypted" : "plain",
                                });
                            }
                        }
                    }
                }
            }
        }

        // 3. Include any additional tracker database infohashes
        if (_peerDatabase != null)
        {
            var trackedHashes = _peerDatabase.GetAllInfoHashes();
            foreach (var unnormalizedHash in trackedHashes)
            {
                var hash = NormalizeInfoHash(unnormalizedHash);
                if (string.IsNullOrEmpty(hash))
                {
                    continue;
                }

                if (seenTorrents.Add(hash))
                {
                    nodes.Add(new PeerGraphNode
                    {
                        Id = $"torrent:{hash}",
                        Label = hash.Length > 8 ? hash[..8] : hash,
                        Type = "torrent",
                        InfoHash = hash,
                        IsActive = true,
                    });

                    links.Add(new PeerGraphLink
                    {
                        Source = "seedarr",
                        Target = $"torrent:{hash}",
                        Type = "seeds",
                    });
                }

                var trackerPeers = _peerDatabase.GetPeers(unnormalizedHash);
                foreach (var peer in trackerPeers)
                {
                    var peerKey = $"{peer.Ip}:{peer.Port}:{hash}";
                    if (disconnectedPeers.Contains(peerKey))
                    {
                        continue;
                    }

                    if (seenPeers.Add(peerKey))
                    {
                        nodes.Add(new PeerGraphNode
                        {
                            Id = $"peer:{peer.Ip}:{peer.Port}:{hash}",
                            Label = peer.Ip,
                            Type = "peer",
                            InfoHash = hash,
                            IsEncrypted = false,
                            IsActive = true,
                        });

                        links.Add(new PeerGraphLink
                        {
                            Source = $"torrent:{hash}",
                            Target = $"peer:{peer.Ip}:{peer.Port}:{hash}",
                            Type = "plain",
                        });
                    }
                }
            }
        }

        var validNodeIds = new HashSet<string>(nodes.Select(n => n.Id), StringComparer.Ordinal);
        links.RemoveAll(l => !validNodeIds.Contains(l.Source) || !validNodeIds.Contains(l.Target));

        return Ok(new PeerGraphResource
        {
            Nodes = nodes,
            Links = links,
        });
    }

    private static string NormalizeInfoHash(string infoHash)
    {
        return (infoHash ?? string.Empty).Trim().ToLowerInvariant();
    }

    private ActionResult ValidateDateRange(DateTime startDate, DateTime endDate)
    {
        if (startDate > endDate)
        {
            return BadRequest("Start date must be earlier than or equal to end date.");
        }

        if (endDate - startDate > TimeSpan.FromDays(7))
        {
            return BadRequest("Requested time window cannot exceed 7 days.");
        }

        return null;
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
    public bool IsActive { get; set; } = true;
}

public class PeerGraphLink
{
    public string Source { get; set; }
    public string Target { get; set; }
    public string Type { get; set; }
}
