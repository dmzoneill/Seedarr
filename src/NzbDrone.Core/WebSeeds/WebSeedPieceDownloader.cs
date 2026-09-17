using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
}

public class WebSeedPieceDownloader : IWebSeedPieceDownloader
{
    public const int ChunkSize = 16 * 1024;

    private readonly HttpClient _httpClient;

    public WebSeedPieceDownloader(IWebSeedHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.GetClient();
    }

    public WebSeedPieceDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public static (long Start, long End) CalculateByteRange(int pieceIndex, long pieceLength, long totalTorrentSize)
    {
        if (pieceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), "Piece index must be non-negative.");
        }

        if (pieceLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceLength), "Piece length must be positive.");
        }

        if (totalTorrentSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalTorrentSize), "Total torrent size must be positive.");
        }

        var rangeStart = (long)pieceIndex * pieceLength;
        if (rangeStart >= totalTorrentSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), "Piece start exceeds total torrent size.");
        }

        var rangeEnd = Math.Min(rangeStart + pieceLength, totalTorrentSize) - 1;
        return (rangeStart, rangeEnd);
    }

    public async Task<byte[]> DownloadPieceAsync(
        string webSeedUrl,
        int pieceIndex,
        long pieceLength,
        long totalTorrentSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webSeedUrl))
        {
            throw new ArgumentException("Web seed URL cannot be null or whitespace.", nameof(webSeedUrl));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var (rangeStart, rangeEnd) = CalculateByteRange(pieceIndex, pieceLength, totalTorrentSize);
        var expectedLength = rangeEnd - rangeStart + 1;

        if (expectedLength > int.MaxValue)
        {
            throw new InvalidOperationException($"Piece length ({expectedLength} bytes) exceeds maximum supported buffer size.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, webSeedUrl);
        request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode != HttpStatusCode.PartialContent && response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"Web seed request failed with status code {response.StatusCode} ({response.ReasonPhrase}). Expected 206 Partial Content or 200 OK.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var pieceBytes = new byte[expectedLength];
        var buffer = new byte[ChunkSize];
        var totalBytesRead = 0;

        while (totalBytesRead < expectedLength)
        {
            var bytesToRead = (int)Math.Min(buffer.Length, expectedLength - totalBytesRead);
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            Buffer.BlockCopy(buffer, 0, pieceBytes, totalBytesRead, bytesRead);
            totalBytesRead += bytesRead;
        }

        if (totalBytesRead != expectedLength)
        {
            throw new InvalidOperationException($"Incomplete piece received from web seed: expected {expectedLength} bytes, but received {totalBytesRead} bytes.");
        }

        return pieceBytes;
    }
}
