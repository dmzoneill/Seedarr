using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.WebSeeds;

public class WebSeedPieceDownloader : IWebSeedPieceDownloader
{
    public const int ChunkSize = 16 * 1024;
    public const int DefaultCorruptionThreshold = 3;
    public const int DefaultDegradedPeerThreshold = 3;

    private readonly HttpClient _httpClient;
    private readonly IWebSeedRedirectHandler _redirectHandler;
    private readonly ConcurrentDictionary<string, int> _corruptionFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _bannedWebSeeds = new(StringComparer.OrdinalIgnoreCase);

    public int CorruptionThreshold { get; set; } = DefaultCorruptionThreshold;
    public int DegradedPeerThreshold { get; set; } = DefaultDegradedPeerThreshold;

    public WebSeedPieceDownloader()
        : this(new WebSeedHttpClientFactory(), new WebSeedRedirectHandler())
    {
    }

    public WebSeedPieceDownloader(IWebSeedHttpClientFactory httpClientFactory)
        : this(httpClientFactory, new WebSeedRedirectHandler())
    {
    }

    public WebSeedPieceDownloader(IWebSeedHttpClientFactory httpClientFactory, IWebSeedRedirectHandler redirectHandler)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.GetClient();
        _redirectHandler = redirectHandler ?? new WebSeedRedirectHandler();
    }

    public WebSeedPieceDownloader(HttpClient httpClient)
        : this(httpClient, new WebSeedRedirectHandler())
    {
    }

    public WebSeedPieceDownloader(HttpClient httpClient, IWebSeedRedirectHandler redirectHandler)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _redirectHandler = redirectHandler ?? new WebSeedRedirectHandler();
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

        var resolvedUrl = _redirectHandler.GetResolvedUrl(webSeedUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, resolvedUrl);
        request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        using var response = await _redirectHandler.SendWithRedirectsAsync(
            _httpClient,
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

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

    public bool VerifyPiece(byte[] pieceData, byte[] expectedPieceHash)
    {
        if (pieceData == null || expectedPieceHash == null)
        {
            return false;
        }

        if (expectedPieceHash.Length != 20)
        {
            return false;
        }

        var computedHash = SHA1.HashData(pieceData);
        return CryptographicOperations.FixedTimeEquals(computedHash, expectedPieceHash);
    }

    public Task<byte[]> DownloadAndVerifyPieceAsync(
        string webSeedUrl,
        int pieceIndex,
        long pieceLength,
        long totalTorrentSize,
        byte[] expectedPieceHash,
        CancellationToken cancellationToken = default)
    {
        return DownloadAndVerifyPieceAsync(
            webSeedUrl,
            pieceIndex,
            pieceLength,
            totalTorrentSize,
            expectedPieceHash,
            torrentId: null,
            cancellationToken);
    }

    public async Task<byte[]> DownloadAndVerifyPieceAsync(
        string webSeedUrl,
        int pieceIndex,
        long pieceLength,
        long totalTorrentSize,
        byte[] expectedPieceHash,
        string torrentId,
        CancellationToken cancellationToken = default)
    {
        if (IsWebSeedBanned(webSeedUrl, torrentId))
        {
            throw new WebSeedException($"Web seed '{webSeedUrl}' is banned due to piece corruption.");
        }

        var pieceBytes = await DownloadPieceAsync(webSeedUrl, pieceIndex, pieceLength, totalTorrentSize, cancellationToken);

        if (!VerifyPiece(pieceBytes, expectedPieceHash))
        {
            // Discard buffer immediately
            Array.Clear(pieceBytes, 0, pieceBytes.Length);
            pieceBytes = null;

            RecordCorruptionFailure(webSeedUrl, torrentId);

            throw new WebSeedException($"Piece {pieceIndex} downloaded from web seed '{webSeedUrl}' failed SHA-1 verification.");
        }

        return pieceBytes;
    }

    public bool ShouldUseWebSeeds(int activePeerCount, bool hasWebSeeds)
    {
        return ShouldUseWebSeeds(activePeerCount, hasWebSeeds, DegradedPeerThreshold);
    }

    public bool ShouldUseWebSeeds(int activePeerCount, bool hasWebSeeds, int degradedPeerThreshold)
    {
        if (!hasWebSeeds)
        {
            return false;
        }

        return activePeerCount < degradedPeerThreshold;
    }

    public bool IsWebSeedBanned(string webSeedUrl, string torrentId = null)
    {
        if (string.IsNullOrWhiteSpace(webSeedUrl))
        {
            return false;
        }

        var normalizedUrl = webSeedUrl.Trim();

        if (!string.IsNullOrWhiteSpace(torrentId))
        {
            var key = BuildKey(normalizedUrl, torrentId);
            if (_bannedWebSeeds.TryGetValue(key, out var banned) && banned)
            {
                return true;
            }
        }

        var globalKey = BuildKey(normalizedUrl, null);
        if (_bannedWebSeeds.TryGetValue(globalKey, out var globalBanned) && globalBanned)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(torrentId))
        {
            return _bannedWebSeeds.Any(kvp => kvp.Value && kvp.Key.EndsWith($"|{normalizedUrl}", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    public bool IsWebSeedBlacklisted(string webSeedUrl, string torrentId = null) => IsWebSeedBanned(webSeedUrl, torrentId);

    public int GetCorruptionCount(string webSeedUrl, string torrentId = null)
    {
        if (string.IsNullOrWhiteSpace(webSeedUrl))
        {
            return 0;
        }

        var normalizedUrl = webSeedUrl.Trim();

        if (!string.IsNullOrWhiteSpace(torrentId))
        {
            var key = BuildKey(normalizedUrl, torrentId);
            if (_corruptionFailures.TryGetValue(key, out var count))
            {
                return count;
            }
        }

        var globalKey = BuildKey(normalizedUrl, null);
        if (_corruptionFailures.TryGetValue(globalKey, out var globalCount))
        {
            return globalCount;
        }

        if (string.IsNullOrWhiteSpace(torrentId))
        {
            return _corruptionFailures
                .Where(kvp => kvp.Key.EndsWith($"|{normalizedUrl}", StringComparison.OrdinalIgnoreCase))
                .Sum(kvp => kvp.Value);
        }

        return 0;
    }

    public void BanWebSeed(string webSeedUrl, string torrentId = null)
    {
        var key = BuildKey(webSeedUrl, torrentId);
        _bannedWebSeeds[key] = true;
    }

    public void BlacklistWebSeed(string webSeedUrl, string torrentId = null) => BanWebSeed(webSeedUrl, torrentId);

    public void ResetCorruption(string webSeedUrl = null, string torrentId = null)
    {
        if (string.IsNullOrWhiteSpace(webSeedUrl))
        {
            _corruptionFailures.Clear();
            _bannedWebSeeds.Clear();
            return;
        }

        var normalizedUrl = webSeedUrl.Trim();

        if (!string.IsNullOrWhiteSpace(torrentId))
        {
            var key = BuildKey(normalizedUrl, torrentId);
            _corruptionFailures.TryRemove(key, out _);
            _bannedWebSeeds.TryRemove(key, out _);
        }
        else
        {
            var globalKey = BuildKey(normalizedUrl, null);
            _corruptionFailures.TryRemove(globalKey, out _);
            _bannedWebSeeds.TryRemove(globalKey, out _);

            var matchingKeys = _corruptionFailures.Keys
                .Where(k => k.EndsWith($"|{normalizedUrl}", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var k in matchingKeys)
            {
                _corruptionFailures.TryRemove(k, out _);
                _bannedWebSeeds.TryRemove(k, out _);
            }
        }
    }

    public void Reset(string webSeedUrl = null) => ResetCorruption(webSeedUrl, null);

    private static string BuildKey(string webSeedUrl, string torrentId = null)
    {
        var normalizedUrl = webSeedUrl?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(torrentId))
        {
            return $"{torrentId.Trim()}|{normalizedUrl}";
        }

        return normalizedUrl;
    }

    private void RecordCorruptionFailure(string webSeedUrl, string torrentId)
    {
        var key = BuildKey(webSeedUrl, torrentId);
        var failures = _corruptionFailures.AddOrUpdate(key, 1, (_, count) => count + 1);

        if (failures >= CorruptionThreshold)
        {
            _bannedWebSeeds[key] = true;
        }
    }
}
