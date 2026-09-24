using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32.SafeHandles;

namespace NzbDrone.Core.Torrents;

public class MultiFilePieceStorage : IMultiFilePieceStorage, Storage.IMultiFileStorage, Storage.IPieceStorage
{
    private readonly IPieceBoundaryResolver _resolver;
    private readonly IFileHandlePool _handlePool;
    private readonly bool _ownsHandlePool;
    private string _baseDirectory;
    private List<TorrentFile> _files;
    private int _pieceLength;
    private long _totalSize;
    private long[] _prefixOffsets;
    private bool _disposed;

    public IFileHandlePool HandlePool => _handlePool;
    public string BaseDirectory => _baseDirectory;
    public IReadOnlyList<TorrentFile> Files => _files;
    public int PieceLength => _pieceLength;
    public long TotalSize => _totalSize;
    public IReadOnlyList<long> PrefixOffsets => _prefixOffsets;
    public int PieceCount => _pieceLength > 0 && _totalSize > 0 ? (int)((_totalSize + _pieceLength - 1) / _pieceLength) : 0;

    public MultiFilePieceStorage(IPieceBoundaryResolver resolver = null, IFileHandlePool handlePool = null)
    {
        _resolver = resolver ?? new PieceBoundaryResolver();
        if (handlePool != null)
        {
            _handlePool = handlePool;
            _ownsHandlePool = false;
        }
        else
        {
            _handlePool = new FileHandlePool();
            _ownsHandlePool = true;
        }

        _files = new List<TorrentFile>();
        _prefixOffsets = Array.Empty<long>();
    }

    public MultiFilePieceStorage(
        string baseDirectory,
        IList<TorrentFile> files,
        int pieceLength,
        long totalSize = 0,
        IFileHandlePool handlePool = null)
        : this(null, handlePool)
    {
        Initialize(baseDirectory, files, pieceLength, totalSize);
    }

    public MultiFilePieceStorage(
        Torrent torrent,
        IList<TorrentFile> files,
        string baseDirectory = null,
        IFileHandlePool handlePool = null)
        : this(null, handlePool)
    {
        Initialize(torrent, files, baseDirectory);
    }

    public MultiFilePieceStorage()
        : this(null, null)
    {
    }

    public void Initialize(string baseDirectory, IList<TorrentFile> files, int pieceLength, long totalSize = 0)
    {
        _baseDirectory = baseDirectory;
        _pieceLength = pieceLength;

        if (files != null && files.Count > 0)
        {
            _files = new List<TorrentFile>(files.Count);
            _prefixOffsets = new long[files.Count];
            long currentOffset = 0;

            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                _files.Add(file);
                _prefixOffsets[i] = currentOffset;

                if (file != null)
                {
                    file.ByteOffset = currentOffset;
                    currentOffset += Math.Max(0L, file.Size);
                }
            }

            _totalSize = totalSize > 0 ? totalSize : currentOffset;
        }
        else
        {
            _files = new List<TorrentFile>();
            _prefixOffsets = Array.Empty<long>();
            _totalSize = totalSize > 0 ? totalSize : 0L;
        }
    }

    public void Initialize(Torrent torrent, IList<TorrentFile> files, string baseDirectory = null)
    {
        var basePath = !string.IsNullOrWhiteSpace(baseDirectory)
            ? baseDirectory
            : (!string.IsNullOrWhiteSpace(torrent?.SavePath) ? torrent.SavePath : string.Empty);

        var pieceLength = torrent?.PieceLength ?? 0;
        var totalSize = torrent?.TotalSize > 0
            ? torrent.TotalSize
            : (files != null && files.Count > 0 ? files.Where(f => f != null && f.Size > 0).Sum(f => f.Size) : 0L);

        Initialize(basePath, files, pieceLength, totalSize);
    }

    public int FindStartingFileIndex(long globalStart)
    {
        if (_files == null || _files.Count == 0)
        {
            return 0;
        }

        var low = 0;
        var high = _files.Count - 1;
        var startIndex = _files.Count;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var fileEnd = _prefixOffsets[mid] + Math.Max(0L, _files[mid]?.Size ?? 0L);

            if (fileEnd > globalStart)
            {
                startIndex = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return startIndex;
    }

    public static bool IsPadding(TorrentFile file)
    {
        if (file == null)
        {
            return false;
        }

        if (file.IsPaddingFile)
        {
            return true;
        }

        return IsPaddingPath(file.Path);
    }

    public static bool IsPaddingPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);

        return normalized.StartsWith(".pad/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(".pad\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.pad/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\.pad\\", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("_____padding_file_", StringComparison.OrdinalIgnoreCase);
    }

    public byte[] ReadBlock(int pieceIndex, int begin, int length)
    {
        ValidateBlockParameters(pieceIndex, begin, length);

        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        var buffer = new byte[length];
        ReadBlockInternal(pieceIndex, begin, buffer);
        return buffer;
    }

    public int ReadBlock(int pieceIndex, int begin, Memory<byte> destinationBuffer)
    {
        ValidateBlockParameters(pieceIndex, begin, destinationBuffer.Length);

        if (destinationBuffer.Length == 0)
        {
            return 0;
        }

        return ReadBlockInternal(pieceIndex, begin, destinationBuffer);
    }

    public void WriteBlock(int pieceIndex, int begin, byte[] data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        WriteBlock(pieceIndex, begin, (ReadOnlyMemory<byte>)data);
    }

    public void WriteBlock(int pieceIndex, int begin, ReadOnlyMemory<byte> data)
    {
        ValidateBlockParameters(pieceIndex, begin, data.Length);

        if (data.Length == 0)
        {
            return;
        }

        var globalStart = (long)pieceIndex * _pieceLength + begin;
        var globalEnd = globalStart + data.Length;

        var startIndex = FindStartingFileIndex(globalStart);

        for (var i = startIndex; i < _files.Count; i++)
        {
            var file = _files[i];
            if (file == null || file.Size <= 0)
            {
                continue;
            }

            var fileStart = _prefixOffsets[i];
            var fileEnd = fileStart + file.Size;

            if (fileStart >= globalEnd)
            {
                break;
            }

            var intersectStart = Math.Max(globalStart, fileStart);
            var intersectEnd = Math.Min(globalEnd, fileEnd);

            if (intersectStart < intersectEnd)
            {
                var fileOffset = intersectStart - fileStart;
                var sliceLength = (int)(intersectEnd - intersectStart);
                var bufferOffset = (int)(intersectStart - globalStart);

                if (IsPadding(file))
                {
                    // BEP 47: Transparently skip writes to padding files
                    continue;
                }

                var filePath = ResolveFilePath(_baseDirectory, file.Path);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    continue;
                }

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                try
                {
                    var handle = _handlePool.GetOrCreateHandle(filePath, writeAccess: true);
                    RandomAccess.Write(handle, data.Slice(bufferOffset, sliceLength).Span, fileOffset);
                }
                catch (ObjectDisposedException)
                {
                    var handle = _handlePool.GetOrCreateHandle(filePath, writeAccess: true);
                    RandomAccess.Write(handle, data.Slice(bufferOffset, sliceLength).Span, fileOffset);
                }
            }
        }
    }

    public byte[] ReadPiece(int pieceIndex)
    {
        if (pieceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        }

        var pieceStart = (long)pieceIndex * _pieceLength;
        if (pieceStart >= _totalSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        }

        var currentPieceSize = (int)Math.Min((long)_pieceLength, _totalSize - pieceStart);
        return ReadBlock(pieceIndex, 0, currentPieceSize);
    }

    public int ReadPiece(int pieceIndex, Memory<byte> destinationBuffer)
    {
        if (pieceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        }

        var pieceStart = (long)pieceIndex * _pieceLength;
        if (pieceStart >= _totalSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex));
        }

        var currentPieceSize = (int)Math.Min((long)_pieceLength, _totalSize - pieceStart);
        var bytesToRead = Math.Min(destinationBuffer.Length, currentPieceSize);
        return ReadBlock(pieceIndex, 0, destinationBuffer.Slice(0, bytesToRead));
    }

    public void WritePiece(int pieceIndex, ReadOnlyMemory<byte> data)
    {
        WriteBlock(pieceIndex, 0, data);
    }

    public void WritePiece(int pieceIndex, byte[] data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        WriteBlock(pieceIndex, 0, (ReadOnlyMemory<byte>)data);
    }

    public int ReadPiece(Torrent torrent, IList<TorrentFile> files, int pieceIndex, Memory<byte> destinationBuffer, string baseDirectory = null)
    {
        destinationBuffer.Span.Clear();

        if (torrent == null || files == null || files.Count == 0 || pieceIndex < 0)
        {
            return 0;
        }

        var pieceLength = torrent.PieceLength;
        if (pieceLength <= 0)
        {
            return 0;
        }

        var totalSize = torrent.TotalSize > 0
            ? torrent.TotalSize
            : files.Where(f => f != null && f.Size > 0).Sum(f => f.Size);

        if (totalSize <= 0)
        {
            return 0;
        }

        var slices = _resolver.ResolvePiece(pieceIndex, pieceLength, totalSize, files);
        if (slices == null || slices.Count == 0)
        {
            return 0;
        }

        var totalBytesRead = 0;

        foreach (var slice in slices)
        {
            if (slice.BufferOffset >= destinationBuffer.Length)
            {
                continue;
            }

            var sliceLen = (int)Math.Min(slice.SliceLength, destinationBuffer.Length - slice.BufferOffset);
            if (sliceLen <= 0)
            {
                continue;
            }

            if (IsPadding(slice.File))
            {
                // BEP 47: Transparently zero-fill padding files in memory
                destinationBuffer.Span.Slice(slice.BufferOffset, sliceLen).Clear();
                totalBytesRead += sliceLen;
                continue;
            }

            var filePath = ResolveFilePath(baseDirectory, torrent, slice.File?.Path);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                continue;
            }

            try
            {
                var handle = _handlePool.GetOrCreateHandle(filePath, writeAccess: false);
                totalBytesRead += ReadSlice(handle, destinationBuffer, slice, sliceLen);
            }
            catch (ObjectDisposedException)
            {
                // Handle may have been evicted and closed under high concurrency; retry once with a fresh handle
                try
                {
                    var handle = _handlePool.GetOrCreateHandle(filePath, writeAccess: false);
                    totalBytesRead += ReadSlice(handle, destinationBuffer, slice, sliceLen);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Gracefully ignore file read errors and treat missing bytes as zeros
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Gracefully ignore file read errors and treat missing bytes as zeros
            }
        }

        return totalBytesRead;
    }

    public void WritePiece(Torrent torrent, IList<TorrentFile> files, int pieceIndex, ReadOnlyMemory<byte> sourceBuffer, string baseDirectory = null)
    {
        if (torrent == null || files == null || files.Count == 0 || pieceIndex < 0)
        {
            return;
        }

        var pieceLength = torrent.PieceLength;
        if (pieceLength <= 0)
        {
            return;
        }

        var totalSize = torrent.TotalSize > 0
            ? torrent.TotalSize
            : files.Where(f => f != null && f.Size > 0).Sum(f => f.Size);

        if (totalSize <= 0)
        {
            return;
        }

        var slices = _resolver.ResolvePiece(pieceIndex, pieceLength, totalSize, files);
        if (slices == null || slices.Count == 0)
        {
            return;
        }

        foreach (var slice in slices)
        {
            if (slice.BufferOffset >= sourceBuffer.Length)
            {
                continue;
            }

            var sliceLen = (int)Math.Min(slice.SliceLength, sourceBuffer.Length - slice.BufferOffset);
            if (sliceLen <= 0)
            {
                continue;
            }

            if (IsPadding(slice.File))
            {
                // BEP 47: Transparently skip writes to padding files
                continue;
            }

            var filePath = ResolveFilePath(baseDirectory, torrent, slice.File?.Path);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                var handle = _handlePool.GetOrCreateHandle(filePath, writeAccess: true);
                RandomAccess.Write(handle, sourceBuffer.Slice(slice.BufferOffset, sliceLen).Span, slice.FileOffset);
            }
            catch (ObjectDisposedException)
            {
                // Handle may have been evicted and closed under high concurrency; retry once with a fresh handle
                var handle = _handlePool.GetOrCreateHandle(filePath, writeAccess: true);
                RandomAccess.Write(handle, sourceBuffer.Slice(slice.BufferOffset, sliceLen).Span, slice.FileOffset);
            }
        }
    }

    public byte[] ReadBlock(Torrent torrent, IList<TorrentFile> files, int pieceIndex, int begin, int length, string baseDirectory = null)
    {
        var tempStorage = new MultiFilePieceStorage(torrent, files, baseDirectory, _handlePool);
        return tempStorage.ReadBlock(pieceIndex, begin, length);
    }

    public void WriteBlock(Torrent torrent, IList<TorrentFile> files, int pieceIndex, int begin, ReadOnlyMemory<byte> data, string baseDirectory = null)
    {
        var tempStorage = new MultiFilePieceStorage(torrent, files, baseDirectory, _handlePool);
        tempStorage.WriteBlock(pieceIndex, begin, data);
    }

    public void WriteBlock(Torrent torrent, IList<TorrentFile> files, int pieceIndex, int begin, byte[] data, string baseDirectory = null)
    {
        WriteBlock(torrent, files, pieceIndex, begin, (ReadOnlyMemory<byte>)data, baseDirectory);
    }

    public void Flush()
    {
        // Flush any pending buffers if needed
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_ownsHandlePool)
            {
                _handlePool.Dispose();
            }
        }
    }

    private void ValidateBlockParameters(int pieceIndex, int begin, int length)
    {
        if (pieceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), $"Piece index {pieceIndex} cannot be negative.");
        }

        if (begin < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(begin), $"Block begin offset {begin} cannot be negative.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), $"Block length {length} cannot be negative.");
        }

        if (_pieceLength <= 0)
        {
            throw new InvalidOperationException("Piece length must be greater than zero.");
        }

        if (_files == null || _files.Count == 0 || _totalSize <= 0)
        {
            throw new InvalidOperationException("Storage has not been initialized with torrent files.");
        }

        var pieceStart = (long)pieceIndex * _pieceLength;
        if (pieceStart >= _totalSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), $"Piece index {pieceIndex} is beyond total size {_totalSize}.");
        }

        var currentPieceSize = Math.Min((long)_pieceLength, _totalSize - pieceStart);
        if (begin > currentPieceSize || (long)begin + length > currentPieceSize)
        {
            throw new ArgumentOutOfRangeException(nameof(begin), $"Block range [{begin}, {begin + length}) exceeds piece size {currentPieceSize}.");
        }
    }

    private int ReadBlockInternal(int pieceIndex, int begin, Memory<byte> destinationBuffer)
    {
        destinationBuffer.Span.Clear();

        var globalStart = (long)pieceIndex * _pieceLength + begin;
        var globalEnd = globalStart + destinationBuffer.Length;
        var totalBytesRead = 0;

        var startIndex = FindStartingFileIndex(globalStart);

        for (var i = startIndex; i < _files.Count; i++)
        {
            var file = _files[i];
            if (file == null || file.Size <= 0)
            {
                continue;
            }

            var fileStart = _prefixOffsets[i];
            var fileEnd = fileStart + file.Size;

            if (fileStart >= globalEnd)
            {
                break;
            }

            var intersectStart = Math.Max(globalStart, fileStart);
            var intersectEnd = Math.Min(globalEnd, fileEnd);

            if (intersectStart < intersectEnd)
            {
                var fileOffset = intersectStart - fileStart;
                var sliceLength = (int)(intersectEnd - intersectStart);
                var bufferOffset = (int)(intersectStart - globalStart);

                if (IsPadding(file))
                {
                    // BEP 47: Transparently zero-fill padding files in memory
                    destinationBuffer.Span.Slice(bufferOffset, sliceLength).Clear();
                    totalBytesRead += sliceLength;
                    continue;
                }

                var filePath = ResolveFilePath(_baseDirectory, file.Path);
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    // Treat missing file as zeros
                    continue;
                }

                try
                {
                    var handle = _handlePool.GetOrCreateHandle(filePath, writeAccess: false);
                    totalBytesRead += ReadSliceFromHandle(handle, destinationBuffer, bufferOffset, fileOffset, sliceLength);
                }
                catch (ObjectDisposedException)
                {
                    try
                    {
                        var handle = _handlePool.GetOrCreateHandle(filePath, writeAccess: false);
                        totalBytesRead += ReadSliceFromHandle(handle, destinationBuffer, bufferOffset, fileOffset, sliceLength);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Gracefully ignore file read errors and treat missing bytes as zeros
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Gracefully ignore file read errors and treat missing bytes as zeros
                }
            }
        }

        return totalBytesRead;
    }

    private static int ReadSliceFromHandle(SafeFileHandle handle, Memory<byte> destinationBuffer, int bufferOffset, long fileOffset, int sliceLen)
    {
        var fileLength = RandomAccess.GetLength(handle);
        if (fileOffset >= fileLength)
        {
            return 0;
        }

        var available = fileLength - fileOffset;
        var bytesToRead = (int)Math.Min((long)sliceLen, available);
        var read = 0;

        while (read < bytesToRead)
        {
            var n = RandomAccess.Read(
                handle,
                destinationBuffer.Slice(bufferOffset + read, bytesToRead - read).Span,
                fileOffset + read);

            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        return read;
    }

    private static int ReadSlice(SafeFileHandle handle, Memory<byte> destinationBuffer, FileSlice slice, int sliceLen)
    {
        var fileLength = RandomAccess.GetLength(handle);
        if (slice.FileOffset >= fileLength)
        {
            return 0;
        }

        var available = fileLength - slice.FileOffset;
        var bytesToRead = (int)Math.Min((long)sliceLen, available);
        var read = 0;

        while (read < bytesToRead)
        {
            var n = RandomAccess.Read(
                handle,
                destinationBuffer.Slice(slice.BufferOffset + read, bytesToRead - read).Span,
                slice.FileOffset + read);

            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        return read;
    }

    public static string ResolveFilePath(string baseDirectory, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (Path.IsPathRooted(filePath))
        {
            return filePath;
        }

        var normalizedPath = filePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return string.IsNullOrEmpty(baseDirectory) ? normalizedPath : Path.Combine(baseDirectory, normalizedPath);
    }

    public static string ResolveFilePath(string baseDirectory, Torrent torrent, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (Path.IsPathRooted(filePath))
        {
            return filePath;
        }

        var basePath = !string.IsNullOrWhiteSpace(baseDirectory)
            ? baseDirectory
            : (!string.IsNullOrWhiteSpace(torrent?.SavePath) ? torrent.SavePath : string.Empty);

        var normalizedPath = filePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return string.IsNullOrEmpty(basePath) ? normalizedPath : Path.Combine(basePath, normalizedPath);
    }
}
