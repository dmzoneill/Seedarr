using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Bandwidth;

public class BandwidthLimiter : IBandwidthLimiter
{
    private readonly ConcurrentDictionary<string, ITokenBucket> _torrentUploadBuckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ITokenBucket> _torrentDownloadBuckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ITokenBucket> _peerUploadBuckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ITokenBucket> _peerDownloadBuckets = new(StringComparer.OrdinalIgnoreCase);

    public static BandwidthLimiter Instance { get; } = new();

    public static Action<long, long> PaceWaitHandler { get; set; } = BandwidthPacer.PaceWait;

    public ITokenBucket GlobalUploadBucket { get; }
    public ITokenBucket GlobalDownloadBucket { get; }

    public BandwidthLimiter(long globalUploadBytesPerSec = 0, long globalDownloadBytesPerSec = 0)
    {
        GlobalUploadBucket = new TokenBucket(globalUploadBytesPerSec);
        GlobalDownloadBucket = new TokenBucket(globalDownloadBytesPerSec);
    }

    public void SetGlobalLimits(long uploadBytesPerSec, long downloadBytesPerSec)
    {
        GlobalUploadBucket.SetRate(uploadBytesPerSec);
        GlobalDownloadBucket.SetRate(downloadBytesPerSec);
    }

    public void SetTorrentLimits(string infoHash, long uploadBytesPerSec, long downloadBytesPerSec)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        _torrentUploadBuckets.AddOrUpdate(
            infoHash,
            _ => new TokenBucket(uploadBytesPerSec),
            (_, bucket) =>
            {
                bucket.SetRate(uploadBytesPerSec);
                return bucket;
            });

        _torrentDownloadBuckets.AddOrUpdate(
            infoHash,
            _ => new TokenBucket(downloadBytesPerSec),
            (_, bucket) =>
            {
                bucket.SetRate(downloadBytesPerSec);
                return bucket;
            });
    }

    public void SetPeerLimits(string peerId, long uploadBytesPerSec, long downloadBytesPerSec)
    {
        if (string.IsNullOrWhiteSpace(peerId))
        {
            return;
        }

        _peerUploadBuckets.AddOrUpdate(
            peerId,
            _ => new TokenBucket(uploadBytesPerSec),
            (_, bucket) =>
            {
                bucket.SetRate(uploadBytesPerSec);
                return bucket;
            });

        _peerDownloadBuckets.AddOrUpdate(
            peerId,
            _ => new TokenBucket(downloadBytesPerSec),
            (_, bucket) =>
            {
                bucket.SetRate(downloadBytesPerSec);
                return bucket;
            });
    }

    public void RemoveTorrent(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        _torrentUploadBuckets.TryRemove(infoHash, out _);
        _torrentDownloadBuckets.TryRemove(infoHash, out _);
    }

    public void RemovePeer(string peerId)
    {
        if (string.IsNullOrWhiteSpace(peerId))
        {
            return;
        }

        _peerUploadBuckets.TryRemove(peerId, out _);
        _peerDownloadBuckets.TryRemove(peerId, out _);
    }

    public bool HasUploadLimit(string infoHash = null, string peerId = null)
    {
        if (!GlobalUploadBucket.IsUnlimited)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(infoHash) && _torrentUploadBuckets.TryGetValue(infoHash, out var tb) && !tb.IsUnlimited)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(peerId) && _peerUploadBuckets.TryGetValue(peerId, out var pb) && !pb.IsUnlimited)
        {
            return true;
        }

        return false;
    }

    public bool HasDownloadLimit(string infoHash = null, string peerId = null)
    {
        if (!GlobalDownloadBucket.IsUnlimited)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(infoHash) && _torrentDownloadBuckets.TryGetValue(infoHash, out var tb) && !tb.IsUnlimited)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(peerId) && _peerDownloadBuckets.TryGetValue(peerId, out var pb) && !pb.IsUnlimited)
        {
            return true;
        }

        return false;
    }

    public ITokenBucket GetTorrentUploadBucket(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        return _torrentUploadBuckets.TryGetValue(infoHash, out var b) ? b : null;
    }

    public ITokenBucket GetTorrentDownloadBucket(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        return _torrentDownloadBuckets.TryGetValue(infoHash, out var b) ? b : null;
    }

    public ITokenBucket GetPeerUploadBucket(string peerId)
    {
        if (string.IsNullOrWhiteSpace(peerId))
        {
            return null;
        }

        return _peerUploadBuckets.TryGetValue(peerId, out var b) ? b : null;
    }

    public ITokenBucket GetPeerDownloadBucket(string peerId)
    {
        if (string.IsNullOrWhiteSpace(peerId))
        {
            return null;
        }

        return _peerDownloadBuckets.TryGetValue(peerId, out var b) ? b : null;
    }

    public ITokenBucket GetOrCreateTorrentUploadBucket(string infoHash)
    {
        return _torrentUploadBuckets.GetOrAdd(infoHash, _ => new TokenBucket(0));
    }

    public ITokenBucket GetOrCreateTorrentDownloadBucket(string infoHash)
    {
        return _torrentDownloadBuckets.GetOrAdd(infoHash, _ => new TokenBucket(0));
    }

    public ITokenBucket GetOrCreatePeerUploadBucket(string peerId)
    {
        return _peerUploadBuckets.GetOrAdd(peerId, _ => new TokenBucket(0));
    }

    public ITokenBucket GetOrCreatePeerDownloadBucket(string peerId)
    {
        return _peerDownloadBuckets.GetOrAdd(peerId, _ => new TokenBucket(0));
    }

    public bool TryConsumeUpload(string infoHash, string peerId, long bytes)
    {
        if (bytes <= 0)
        {
            return true;
        }

        ITokenBucket peerBucket = null;
        if (!string.IsNullOrEmpty(peerId) && _peerUploadBuckets.TryGetValue(peerId, out var pb) && !pb.IsUnlimited)
        {
            peerBucket = pb;
        }

        ITokenBucket torrentBucket = null;
        if (!string.IsNullOrEmpty(infoHash) && _torrentUploadBuckets.TryGetValue(infoHash, out var tb) && !tb.IsUnlimited)
        {
            torrentBucket = tb;
        }

        var globalBucket = !GlobalUploadBucket.IsUnlimited ? GlobalUploadBucket : null;

        // Bottom-up acquisition: Peer -> Torrent -> Global
        if (peerBucket != null && !peerBucket.TryConsume(bytes))
        {
            return false;
        }

        if (torrentBucket != null && !torrentBucket.TryConsume(bytes))
        {
            peerBucket?.Refund(bytes);
            return false;
        }

        if (globalBucket != null && !globalBucket.TryConsume(bytes))
        {
            torrentBucket?.Refund(bytes);
            peerBucket?.Refund(bytes);
            return false;
        }

        return true;
    }

    public bool TryConsumeDownload(string infoHash, string peerId, long bytes)
    {
        if (bytes <= 0)
        {
            return true;
        }

        ITokenBucket peerBucket = null;
        if (!string.IsNullOrEmpty(peerId) && _peerDownloadBuckets.TryGetValue(peerId, out var pb) && !pb.IsUnlimited)
        {
            peerBucket = pb;
        }

        ITokenBucket torrentBucket = null;
        if (!string.IsNullOrEmpty(infoHash) && _torrentDownloadBuckets.TryGetValue(infoHash, out var tb) && !tb.IsUnlimited)
        {
            torrentBucket = tb;
        }

        var globalBucket = !GlobalDownloadBucket.IsUnlimited ? GlobalDownloadBucket : null;

        // Bottom-up acquisition: Peer -> Torrent -> Global
        if (peerBucket != null && !peerBucket.TryConsume(bytes))
        {
            return false;
        }

        if (torrentBucket != null && !torrentBucket.TryConsume(bytes))
        {
            peerBucket?.Refund(bytes);
            return false;
        }

        if (globalBucket != null && !globalBucket.TryConsume(bytes))
        {
            torrentBucket?.Refund(bytes);
            peerBucket?.Refund(bytes);
            return false;
        }

        return true;
    }

    public void ConsumeUpload(string infoHash, string peerId, long bytes)
    {
        if (bytes <= 0 || !HasUploadLimit(infoHash, peerId))
        {
            return;
        }

        while (!TryConsumeUpload(infoHash, peerId, bytes))
        {
            var waitTicks = CalculateUploadWaitTicks(infoHash, peerId, bytes);
            PaceWait(waitTicks);
        }
    }

    public void ConsumeDownload(string infoHash, string peerId, long bytes)
    {
        if (bytes <= 0 || !HasDownloadLimit(infoHash, peerId))
        {
            return;
        }

        while (!TryConsumeDownload(infoHash, peerId, bytes))
        {
            var waitTicks = CalculateDownloadWaitTicks(infoHash, peerId, bytes);
            PaceWait(waitTicks);
        }
    }

    public async Task ConsumeUploadAsync(string infoHash, string peerId, long bytes, CancellationToken cancellationToken = default)
    {
        if (bytes <= 0 || !HasUploadLimit(infoHash, peerId))
        {
            return;
        }

        while (!TryConsumeUpload(infoHash, peerId, bytes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var waitTicks = CalculateUploadWaitTicks(infoHash, peerId, bytes);
            var waitMs = (int)Math.Ceiling((double)waitTicks * 1000.0 / Stopwatch.Frequency);

            if (waitMs > 1)
            {
                await Task.Delay(waitMs, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                PaceWait(waitTicks);
            }
        }
    }

    public async Task ConsumeDownloadAsync(string infoHash, string peerId, long bytes, CancellationToken cancellationToken = default)
    {
        if (bytes <= 0 || !HasDownloadLimit(infoHash, peerId))
        {
            return;
        }

        while (!TryConsumeDownload(infoHash, peerId, bytes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var waitTicks = CalculateDownloadWaitTicks(infoHash, peerId, bytes);
            var waitMs = (int)Math.Ceiling((double)waitTicks * 1000.0 / Stopwatch.Frequency);

            if (waitMs > 1)
            {
                await Task.Delay(waitMs, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                PaceWait(waitTicks);
            }
        }
    }

    public void Reset()
    {
        GlobalUploadBucket.Reset();
        GlobalDownloadBucket.Reset();
        foreach (var bucket in _torrentUploadBuckets.Values)
        {
            bucket.Reset();
        }

        foreach (var bucket in _torrentDownloadBuckets.Values)
        {
            bucket.Reset();
        }

        foreach (var bucket in _peerUploadBuckets.Values)
        {
            bucket.Reset();
        }

        foreach (var bucket in _peerDownloadBuckets.Values)
        {
            bucket.Reset();
        }
    }

    private long CalculateUploadWaitTicks(string infoHash, string peerId, long bytes)
    {
        var maxTicks = 1L;

        if (!GlobalUploadBucket.IsUnlimited)
        {
            maxTicks = Math.Max(maxTicks, CalculateBucketWaitTicks(GlobalUploadBucket, bytes));
        }

        if (!string.IsNullOrEmpty(infoHash) && _torrentUploadBuckets.TryGetValue(infoHash, out var tb) && !tb.IsUnlimited)
        {
            maxTicks = Math.Max(maxTicks, CalculateBucketWaitTicks(tb, bytes));
        }

        if (!string.IsNullOrEmpty(peerId) && _peerUploadBuckets.TryGetValue(peerId, out var pb) && !pb.IsUnlimited)
        {
            maxTicks = Math.Max(maxTicks, CalculateBucketWaitTicks(pb, bytes));
        }

        return maxTicks;
    }

    private long CalculateDownloadWaitTicks(string infoHash, string peerId, long bytes)
    {
        var maxTicks = 1L;

        if (!GlobalDownloadBucket.IsUnlimited)
        {
            maxTicks = Math.Max(maxTicks, CalculateBucketWaitTicks(GlobalDownloadBucket, bytes));
        }

        if (!string.IsNullOrEmpty(infoHash) && _torrentDownloadBuckets.TryGetValue(infoHash, out var tb) && !tb.IsUnlimited)
        {
            maxTicks = Math.Max(maxTicks, CalculateBucketWaitTicks(tb, bytes));
        }

        if (!string.IsNullOrEmpty(peerId) && _peerDownloadBuckets.TryGetValue(peerId, out var pb) && !pb.IsUnlimited)
        {
            maxTicks = Math.Max(maxTicks, CalculateBucketWaitTicks(pb, bytes));
        }

        return maxTicks;
    }

    private static long CalculateBucketWaitTicks(ITokenBucket bucket, long bytes)
    {
        var rate = bucket.Rate;
        if (rate <= 0)
        {
            return 0;
        }

        var available = bucket.AvailableTokens;
        var deficit = Math.Max(1.0, bytes - available);
        return (long)Math.Ceiling(deficit * Stopwatch.Frequency / rate);
    }

    private static void PaceWait(long waitTicks)
    {
        var start = Stopwatch.GetTimestamp();
        PaceWaitHandler(start, waitTicks);
    }
}
