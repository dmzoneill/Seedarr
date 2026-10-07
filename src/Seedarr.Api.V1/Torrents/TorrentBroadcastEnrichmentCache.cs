using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace Seedarr.Api.V1.Torrents;

public interface ITorrentBroadcastEnrichmentCache
{
    void Invalidate(int torrentId, string infoHash = null);

    List<TrackerEntry> GetOrAddTrackers(int torrentId, Func<List<TrackerEntry>> loader);

    TorrentMediaMetadata GetOrAddMediaMetadata(int torrentId, Func<TorrentMediaMetadata> loader);

    (DownloadHistory History, MediaMetadata ParsedMetadata) GetOrAddHistory(
        string infoHash,
        Func<(DownloadHistory History, MediaMetadata ParsedMetadata)> loader);
}

public class TorrentBroadcastEnrichmentCache : ITorrentBroadcastEnrichmentCache
{
    private static readonly TimeSpan TrackerTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MediaMetadataTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HistoryTtl = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<int, (List<TrackerEntry> Trackers, DateTime Expiry)> _trackersCache = new();
    private readonly ConcurrentDictionary<int, (TorrentMediaMetadata Metadata, DateTime Expiry)> _mediaMetaCache = new();
    private readonly ConcurrentDictionary<string, (DownloadHistory History, MediaMetadata ParsedMetadata, DateTime Expiry)> _historyCache = new();

    public void Invalidate(int torrentId, string infoHash = null)
    {
        _trackersCache.TryRemove(torrentId, out _);
        _mediaMetaCache.TryRemove(torrentId, out _);
        if (!string.IsNullOrEmpty(infoHash))
        {
            _historyCache.TryRemove(infoHash, out _);
        }
    }

    public List<TrackerEntry> GetOrAddTrackers(int torrentId, Func<List<TrackerEntry>> loader)
    {
        var now = DateTime.UtcNow;
        if (_trackersCache.TryGetValue(torrentId, out var entry) && entry.Expiry > now)
        {
            return entry.Trackers;
        }

        var trackers = loader() ?? new List<TrackerEntry>();
        _trackersCache[torrentId] = (trackers, now.Add(TrackerTtl));
        return trackers;
    }

    public TorrentMediaMetadata GetOrAddMediaMetadata(int torrentId, Func<TorrentMediaMetadata> loader)
    {
        var now = DateTime.UtcNow;
        if (_mediaMetaCache.TryGetValue(torrentId, out var entry) && entry.Expiry > now)
        {
            return entry.Metadata;
        }

        var metadata = loader();
        _mediaMetaCache[torrentId] = (metadata, now.Add(MediaMetadataTtl));
        return metadata;
    }

    public (DownloadHistory History, MediaMetadata ParsedMetadata) GetOrAddHistory(
        string infoHash,
        Func<(DownloadHistory History, MediaMetadata ParsedMetadata)> loader)
    {
        var now = DateTime.UtcNow;
        if (_historyCache.TryGetValue(infoHash, out var entry) && entry.Expiry > now)
        {
            return (entry.History, entry.ParsedMetadata);
        }

        var loaded = loader();
        _historyCache[infoHash] = (loaded.History, loaded.ParsedMetadata, now.Add(HistoryTtl));
        return loaded;
    }
}
