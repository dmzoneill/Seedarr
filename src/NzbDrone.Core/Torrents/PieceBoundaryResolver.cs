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

        var offsets = new long[files.Count];
        long running = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];
            offsets[i] = (f != null && f.ByteOffset > 0) ? f.ByteOffset : running;
            if (f != null && f.Size > 0)
            {
                running = offsets[i] + f.Size;
            }
        }

        var low = 0;
        var high = files.Count - 1;
        var startIndex = files.Count;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var f = files[mid];
            var fEnd = offsets[mid] + (f != null && f.Size > 0 ? f.Size : 0L);

            if (fEnd > rangeStart)
            {
                startIndex = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        for (var i = startIndex; i < files.Count; i++)
        {
            var file = files[i];
            if (file == null || file.Size <= 0)
            {
                continue;
            }

            var fileStart = offsets[i];
            var fileEnd = fileStart + file.Size;

            if (fileStart >= rangeEnd)
            {
                break;
            }

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
