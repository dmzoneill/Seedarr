using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.WebSeeds;

public class WebSeedHostState
{
    public string Host { get; }
    public bool IsDead { get; set; }
    public string DeadReason { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int CircuitBreakerTripCount { get; set; }
    public DateTime? CooldownUntil { get; set; }
    public string CooldownReason { get; set; }

    public WebSeedHostState(string host)
    {
        Host = host;
    }
}

public class WebSeedFileInfo
{
    public string Path { get; set; }
    public long Size { get; set; }
    public bool IsPaddingFile { get; set; }
    public string Url { get; set; }

    public WebSeedFileInfo()
    {
    }

    public WebSeedFileInfo(string path, long size, string url = null, bool isPaddingFile = false)
    {
        Path = path;
        Size = size;
        Url = url;
        IsPaddingFile = isPaddingFile;
    }
}

public class WebSeedFileSlice
{
    public WebSeedFileInfo File { get; set; }
    public string FilePath => File?.Path;
    public string Url { get; set; }
    public long FileOffset { get; set; }
    public int Length { get; set; }
    public int BufferOffset { get; set; }
    public bool IsPadding => File?.IsPaddingFile ?? false;
}

public class WebSeedClient : IWebSeedClient
{
    public const int ChunkSize = 16 * 1024;

    public static readonly TimeSpan[] DefaultCircuitBreakerBackoffs =
    {
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(300)
    };

    private readonly HttpClient _httpClient;
    private readonly IWebSeedRedirectHandler _redirectHandler;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, WebSeedHostState> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public long? PieceLength { get; set; }
    public int CircuitBreakerThreshold { get; set; } = 5;
    public TimeSpan DefaultRetryAfterCooldown { get; set; } = TimeSpan.FromSeconds(30);
    public IList<TimeSpan> CircuitBreakerBackoffs { get; set; } = new List<TimeSpan>(DefaultCircuitBreakerBackoffs);
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public static string GetHostKey(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Authority.ToLowerInvariant();
        }

        return url.Trim().ToLowerInvariant();
    }

    public bool IsSeedDead(string url)
    {
        var host = GetHostKey(url);
        if (_hosts.TryGetValue(host, out var state))
        {
            lock (state)
            {
                return state.IsDead;
            }
        }

        return false;
    }

    public bool IsWebSeedDead(string url) => IsSeedDead(url);

    public bool IsSeedCoolingDown(string url)
    {
        var host = GetHostKey(url);
        if (_hosts.TryGetValue(host, out var state))
        {
            lock (state)
            {
                if (state.CooldownUntil.HasValue)
                {
                    if (state.CooldownUntil.Value > UtcNow())
                    {
                        return true;
                    }

                    state.CooldownUntil = null;
                    state.CooldownReason = null;
                }
            }
        }

        return false;
    }

    public bool IsWebSeedCoolingDown(string url) => IsSeedCoolingDown(url);

    public bool IsSeedAvailable(string url) => !IsSeedDead(url) && !IsSeedCoolingDown(url);

    public bool IsWebSeedAvailable(string url) => IsSeedAvailable(url);

    public TimeSpan? GetCooldownRemaining(string url)
    {
        var host = GetHostKey(url);
        if (_hosts.TryGetValue(host, out var state))
        {
            lock (state)
            {
                if (state.CooldownUntil.HasValue)
                {
                    var diff = state.CooldownUntil.Value - UtcNow();
                    return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
                }
            }
        }

        return null;
    }

    public int GetConsecutiveFailures(string url)
    {
        var host = GetHostKey(url);
        return _hosts.TryGetValue(host, out var state) ? state.ConsecutiveFailures : 0;
    }

    public int GetCircuitBreakerTripCount(string url)
    {
        var host = GetHostKey(url);
        return _hosts.TryGetValue(host, out var state) ? state.CircuitBreakerTripCount : 0;
    }

    public WebSeedHostState GetHostState(string url)
    {
        var host = GetHostKey(url);
        return _hosts.TryGetValue(host, out var state) ? state : null;
    }

    public void MarkDead(string url, string reason = null)
    {
        var host = GetHostKey(url);
        var state = _hosts.GetOrAdd(host, h => new WebSeedHostState(h));
        lock (state)
        {
            state.IsDead = true;
            state.DeadReason = reason ?? "Marked dead";
        }
    }

    public void Reset(string url = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            _hosts.Clear();
        }
        else
        {
            var host = GetHostKey(url);
            _hosts.TryRemove(host, out _);
        }
    }

    private TimeSpan CalculateRetryAfterCooldown(HttpResponseHeaders headers)
    {
        if (headers?.RetryAfter != null)
        {
            if (headers.RetryAfter.Delta.HasValue)
            {
                return headers.RetryAfter.Delta.Value > TimeSpan.Zero ? headers.RetryAfter.Delta.Value : TimeSpan.FromSeconds(1);
            }

            if (headers.RetryAfter.Date.HasValue)
            {
                var diff = headers.RetryAfter.Date.Value - UtcNow();
                return diff > TimeSpan.Zero ? diff : TimeSpan.FromSeconds(1);
            }
        }

        if (headers != null && headers.TryGetValues("Retry-After", out var values))
        {
            var val = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(val))
            {
                if (int.TryParse(val, out var seconds))
                {
                    return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(1);
                }

                if (DateTimeOffset.TryParse(val, out var date))
                {
                    var diff = date - UtcNow();
                    return diff > TimeSpan.Zero ? diff : TimeSpan.FromSeconds(1);
                }
            }
        }

        return DefaultRetryAfterCooldown;
    }

    private TimeSpan CalculateCircuitBreakerCooldown(int tripCount)
    {
        var backoffs = CircuitBreakerBackoffs;
        if (backoffs == null || backoffs.Count == 0)
        {
            return TimeSpan.FromSeconds(15);
        }

        var index = Math.Clamp(tripCount - 1, 0, backoffs.Count - 1);
        return backoffs[index];
    }

    private void RecordTransientFailure(string host, string error)
    {
        var state = _hosts.GetOrAdd(host, h => new WebSeedHostState(h));
        lock (state)
        {
            state.ConsecutiveFailures++;
            if (state.ConsecutiveFailures >= CircuitBreakerThreshold)
            {
                state.CircuitBreakerTripCount++;
                var cooldown = CalculateCircuitBreakerCooldown(state.CircuitBreakerTripCount);
                state.CooldownUntil = UtcNow().Add(cooldown);
                state.CooldownReason = $"Circuit breaker tripped after {state.ConsecutiveFailures} consecutive failures (trip #{state.CircuitBreakerTripCount}): {error}";

                _logger.Warn(
                    "Circuit breaker tripped for web seed '{0}': {1} consecutive failures (trip #{2}). Cooling down for {3}s until {4:O}.",
                    host,
                    state.ConsecutiveFailures,
                    state.CircuitBreakerTripCount,
                    cooldown.TotalSeconds,
                    state.CooldownUntil);
            }
            else
            {
                _logger.Warn(
                    "Web seed '{0}' recorded failure {1}/{2}: {3}",
                    host,
                    state.ConsecutiveFailures,
                    CircuitBreakerThreshold,
                    error);
            }
        }
    }

    private void RecordSuccess(string host)
    {
        if (_hosts.TryGetValue(host, out var state))
        {
            lock (state)
            {
                state.ConsecutiveFailures = 0;
                state.CircuitBreakerTripCount = 0;
                state.CooldownUntil = null;
                state.CooldownReason = null;
            }
        }
    }

    public WebSeedClient(IWebSeedHttpClientFactory httpClientFactory)
        : this(httpClientFactory, new WebSeedRedirectHandler(), null)
    {
    }

    public WebSeedClient(IWebSeedHttpClientFactory httpClientFactory, IWebSeedRedirectHandler redirectHandler)
        : this(httpClientFactory, redirectHandler, null)
    {
    }

    public WebSeedClient(IWebSeedHttpClientFactory httpClientFactory, Logger logger)
        : this(httpClientFactory, new WebSeedRedirectHandler(), logger)
    {
    }

    public WebSeedClient(IWebSeedHttpClientFactory httpClientFactory, IWebSeedRedirectHandler redirectHandler, Logger logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.GetClient();
        _redirectHandler = redirectHandler ?? new WebSeedRedirectHandler();
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public WebSeedClient(HttpClient httpClient)
        : this(httpClient, new WebSeedRedirectHandler(), null)
    {
    }

    public WebSeedClient(HttpClient httpClient, IWebSeedRedirectHandler redirectHandler)
        : this(httpClient, redirectHandler, null)
    {
    }

    public WebSeedClient(HttpClient httpClient, Logger logger)
        : this(httpClient, new WebSeedRedirectHandler(), logger)
    {
    }

    public WebSeedClient(HttpClient httpClient, IWebSeedRedirectHandler redirectHandler, Logger logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _redirectHandler = redirectHandler ?? new WebSeedRedirectHandler();
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public static (long Start, long End) CalculateByteRange(int pieceIndex, long pieceLength, int offset, int length)
    {
        if (pieceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), "Piece index must be non-negative.");
        }

        if (pieceLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceLength), "Piece length must be positive.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be non-negative.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.");
        }

        var start = ((long)pieceIndex * pieceLength) + offset;
        var end = start + length - 1;
        return (start, end);
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

    public async Task<byte[]> DownloadBlockAsync(
        string url,
        long startByte,
        int length,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or whitespace.", nameof(url));
        }

        if (startByte < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startByte), "Start byte must be non-negative.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var host = GetHostKey(url);
        var state = _hosts.GetOrAdd(host, h => new WebSeedHostState(h));

        lock (state)
        {
            if (state.IsDead)
            {
                _logger.Warn("Web seed request to '{0}' aborted: host '{1}' is marked dead ({2}).", url, host, state.DeadReason);
                throw new WebSeedException($"Web seed '{host}' is permanently disabled/dead ({state.DeadReason}).");
            }

            if (state.CooldownUntil.HasValue)
            {
                var now = UtcNow();
                if (state.CooldownUntil.Value > now)
                {
                    var remaining = state.CooldownUntil.Value - now;
                    _logger.Warn(
                        "Web seed request to '{0}' aborted: host '{1}' is cooling down for another {2:F1}s ({3}).",
                        url,
                        host,
                        remaining.TotalSeconds,
                        state.CooldownReason);
                    throw new WebSeedException($"Web seed '{host}' is cooling down for another {remaining.TotalSeconds:F1}s until {state.CooldownUntil.Value:O} ({state.CooldownReason}).");
                }

                state.CooldownUntil = null;
                state.CooldownReason = null;
            }
        }

        var endByte = startByte + length - 1;
        var resolvedUrl = _redirectHandler.GetResolvedUrl(url);

        using var request = new HttpRequestMessage(HttpMethod.Get, resolvedUrl);
        request.Headers.Range = new RangeHeaderValue(startByte, endByte);
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        HttpResponseMessage response;
        try
        {
            response = await _redirectHandler.SendWithRedirectsAsync(
                _httpClient,
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            RecordTransientFailure(host, $"Network error: {ex.Message}");
            throw;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.Warn("Server does not support byte ranges; returned 200 OK for {0}", url);
                throw new WebSeedException("Server does not support byte ranges; returned 200 OK");
            }

            // Permanent errors: 401 Unauthorized, 403 Forbidden, 404 Not Found, 410 Gone
            if (response.StatusCode is HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound
                or HttpStatusCode.Gone)
            {
                var reason = $"Received permanent HTTP error: {(int)response.StatusCode} {response.ReasonPhrase}";
                lock (state)
                {
                    state.IsDead = true;
                    state.DeadReason = reason;
                }

                _logger.Warn("Web seed '{0}' permanently disabled due to {1}", host, reason);

                throw new HttpRequestException(
                    $"Web seed request to '{url}' failed with status code {response.StatusCode} ({response.ReasonPhrase}). Expected 206 Partial Content.",
                    null,
                    response.StatusCode);
            }

            // Rate-limit errors: 429 Too Many Requests, 503 Service Unavailable with Retry-After
            if (response.StatusCode is (HttpStatusCode)429 ||
                (response.StatusCode == HttpStatusCode.ServiceUnavailable && (response.Headers.RetryAfter != null || response.Headers.Contains("Retry-After"))))
            {
                var cooldown = CalculateRetryAfterCooldown(response.Headers);
                var reason = $"Rate limited: HTTP {(int)response.StatusCode} {response.ReasonPhrase}";

                lock (state)
                {
                    state.CooldownUntil = UtcNow().Add(cooldown);
                    state.CooldownReason = reason;
                }

                _logger.Warn(
                    "Web seed '{0}' rate limited ({1}). Cooling down for {2}s until {3:O}.",
                    host,
                    reason,
                    cooldown.TotalSeconds,
                    state.CooldownUntil);

                throw new HttpRequestException(
                    $"Web seed request to '{url}' failed with status code {response.StatusCode} ({response.ReasonPhrase}). Expected 206 Partial Content.",
                    null,
                    response.StatusCode);
            }

            // Server errors: 5xx
            if ((int)response.StatusCode >= 500 && (int)response.StatusCode <= 599)
            {
                RecordTransientFailure(host, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

                throw new HttpRequestException(
                    $"Web seed request to '{url}' failed with status code {response.StatusCode} ({response.ReasonPhrase}). Expected 206 Partial Content.",
                    null,
                    response.StatusCode);
            }

            // 416 Range Not Satisfiable
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                throw new HttpRequestException(
                    $"Web seed request to '{url}' failed with status code 416 (Requested Range Not Satisfiable). The requested range [{startByte}..{endByte}] may exceed file boundaries or remote file size differs from torrent metainfo.",
                    null,
                    response.StatusCode);
            }

            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                throw new HttpRequestException(
                    $"Web seed request to '{url}' failed with status code {response.StatusCode} ({response.ReasonPhrase}). Expected 206 Partial Content.",
                    null,
                    response.StatusCode);
            }

            if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != length)
            {
                throw new WebSeedException($"Unexpected Content-Length from web seed: expected {length} bytes, but server indicated {response.Content.Headers.ContentLength.Value} bytes.");
            }

            byte[] payload;
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

                payload = new byte[length];
                var totalBytesRead = 0;

                while (totalBytesRead < length)
                {
                    var bytesRead = await stream.ReadAsync(payload.AsMemory(totalBytesRead, length - totalBytesRead), cancellationToken);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    totalBytesRead += bytesRead;
                }

                if (totalBytesRead != length)
                {
                    throw new InvalidOperationException($"Incomplete block received from web seed: expected {length} bytes, but received {totalBytesRead} bytes.");
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not WebSeedException && ex is not InvalidOperationException)
            {
                RecordTransientFailure(host, $"Stream read error: {ex.Message}");
                throw;
            }

            RecordSuccess(host);

            return payload;
        }
    }

    public Task<byte[]> DownloadBlockAsync(
        string url,
        int pieceIndex,
        long pieceLength,
        int offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        var (startByte, _) = CalculateByteRange(pieceIndex, pieceLength, offset, length);
        return DownloadBlockAsync(url, startByte, length, cancellationToken);
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
        return DownloadBlockAsync(baseUrl, pieceIndex, pieceLength, offset, length, cancellationToken);
    }

    public Task<byte[]> DownloadBlockAsync(
        string baseUrl,
        byte[] infoHash,
        int pieceIndex,
        int offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        if (pieceIndex == 0)
        {
            return DownloadBlockAsync(baseUrl, offset, length, cancellationToken);
        }

        if (PieceLength.HasValue)
        {
            var startByte = ((long)pieceIndex * PieceLength.Value) + offset;
            return DownloadBlockAsync(baseUrl, startByte, length, cancellationToken);
        }

        throw new InvalidOperationException("Piece length is required to calculate startByte for pieceIndex > 0 in BEP 19. Specify PieceLength on the client or use the overload with pieceLength.");
    }

    public async Task<byte[]> DownloadPieceAsync(
        string baseUrl,
        byte[] infoHash,
        int pieceIndex,
        long pieceLength,
        long totalTorrentSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL cannot be null or whitespace.", nameof(baseUrl));
        }

        var (rangeStart, rangeEnd) = CalculateByteRange(pieceIndex, pieceLength, totalTorrentSize);
        var expectedLength = (int)(rangeEnd - rangeStart + 1);

        return await DownloadBlockAsync(baseUrl, rangeStart, expectedLength, cancellationToken);
    }

    public static List<WebSeedFileSlice> CalculateFileSlices(
        long startByte,
        int length,
        IReadOnlyList<WebSeedFileInfo> files,
        string baseUrl = null)
    {
        if (startByte < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startByte), "Start byte must be non-negative.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(files);

        var requestedEnd = startByte + length - 1;
        var slices = new List<WebSeedFileSlice>();

        long currentFileStart = 0;

        foreach (var file in files)
        {
            if (file == null)
            {
                continue;
            }

            var fileSize = file.Size;
            if (fileSize <= 0)
            {
                continue;
            }

            var currentFileEnd = currentFileStart + fileSize - 1;

            if (startByte <= currentFileEnd && requestedEnd >= currentFileStart)
            {
                var overlapStart = Math.Max(startByte, currentFileStart);
                var overlapEnd = Math.Min(requestedEnd, currentFileEnd);
                var sliceLength = (int)(overlapEnd - overlapStart + 1);

                var fileOffset = overlapStart - currentFileStart;
                var bufferOffset = (int)(overlapStart - startByte);

                var fileUrl = file.Url;
                if (string.IsNullOrEmpty(fileUrl) && !string.IsNullOrWhiteSpace(baseUrl))
                {
                    fileUrl = WebSeedUrlResolver.ResolveWebSeedUrl(baseUrl, null, file.Path);
                }

                slices.Add(new WebSeedFileSlice
                {
                    File = file,
                    Url = fileUrl,
                    FileOffset = fileOffset,
                    Length = sliceLength,
                    BufferOffset = bufferOffset
                });
            }

            currentFileStart += fileSize;

            if (currentFileStart > requestedEnd)
            {
                break;
            }
        }

        return slices;
    }

    public static List<WebSeedFileSlice> CalculateFileSlices(
        long startByte,
        int length,
        IEnumerable<TorrentFile> files,
        string baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        var fileInfos = files.Select(f => new WebSeedFileInfo(f.Path, f.Size, null, f.IsPaddingFile)).ToList();
        return CalculateFileSlices(startByte, length, fileInfos, baseUrl);
    }

    public static List<WebSeedFileSlice> CalculateFileSlices(
        long startByte,
        int length,
        IEnumerable<ParsedTorrentFile> files,
        string baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        var fileInfos = files.Select(f => new WebSeedFileInfo(f.Path, f.Size, null, f.IsPaddingFile)).ToList();
        return CalculateFileSlices(startByte, length, fileInfos, baseUrl);
    }

    public async Task<byte[]> DownloadMultiFileBlockAsync(
        string baseUrl,
        IReadOnlyList<WebSeedFileInfo> files,
        long torrentOffset,
        int length,
        CancellationToken cancellationToken = default)
    {
        if (torrentOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(torrentOffset), "Torrent offset must be non-negative.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(files);

        var slices = CalculateFileSlices(torrentOffset, length, files, baseUrl);

        var totalSliced = 0;
        foreach (var slice in slices)
        {
            totalSliced += slice.Length;
        }

        if (totalSliced != length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"Requested range [{torrentOffset}..{torrentOffset + length - 1}] is not fully covered by files (covered {totalSliced} of {length} bytes).");
        }

        var resultBuffer = new byte[length];

        foreach (var slice in slices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (slice.IsPadding)
            {
                Array.Clear(resultBuffer, slice.BufferOffset, slice.Length);
                continue;
            }

            if (string.IsNullOrWhiteSpace(slice.Url))
            {
                throw new InvalidOperationException($"Cannot download slice for file '{slice.FilePath}': URL could not be resolved.");
            }

            var sliceBytes = await DownloadBlockAsync(slice.Url, slice.FileOffset, slice.Length, cancellationToken);
            Buffer.BlockCopy(sliceBytes, 0, resultBuffer, slice.BufferOffset, slice.Length);
        }

        return resultBuffer;
    }

    public Task<byte[]> DownloadMultiFileBlockAsync(
        string baseUrl,
        IReadOnlyList<WebSeedFileInfo> files,
        int pieceIndex,
        long pieceLength,
        int offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        var (torrentOffset, _) = CalculateByteRange(pieceIndex, pieceLength, offset, length);
        return DownloadMultiFileBlockAsync(baseUrl, files, torrentOffset, length, cancellationToken);
    }

    public Task<byte[]> DownloadMultiFileBlockAsync(
        string baseUrl,
        IEnumerable<TorrentFile> files,
        long torrentOffset,
        int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        var fileInfos = files.Select(f => new WebSeedFileInfo(f.Path, f.Size, null, f.IsPaddingFile)).ToList();
        return DownloadMultiFileBlockAsync(baseUrl, fileInfos, torrentOffset, length, cancellationToken);
    }

    public Task<byte[]> DownloadMultiFileBlockAsync(
        string baseUrl,
        IEnumerable<ParsedTorrentFile> files,
        long torrentOffset,
        int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        var fileInfos = files.Select(f => new WebSeedFileInfo(f.Path, f.Size, null, f.IsPaddingFile)).ToList();
        return DownloadMultiFileBlockAsync(baseUrl, fileInfos, torrentOffset, length, cancellationToken);
    }

    public Task<byte[]> DownloadMultiFileBlockAsync(
        string baseUrl,
        IEnumerable<TorrentFile> files,
        int pieceIndex,
        long pieceLength,
        int offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        var (torrentOffset, _) = CalculateByteRange(pieceIndex, pieceLength, offset, length);
        return DownloadMultiFileBlockAsync(baseUrl, files, torrentOffset, length, cancellationToken);
    }

    public Task<byte[]> DownloadMultiFileBlockAsync(
        string baseUrl,
        IEnumerable<ParsedTorrentFile> files,
        int pieceIndex,
        long pieceLength,
        int offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        var (torrentOffset, _) = CalculateByteRange(pieceIndex, pieceLength, offset, length);
        return DownloadMultiFileBlockAsync(baseUrl, files, torrentOffset, length, cancellationToken);
    }
}
