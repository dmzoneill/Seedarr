using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public static class TorrentPieceCalculator
{
    /// <summary>
    /// Calculates PieceOffset and PieceCount for a single file given the running byte offset, file size, and piece length.
    /// </summary>
    public static (int PieceOffset, int PieceCount) CalculateForFile(long runningByteOffset, long fileSize, long pieceLength)
    {
        if (pieceLength <= 0)
        {
            return (0, 0);
        }

        var filePieceOffset = (int)(runningByteOffset / pieceLength);
        if (fileSize <= 0)
        {
            return (filePieceOffset, 0);
        }

        var filePieceEnd = (int)((runningByteOffset + fileSize - 1) / pieceLength);
        var filePieceCount = filePieceEnd - filePieceOffset + 1;
        return (filePieceOffset, filePieceCount);
    }

    /// <summary>
    /// Calculates and assigns sequential PieceOffset and PieceCount for each file in a collection given the torrent's piece length.
    /// </summary>
    public static void CalculatePieceBoundaries(IList<TorrentFile> files, long pieceLength)
    {
        if (files == null || files.Count == 0)
        {
            return;
        }

        var runningByteOffset = 0L;
        foreach (var file in files)
        {
            if (file == null)
            {
                continue;
            }

            var (pieceOffset, pieceCount) = CalculateForFile(runningByteOffset, file.Size, pieceLength);
            file.PieceOffset = pieceOffset;
            file.PieceCount = pieceCount;

            if (pieceLength > 0)
            {
                runningByteOffset += file.Size;
            }
        }
    }
}
