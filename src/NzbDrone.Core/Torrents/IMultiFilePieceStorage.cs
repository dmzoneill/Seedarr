using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public interface IMultiFilePieceStorage : IDisposable
{
    int ReadPiece(Torrent torrent, IList<TorrentFile> files, int pieceIndex, Memory<byte> destinationBuffer, string baseDirectory = null);

    void WritePiece(Torrent torrent, IList<TorrentFile> files, int pieceIndex, ReadOnlyMemory<byte> sourceBuffer, string baseDirectory = null);
}
