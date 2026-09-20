using System;
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

    private readonly HttpClient _httpClient;
    private readonly IWebSeedRedirectHandler _redirectHandler;
    private readonly Logger _logger;

    public long? PieceLength { get; set; }

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

        var endByte = startByte + length - 1;
        var resolvedUrl = _redirectHandler.GetResolvedUrl(url);

        using var request = new HttpRequestMessage(HttpMethod.Get, resolvedUrl);
        request.Headers.Range = new RangeHeaderValue(startByte, endByte);
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        using var response = await _redirectHandler.SendWithRedirectsAsync(
            _httpClient,
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            _logger.Warn("Server does not support byte ranges; returned 200 OK for {0}", url);
            throw new WebSeedException("Server does not support byte ranges; returned 200 OK");
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

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var payload = new byte[length];
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

        return payload;
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
