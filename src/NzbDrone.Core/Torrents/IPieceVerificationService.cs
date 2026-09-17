using System;

namespace NzbDrone.Core.Torrents;

public interface IPieceVerificationService
{
    bool VerifyPiece(string infoHash, int pieceIndex, byte[] pieceData, byte[] expectedHash);

    bool VerifyPiece(string infoHash, int pieceIndex, ReadOnlySpan<byte> pieceData, ReadOnlySpan<byte> expectedHash);
}
