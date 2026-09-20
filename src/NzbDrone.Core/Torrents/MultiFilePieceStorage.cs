using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32.SafeHandles;

namespace NzbDrone.Core.Torrents;

public class MultiFilePieceStorage : IMultiFilePieceStorage
{
    private readonly IPieceBoundaryResolver _resolver;
    private readonly IFileHandlePool _handlePool;
    private readonly bool _ownsHandlePool;
    private bool _disposed;

    public IFileHandlePool HandlePool => _handlePool;

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
                catch (Exception)
                {
                    // Gracefully ignore file read errors and treat missing bytes as zeros
                }
            }
            catch (Exception)
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

    private static int ReadSlice(SafeFileHandle handle, Memory<byte> destinationBuffer, TorrentSlice slice, int sliceLen)
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

    private static string ResolveFilePath(string baseDirectory, Torrent torrent, string filePath)
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

        return string.IsNullOrEmpty(basePath) ? filePath : Path.Combine(basePath, filePath);
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
}
