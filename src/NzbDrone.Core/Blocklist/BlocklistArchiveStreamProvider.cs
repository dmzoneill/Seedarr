using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Blocklist;

public class BlocklistArchiveStreamProvider : IBlocklistArchiveStreamProvider
{
    private readonly BlocklistArchiveStreamOptions _options;
    private readonly Logger _logger;

    public BlocklistArchiveStreamProvider(
        BlocklistArchiveStreamOptions options = null,
        Logger logger = null)
    {
        _options = options ?? new BlocklistArchiveStreamOptions();
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(
        HttpResponseMessage response,
        string url = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        var effectiveUrl = url ?? response.RequestMessage?.RequestUri?.ToString();
        var contentType = response.Content?.Headers.ContentType?.MediaType;
        var contentEncoding = response.Content?.Headers.ContentEncoding?.FirstOrDefault();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await foreach (var line in ReadLinesAsync(stream, effectiveUrl, contentType, contentEncoding, leaveOpen: false, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return line;
            }
        }
    }

    public async IAsyncEnumerable<string> ReadRulesAsync(
        HttpResponseMessage response,
        string url = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        var effectiveUrl = url ?? response.RequestMessage?.RequestUri?.ToString();
        var contentType = response.Content?.Headers.ContentType?.MediaType;
        var contentEncoding = response.Content?.Headers.ContentEncoding?.FirstOrDefault();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await foreach (var rule in ReadRulesAsync(stream, effectiveUrl, contentType, contentEncoding, leaveOpen: false, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return rule;
            }
        }
    }

    public async Task<List<string>> ExtractRulesAsync(
        HttpResponseMessage response,
        string url = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        var effectiveUrl = url ?? response.RequestMessage?.RequestUri?.ToString();
        var contentType = response.Content?.Headers.ContentType?.MediaType;
        var contentEncoding = response.Content?.Headers.ContentEncoding?.FirstOrDefault();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ExtractRulesAsync(stream, effectiveUrl, contentType, contentEncoding, leaveOpen: false, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> ReadRulesAsync(
        Stream stream,
        string url = null,
        string contentType = null,
        string contentEncoding = null,
        bool leaveOpen = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var ruleCount = 0;
        await foreach (var line in ReadLinesAsync(stream, url, contentType, contentEncoding, leaveOpen, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) ||
                trimmed.StartsWith("#", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            ruleCount++;
            if (_options.MaxRuleLines > 0 && ruleCount > _options.MaxRuleLines)
            {
                throw new BlocklistQuotaExceededException(
                    $"Rule count exceeded safety limit of {_options.MaxRuleLines:N0} lines.");
            }

            yield return trimmed;
        }
    }

    public async Task<List<string>> ExtractRulesAsync(
        Stream stream,
        string url = null,
        string contentType = null,
        string contentEncoding = null,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default)
    {
        var list = new List<string>();
        await foreach (var rule in ReadRulesAsync(stream, url, contentType, contentEncoding, leaveOpen, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            list.Add(rule);
        }

        return list;
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(
        Stream stream,
        string url = null,
        string contentType = null,
        string contentEncoding = null,
        bool leaveOpen = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var (format, fullStream) = await SniffFormatAndPrepareStreamAsync(
            stream,
            url,
            contentType,
            contentEncoding,
            leaveOpen,
            cancellationToken).ConfigureAwait(false);

        if (fullStream == null)
        {
            yield break;
        }

        try
        {
            if (format == BlocklistArchiveFormat.GZip)
            {
                await using var gzipStream = new GZipStream(fullStream, CompressionMode.Decompress, leaveOpen: false);
                await using var countingStream = new QuotaCountingStream(gzipStream, _options.MaxUncompressedBytes, leaveOpen: false);
                using var reader = new StreamReader(
                    countingStream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: _options.BufferSize,
                    leaveOpen: false);

                string line;
                while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                {
                    yield return line;
                }
            }
            else if (format == BlocklistArchiveFormat.Zip)
            {
                using var archive = new ZipArchive(fullStream, ZipArchiveMode.Read, leaveOpen: false);
                var validEntries = archive.Entries
                    .Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\') && !IsZipSlip(e.FullName))
                    .ToList();

                if (validEntries.Count == 0)
                {
                    yield break;
                }

                var primaryEntry = SelectPrimaryBlocklistEntry(validEntries);
                if (_options.MaxUncompressedBytes > 0 && primaryEntry.Length > _options.MaxUncompressedBytes)
                {
                    throw new BlocklistQuotaExceededException(
                        $"Zip entry uncompressed size ({primaryEntry.Length} bytes) exceeds safety quota of {_options.MaxUncompressedBytes} bytes.");
                }

#pragma warning disable CA1849
                await using var entryStream = primaryEntry.Open();
#pragma warning restore CA1849
                await using var countingStream = new QuotaCountingStream(entryStream, _options.MaxUncompressedBytes, leaveOpen: false);
                using var reader = new StreamReader(
                    countingStream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: _options.BufferSize,
                    leaveOpen: false);

                string line;
                while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                {
                    yield return line;
                }
            }
            else
            {
                await using var countingStream = new QuotaCountingStream(fullStream, _options.MaxUncompressedBytes, leaveOpen: false);
                using var reader = new StreamReader(
                    countingStream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: _options.BufferSize,
                    leaveOpen: false);

                string line;
                while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                {
                    yield return line;
                }
            }
        }
        finally
        {
            if (leaveOpen && fullStream is PrefixedReadStream)
            {
                // Inner stream was left open by PrefixedReadStream
            }
            else if (!leaveOpen)
            {
                await fullStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public static BlocklistArchiveFormat DetectFormat(
        byte[] header,
        int bytesRead,
        string url = null,
        string contentType = null,
        string contentEncoding = null)
    {
        // 1. Magic bytes check
        if (bytesRead >= 2 && header[0] == 0x1F && header[1] == 0x8B)
        {
            return BlocklistArchiveFormat.GZip;
        }

        if (bytesRead >= 4 && header[0] == 0x50 && header[1] == 0x4B &&
            ((header[2] == 0x03 && header[3] == 0x04) ||
             (header[2] == 0x05 && header[3] == 0x06) ||
             (header[2] == 0x07 && header[3] == 0x08)))
        {
            return BlocklistArchiveFormat.Zip;
        }

        if (bytesRead >= 2 && header[0] == 0x50 && header[1] == 0x4B && IsZipUrlOrContentType(url, contentType))
        {
            return BlocklistArchiveFormat.Zip;
        }

        // 2. Content-Encoding check
        if (!string.IsNullOrWhiteSpace(contentEncoding) &&
            contentEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            return BlocklistArchiveFormat.GZip;
        }

        // 3. Content-Type check
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var mediaType = contentType.Split(';')[0].Trim();
            if (mediaType.Equals("application/gzip", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("application/x-gzip", StringComparison.OrdinalIgnoreCase))
            {
                return BlocklistArchiveFormat.GZip;
            }

            if (mediaType.Equals("application/zip", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("application/x-zip-compressed", StringComparison.OrdinalIgnoreCase))
            {
                return BlocklistArchiveFormat.Zip;
            }
        }

        // 4. URL extension check
        if (!string.IsNullOrWhiteSpace(url))
        {
            var path = url;
            var queryIdx = path.IndexOf('?');
            if (queryIdx >= 0)
            {
                path = path[..queryIdx];
            }

            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".gzip", StringComparison.OrdinalIgnoreCase))
            {
                return BlocklistArchiveFormat.GZip;
            }

            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return BlocklistArchiveFormat.Zip;
            }
        }

        return BlocklistArchiveFormat.PlainText;
    }

    public static bool IsZipSlip(string entryFullName)
    {
        if (string.IsNullOrWhiteSpace(entryFullName))
        {
            return true;
        }

        var normalized = entryFullName.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
        {
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment == "..");
    }

    internal static ZipArchiveEntry SelectPrimaryBlocklistEntry(List<ZipArchiveEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return null;
        }

        if (entries.Count == 1)
        {
            return entries[0];
        }

        static int ScoreEntry(ZipArchiveEntry entry)
        {
            var name = entry.Name;
            var isNonRule = name.StartsWith("readme", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("license", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("notice", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("info", StringComparison.OrdinalIgnoreCase);

            var ext = Path.GetExtension(name).ToLowerInvariant();
            var extScore = ext switch
            {
                ".p2p" => 100,
                ".dat" => 90,
                ".txt" => 80,
                ".netfilter" => 70,
                ".cidr" => 60,
                _ => 10
            };

            return (isNonRule ? 0 : 1000) + extScore;
        }

        return entries
            .OrderByDescending(ScoreEntry)
            .ThenByDescending(e => e.Length)
            .First();
    }

    private static bool IsZipUrlOrContentType(string url, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var mediaType = contentType.Split(';')[0].Trim();
            if (mediaType.Equals("application/zip", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("application/x-zip-compressed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            var path = url;
            var queryIdx = path.IndexOf('?');
            if (queryIdx >= 0)
            {
                path = path[..queryIdx];
            }

            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<(BlocklistArchiveFormat Format, Stream FullStream)> SniffFormatAndPrepareStreamAsync(
        Stream stream,
        string url,
        string contentType,
        string contentEncoding,
        bool leaveOpen,
        CancellationToken cancellationToken)
    {
        var header = new byte[4];
        var bytesRead = 0;

        if (stream.CanSeek)
        {
            var startPos = stream.Position;
            while (bytesRead < 4)
            {
                var read = await stream.ReadAsync(header.AsMemory(bytesRead, 4 - bytesRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            if (bytesRead == 0)
            {
                return (BlocklistArchiveFormat.PlainText, null);
            }

            stream.Seek(startPos, SeekOrigin.Begin);
            var format = DetectFormat(header, bytesRead, url, contentType, contentEncoding);
            return (format, stream);
        }
        else
        {
            while (bytesRead < 4)
            {
                var read = await stream.ReadAsync(header.AsMemory(bytesRead, 4 - bytesRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            if (bytesRead == 0)
            {
                return (BlocklistArchiveFormat.PlainText, null);
            }

            var format = DetectFormat(header, bytesRead, url, contentType, contentEncoding);
            var prefixedStream = new PrefixedReadStream(header, bytesRead, stream, leaveOpen);
            return (format, prefixedStream);
        }
    }

    internal sealed class QuotaCountingStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly long _maxBytes;
        private readonly bool _leaveOpen;
        private long _totalBytesRead;

        public QuotaCountingStream(Stream innerStream, long maxBytes, bool leaveOpen = false)
        {
            _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            _maxBytes = maxBytes;
            _leaveOpen = leaveOpen;
        }

        public long TotalBytesRead => _totalBytesRead;

        public override bool CanRead => _innerStream.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _innerStream.Length;

        public override long Position
        {
            get => _totalBytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _innerStream.Read(buffer, offset, count);
            CheckLimit(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _innerStream.Read(buffer);
            CheckLimit(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            CheckLimit(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            CheckLimit(read);
            return read;
        }

        public override void Flush() => _innerStream.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen)
            {
                _innerStream.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_leaveOpen)
            {
                await _innerStream.DisposeAsync().ConfigureAwait(false);
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }

        private void CheckLimit(int bytesJustRead)
        {
            if (bytesJustRead <= 0)
            {
                return;
            }

            _totalBytesRead += bytesJustRead;
            if (_maxBytes > 0 && _totalBytesRead > _maxBytes)
            {
                throw new BlocklistQuotaExceededException(
                    $"Uncompressed blocklist size exceeded safety quota of {_maxBytes:N0} bytes.");
            }
        }
    }

    internal sealed class PrefixedReadStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly int _prefixLength;
        private readonly Stream _innerStream;
        private readonly bool _leaveOpen;
        private int _prefixOffset;

        public PrefixedReadStream(byte[] prefix, int prefixLength, Stream innerStream, bool leaveOpen = false)
        {
            _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
            _prefixLength = prefixLength;
            _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            _leaveOpen = leaveOpen;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _innerStream.Length + _prefixLength;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - offset < count)
            {
                throw new ArgumentException("Offset and count exceed buffer boundaries.");
            }

            if (count == 0)
            {
                return 0;
            }

            var bytesFromPrefix = 0;
            if (_prefixOffset < _prefixLength)
            {
                bytesFromPrefix = Math.Min(count, _prefixLength - _prefixOffset);
                Buffer.BlockCopy(_prefix, _prefixOffset, buffer, offset, bytesFromPrefix);
                _prefixOffset += bytesFromPrefix;
                offset += bytesFromPrefix;
                count -= bytesFromPrefix;
            }

            if (count == 0)
            {
                return bytesFromPrefix;
            }

            var bytesFromInner = _innerStream.Read(buffer, offset, count);
            return bytesFromPrefix + bytesFromInner;
        }

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            var bytesFromPrefix = 0;
            if (_prefixOffset < _prefixLength)
            {
                bytesFromPrefix = Math.Min(buffer.Length, _prefixLength - _prefixOffset);
                _prefix.AsSpan(_prefixOffset, bytesFromPrefix).CopyTo(buffer);
                _prefixOffset += bytesFromPrefix;
                buffer = buffer[bytesFromPrefix..];
            }

            if (buffer.IsEmpty)
            {
                return bytesFromPrefix;
            }

            var bytesFromInner = _innerStream.Read(buffer);
            return bytesFromPrefix + bytesFromInner;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - offset < count)
            {
                throw new ArgumentException("Offset and count exceed buffer boundaries.");
            }

            if (count == 0)
            {
                return 0;
            }

            var bytesFromPrefix = 0;
            if (_prefixOffset < _prefixLength)
            {
                bytesFromPrefix = Math.Min(count, _prefixLength - _prefixOffset);
                Buffer.BlockCopy(_prefix, _prefixOffset, buffer, offset, bytesFromPrefix);
                _prefixOffset += bytesFromPrefix;
                offset += bytesFromPrefix;
                count -= bytesFromPrefix;
            }

            if (count == 0)
            {
                return bytesFromPrefix;
            }

            var bytesFromInner = await _innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            return bytesFromPrefix + bytesFromInner;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            var bytesFromPrefix = 0;
            if (_prefixOffset < _prefixLength)
            {
                bytesFromPrefix = Math.Min(buffer.Length, _prefixLength - _prefixOffset);
                _prefix.AsSpan(_prefixOffset, bytesFromPrefix).CopyTo(buffer.Span);
                _prefixOffset += bytesFromPrefix;
                buffer = buffer[bytesFromPrefix..];
            }

            if (buffer.IsEmpty)
            {
                return bytesFromPrefix;
            }

            var bytesFromInner = await _innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            return bytesFromPrefix + bytesFromInner;
        }

        public override void Flush() => _innerStream.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen)
            {
                _innerStream.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_leaveOpen)
            {
                await _innerStream.DisposeAsync().ConfigureAwait(false);
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
