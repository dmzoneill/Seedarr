using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.WebSeeds;

public interface IWebSeedPieceDownloader
{
    Task<byte[]> DownloadPieceAsync(
        string webSeedUrl,
        int pieceIndex,
        long pieceLength,
        long totalTorrentSize,
        CancellationToken cancellationToken = default);

    bool VerifyPiece(byte[] pieceData, byte[] expectedPieceHash);

    Task<byte[]> DownloadAndVerifyPieceAsync(
        string webSeedUrl,
        int pieceIndex,
        long pieceLength,
        long totalTorrentSize,
        byte[] expectedPieceHash,
        CancellationToken cancellationToken = default);

    Task<byte[]> DownloadAndVerifyPieceAsync(
        string webSeedUrl,
        int pieceIndex,
        long pieceLength,
        long totalTorrentSize,
        byte[] expectedPieceHash,
        string torrentId,
        CancellationToken cancellationToken = default);

    bool ShouldUseWebSeeds(int activePeerCount, bool hasWebSeeds);

    bool ShouldUseWebSeeds(int activePeerCount, bool hasWebSeeds, int degradedPeerThreshold);

    bool IsWebSeedBanned(string webSeedUrl, string torrentId = null);

    int GetCorruptionCount(string webSeedUrl, string torrentId = null);

    void ResetCorruption(string webSeedUrl = null, string torrentId = null);
}
