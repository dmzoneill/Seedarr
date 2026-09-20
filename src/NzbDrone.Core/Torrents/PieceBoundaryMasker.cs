using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Torrents;

public class PieceBoundaryMasker : IPieceBoundaryMasker
{
    public bool IsSelectiveDownload(IList<TorrentFile> files)
    {
        if (files == null || files.Count == 0)
        {
            return false;
        }

        return files.Any(f => !f.Wanted);
    }

    public long CalculateWantedSize(Torrent torrent, IList<TorrentFile> files)
    {
        if (files == null || files.Count == 0)
        {
            return torrent?.TotalSize ?? 0L;
        }

        if (!IsSelectiveDownload(files))
        {
            return torrent?.TotalSize > 0 ? torrent.TotalSize : files.Sum(f => Math.Max(0L, f.Size));
        }

        return files.Where(f => f.Wanted).Sum(f => Math.Max(0L, f.Size));
    }

    public bool[] ComputeWantedPieces(Torrent torrent, IList<TorrentFile> files)
    {
        if (torrent == null || torrent.PieceLength <= 0)
        {
            return Array.Empty<bool>();
        }

        var pieceCount = torrent.PieceCount;
        if (pieceCount <= 0)
        {
            var totalSize = torrent.TotalSize > 0
                ? torrent.TotalSize
                : (files != null && files.Count > 0 ? files.Sum(f => Math.Max(0L, f.Size)) : 0L);

            if (totalSize > 0)
            {
                pieceCount = (int)((totalSize + torrent.PieceLength - 1) / torrent.PieceLength);
            }
        }

        if (pieceCount <= 0)
        {
            return Array.Empty<bool>();
        }

        var wantedPieces = new bool[pieceCount];

        if (files == null || files.Count == 0)
        {
            Array.Fill(wantedPieces, true);
            return wantedPieces;
        }

        long runningOffset = 0;

        foreach (var file in files)
        {
            if (file == null || file.Size <= 0)
            {
                continue;
            }

            var offset = file.ByteOffset > 0
                ? file.ByteOffset
                : (runningOffset > 0 ? runningOffset : (file.PieceOffset > 0 ? (long)file.PieceOffset * torrent.PieceLength : runningOffset));

            var startPiece = (int)(offset / torrent.PieceLength);
            var endPiece = (int)((offset + file.Size - 1) / torrent.PieceLength);

            runningOffset = offset + file.Size;

            if (!file.Wanted)
            {
                continue;
            }

            startPiece = Math.Max(0, startPiece);
            endPiece = Math.Min(pieceCount - 1, endPiece);

            for (var p = startPiece; p <= endPiece; p++)
            {
                wantedPieces[p] = true;
            }
        }

        return wantedPieces;
    }
}
