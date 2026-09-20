using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.WebSeeds;

public class HoffmanWebSeedClient : IWebSeedClient
{
    public const int ChunkSize = 16 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IWebSeedRedirectHandler _redirectHandler;

    public HoffmanWebSeedClient(IWebSeedHttpClientFactory httpClientFactory)
        : this(httpClientFactory, new WebSeedRedirectHandler())
    {
    }

    public HoffmanWebSeedClient(IWebSeedHttpClientFactory httpClientFactory, IWebSeedRedirectHandler redirectHandler)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.GetClient();
        _redirectHandler = redirectHandler ?? new WebSeedRedirectHandler();
    }

    public HoffmanWebSeedClient(HttpClient httpClient)
        : this(httpClient, new WebSeedRedirectHandler())
    {
    }

    public HoffmanWebSeedClient(HttpClient httpClient, IWebSeedRedirectHandler redirectHandler)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _redirectHandler = redirectHandler ?? new WebSeedRedirectHandler();
    }

    public static string BuildUrl(string baseUrl, byte[] infoHash, int pieceIndex, int? offset = null, int? length = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL cannot be null or whitespace.", nameof(baseUrl));
        }

        if (infoHash == null || infoHash.Length != 20)
        {
            throw new ArgumentException("InfoHash must be exactly 20 bytes.", nameof(infoHash));
        }

        if (pieceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), "Piece index must be non-negative.");
        }

        var escapedHash = string.Join(string.Empty, infoHash.Select(b => $"%{b:X2}"));
        var sep = baseUrl.Contains('?') ? "&" : "?";

        var url = $"{baseUrl}{sep}info_hash={escapedHash}&piece={pieceIndex}";

        if (offset.HasValue && length.HasValue)
        {
            if (offset.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be non-negative.");
            }

            if (length.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");
            }

            url += $"&ranges={offset.Value}-{length.Value}";
        }

        return url;
    }

    public async Task<byte[]> DownloadPieceAsync(
        string baseUrl,
        byte[] infoHash,
        int pieceIndex,
        long pieceLength,
        long totalTorrentSize,
        CancellationToken cancellationToken = default)
    {
        if (pieceLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceLength), "Piece length must be greater than zero.");
        }

        if (totalTorrentSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalTorrentSize), "Total torrent size must be greater than zero.");
        }

        var rangeStart = (long)pieceIndex * pieceLength;
        if (rangeStart >= totalTorrentSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), "Piece start exceeds total torrent size.");
        }

        var rangeEnd = Math.Min(rangeStart + pieceLength, totalTorrentSize) - 1;
        var expectedLength = (int)(rangeEnd - rangeStart + 1);

        var url = BuildUrl(baseUrl, infoHash, pieceIndex);
        return await DownloadPayloadAsync(url, expectedLength, cancellationToken);
    }

    public async Task<byte[]> DownloadBlockAsync(
        string baseUrl,
        byte[] infoHash,
        int pieceIndex,
        int offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");
        }

        var url = BuildUrl(baseUrl, infoHash, pieceIndex, offset, length);
        return await DownloadPayloadAsync(url, length, cancellationToken);
    }

    public Task<byte[]> DownloadBlockAsync(
        string baseUrl,
        byte[] infoHash,
        int pieceIndex,
        long pieceLength,
        int offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        return DownloadBlockAsync(baseUrl, infoHash, pieceIndex, offset, length, cancellationToken);
    }

    public Task<byte[]> DownloadBlockAsync(
        string url,
        long startByte,
        int length,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Hoffman (BEP 17) web seeds require info_hash and piece index.");
    }

    private async Task<byte[]> DownloadPayloadAsync(string url, int expectedLength, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        using var response = await _redirectHandler.SendWithRedirectsAsync(
            _httpClient,
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"Hoffman web seed request failed with status code {response.StatusCode} ({response.ReasonPhrase}). Expected 200 OK.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var payload = new byte[expectedLength];
        var buffer = new byte[ChunkSize];
        var totalBytesRead = 0;

        while (totalBytesRead < expectedLength)
        {
            var bytesToRead = Math.Min(buffer.Length, expectedLength - totalBytesRead);
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            Buffer.BlockCopy(buffer, 0, payload, totalBytesRead, bytesRead);
            totalBytesRead += bytesRead;
        }

        if (totalBytesRead != expectedLength)
        {
            throw new InvalidOperationException($"Incomplete payload received from Hoffman web seed: expected {expectedLength} bytes, received {totalBytesRead} bytes.");
        }

        return payload;
    }
}
