using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Bandwidth;

public interface IBandwidthLimiter
{
    ITokenBucket GlobalUploadBucket { get; }
    ITokenBucket GlobalDownloadBucket { get; }

    void SetGlobalLimits(long uploadBytesPerSec, long downloadBytesPerSec);
    void SetTorrentLimits(string infoHash, long uploadBytesPerSec, long downloadBytesPerSec);
    void SetPeerLimits(string peerId, long uploadBytesPerSec, long downloadBytesPerSec);

    void RemoveTorrent(string infoHash);
    void RemovePeer(string peerId);

    bool HasUploadLimit(string infoHash = null, string peerId = null);
    bool HasDownloadLimit(string infoHash = null, string peerId = null);

    ITokenBucket GetTorrentUploadBucket(string infoHash);
    ITokenBucket GetTorrentDownloadBucket(string infoHash);
    ITokenBucket GetPeerUploadBucket(string peerId);
    ITokenBucket GetPeerDownloadBucket(string peerId);

    ITokenBucket GetOrCreateTorrentUploadBucket(string infoHash);
    ITokenBucket GetOrCreateTorrentDownloadBucket(string infoHash);
    ITokenBucket GetOrCreatePeerUploadBucket(string peerId);
    ITokenBucket GetOrCreatePeerDownloadBucket(string peerId);

    bool TryConsumeUpload(string infoHash, string peerId, long bytes);
    bool TryConsumeDownload(string infoHash, string peerId, long bytes);

    void ConsumeUpload(string infoHash, string peerId, long bytes);
    void ConsumeDownload(string infoHash, string peerId, long bytes);

    Task ConsumeUploadAsync(string infoHash, string peerId, long bytes, CancellationToken cancellationToken = default);
    Task ConsumeDownloadAsync(string infoHash, string peerId, long bytes, CancellationToken cancellationToken = default);

    void Reset();
}
