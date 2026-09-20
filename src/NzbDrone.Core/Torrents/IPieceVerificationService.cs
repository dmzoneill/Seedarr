using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public interface IPieceVerificationService
{
    bool VerifyPiece(string infoHash, int pieceIndex, byte[] pieceData, byte[] expectedHash);

    bool VerifyPiece(string infoHash, int pieceIndex, ReadOnlySpan<byte> pieceData, ReadOnlySpan<byte> expectedHash);

    bool VerifyPieceFromStorage(
        Torrent torrent,
        IList<TorrentFile> files,
        int pieceIndex,
        ReadOnlySpan<byte> expectedHash,
        IMultiFilePieceStorage storage = null,
        string baseDirectory = null);

    bool VerifyPieceFromStorage(
        Torrent torrent,
        IList<TorrentFile> files,
        int pieceIndex,
        byte[] expectedHash,
        IMultiFilePieceStorage storage = null,
        string baseDirectory = null);
}
