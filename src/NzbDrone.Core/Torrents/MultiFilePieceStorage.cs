using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NzbDrone.Core.Torrents;

public class MultiFilePieceStorage : IMultiFilePieceStorage
{
    private readonly IPieceBoundaryResolver _resolver;

    public MultiFilePieceStorage(IPieceBoundaryResolver resolver = null)
    {
        _resolver = resolver ?? new PieceBoundaryResolver();
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
                using var handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var fileLength = RandomAccess.GetLength(handle);

                if (slice.FileOffset < fileLength)
                {
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

                    totalBytesRead += read;
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

            using var handle = File.OpenHandle(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            RandomAccess.Write(handle, sourceBuffer.Slice(slice.BufferOffset, sliceLen).Span, slice.FileOffset);
        }
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
}
