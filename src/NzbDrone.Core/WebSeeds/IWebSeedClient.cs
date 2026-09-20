using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.WebSeeds;

public interface IWebSeedClient
{
    Task<byte[]> DownloadPieceAsync(
        string baseUrl,
        byte[] infoHash,
        int pieceIndex,
        long pieceLength,
        long totalTorrentSize,
        CancellationToken cancellationToken = default);

    Task<byte[]> DownloadBlockAsync(
        string baseUrl,
        byte[] infoHash,
        int pieceIndex,
        int offset,
        int length,
        CancellationToken cancellationToken = default);

    Task<byte[]> DownloadBlockAsync(
        string baseUrl,
        byte[] infoHash,
        int pieceIndex,
        long pieceLength,
        int offset,
        int length,
        CancellationToken cancellationToken = default);

    Task<byte[]> DownloadBlockAsync(
        string url,
        long startByte,
        int length,
        CancellationToken cancellationToken = default);
}
