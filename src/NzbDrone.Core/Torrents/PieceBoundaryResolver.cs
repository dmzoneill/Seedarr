using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Torrents;

public class FileSlice
{
    public TorrentFile File { get; set; }
    public long FileOffset { get; set; }
    public long SliceLength { get; set; }
    public int BufferOffset { get; set; }
}

public class PieceBoundaryResolver : IPieceBoundaryResolver
{
    public List<FileSlice> ResolvePiece(int pieceIndex, int pieceLength, long totalSize, IList<TorrentFile> files)
    {
        if (pieceIndex < 0 || pieceLength <= 0 || files == null || files.Count == 0)
        {
            return new List<FileSlice>();
        }

        if (totalSize <= 0)
        {
            totalSize = files.Where(f => f != null && f.Size > 0).Sum(f => f.Size);
        }

        if (totalSize <= 0)
        {
            return new List<FileSlice>();
        }

        var pieceStartOffset = (long)pieceIndex * pieceLength;
        if (pieceStartOffset >= totalSize)
        {
            return new List<FileSlice>();
        }

        var length = Math.Min((long)pieceLength, totalSize - pieceStartOffset);
        return ResolveRange(pieceStartOffset, length, files);
    }

    public List<FileSlice> ResolveRange(long globalOffset, long length, IList<TorrentFile> files)
    {
        var result = new List<FileSlice>();

        if (globalOffset < 0 || length <= 0 || files == null || files.Count == 0)
        {
            return result;
        }

        var rangeStart = globalOffset;
        var rangeEnd = globalOffset + length;
        long runningOffset = 0;

        foreach (var file in files)
        {
            if (file == null || file.Size <= 0)
            {
                continue;
            }

            var fileStart = file.ByteOffset > 0 ? file.ByteOffset : runningOffset;
            var fileEnd = fileStart + file.Size;
            runningOffset = fileEnd;

            var intersectStart = Math.Max(rangeStart, fileStart);
            var intersectEnd = Math.Min(rangeEnd, fileEnd);

            if (intersectStart < intersectEnd)
            {
                result.Add(new FileSlice
                {
                    File = file,
                    FileOffset = intersectStart - fileStart,
                    SliceLength = intersectEnd - intersectStart,
                    BufferOffset = (int)(intersectStart - rangeStart)
                });
            }
        }

        return result;
    }
}
