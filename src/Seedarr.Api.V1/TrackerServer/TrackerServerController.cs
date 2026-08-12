using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer;
using Seedarr.Http;

namespace Seedarr.Api.V1.TrackerServer;

[V1ApiController("trackerserver")]
public class TrackerServerController : Controller
{
    private readonly IPeerDatabase _peerDatabase;
    private readonly ITorrentService _torrentService;
    private readonly IConfigService _configService;
    private readonly IDownloadHistoryRepository _downloadHistoryRepository;
    private static readonly DateTime StartTime = DateTime.UtcNow;
    private static long _totalAnnounces;
    private static long _totalScrapes;

    public TrackerServerController(
        IPeerDatabase peerDatabase,
        ITorrentService torrentService,
        IConfigService configService,
        IDownloadHistoryRepository downloadHistoryRepository = null)
    {
        _peerDatabase = peerDatabase;
        _torrentService = torrentService;
        _configService = configService;
        _downloadHistoryRepository = downloadHistoryRepository;
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var internalCount = 0;
        var infoHashes = _peerDatabase.GetAllInfoHashes();
        var allTorrents = _torrentService.GetAll();
        var knownHashes = new HashSet<string>(allTorrents.Where(t => !string.IsNullOrEmpty(t.InfoHash)).Select(t => t.InfoHash), StringComparer.OrdinalIgnoreCase);

        foreach (var hash in infoHashes)
        {
            if (knownHashes.Contains(hash))
            {
                internalCount++;
            }
        }

        return Ok(new
        {
            totalTorrents = _peerDatabase.GetTotalTorrentCount(),
            internalTorrents = internalCount,
            totalPeers = _peerDatabase.GetTotalPeerCount(),
            totalAnnounces = _totalAnnounces,
            totalScrapes = _totalScrapes,
            uptime = (long)(DateTime.UtcNow - StartTime).TotalSeconds
        });
    }

    [HttpGet("torrents")]
    public IActionResult GetTrackedTorrents()
    {
        var infoHashes = _peerDatabase.GetAllInfoHashes();
        var allTorrents = _torrentService.GetAll();
        var torrentsByHash = new Dictionary<string, Torrent>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in allTorrents)
        {
            if (!string.IsNullOrEmpty(t.InfoHash))
            {
                torrentsByHash[t.InfoHash] = t;
            }
        }

        var result = infoHashes.Select(hash =>
        {
            var stats = _peerDatabase.GetStats(hash);
            var peers = _peerDatabase.GetPeers(hash);
            var lastAnnounce = peers.Count > 0
                ? peers.Max(p => p.LastAnnounce)
                : (DateTime?)null;

            torrentsByHash.TryGetValue(hash, out var torrent);

            string posterUrl = null;
            string fanartUrl = null;
            string mediaTitle = null;
            int? year = null;
            double? rating = null;
            var genres = new List<string>();
            var source = torrent != null ? (torrent.IsPrivate ? "Private Tracker" : "Public Tracker") : "External";

            if (_downloadHistoryRepository != null)
            {
                try
                {
                    var hist = _downloadHistoryRepository.FindByInfoHash(hash);
                    if (hist != null)
                    {
                        source = hist.Source ?? source;
                        if (!string.IsNullOrEmpty(hist.DataJson))
                        {
                            var meta = JsonSerializer.Deserialize<MediaMetadata>(
                                hist.DataJson,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (meta != null)
                            {
                                posterUrl = meta.PosterUrl;
                                fanartUrl = meta.FanartUrl;
                                mediaTitle = meta.Title;
                                year = meta.Year;
                                rating = meta.Rating;
                                genres = meta.Genres ?? new();
                            }
                        }
                    }
                }
                catch
                {
                    // Graceful fallback
                }
            }

            return new
            {
                infoHash = hash,
                name = torrent?.Name ?? mediaTitle ?? hash,
                peerCount = peers.Count,
                seeders = stats.Complete,
                leechers = stats.Incomplete,
                completed = stats.Downloaded,
                uploaded = torrent?.Uploaded ?? 0L,
                downloaded = torrent?.Downloaded ?? 0L,
                totalSize = torrent?.TotalSize ?? 0L,
                ratio = torrent?.Ratio ?? 0.0,
                isInternal = torrent != null,
                lastActivity = lastAnnounce,
                posterUrl,
                fanartUrl,
                mediaTitle,
                year,
                rating,
                genres,
                source
            };
        }).ToList();

        return Ok(result);
    }

    [HttpGet("torrents/{infoHash}/peers")]
    public IActionResult GetPeersForTorrent(string infoHash)
    {
        var peers = _peerDatabase.GetPeers(infoHash);
        var result = peers.Select(p => new
        {
            ip = p.Ip,
            port = p.Port,
            peerId = p.PeerId,
            lastAnnounce = p.LastAnnounce
        }).ToList();

        return Ok(result);
    }

    public static void IncrementAnnounces()
    {
        Interlocked.Increment(ref _totalAnnounces);
    }

    public static void IncrementScrapes()
    {
        Interlocked.Increment(ref _totalScrapes);
    }
}
