using System;
using System.Security.Cryptography;

namespace NzbDrone.Core.Torrents;

public class PieceVerificationService : IPieceVerificationService
{
    private readonly IPieceStorage _pieceStorage;
    private readonly IPiecePicker _piecePicker;

    public PieceVerificationService(IPieceStorage pieceStorage, IPiecePicker piecePicker = null)
    {
        _pieceStorage = pieceStorage ?? throw new ArgumentNullException(nameof(pieceStorage));
        _piecePicker = piecePicker;
    }

    public bool VerifyPiece(string infoHash, int pieceIndex, byte[] pieceData, byte[] expectedHash)
    {
        if (pieceData == null || expectedHash == null)
        {
            _piecePicker?.MarkPieceInactive(infoHash, pieceIndex);
            _pieceStorage.MarkPieceCorrupted(infoHash, pieceIndex);
            return false;
        }

        return VerifyPiece(infoHash, pieceIndex, pieceData.AsSpan(), expectedHash.AsSpan());
    }

    public bool VerifyPiece(string infoHash, int pieceIndex, ReadOnlySpan<byte> pieceData, ReadOnlySpan<byte> expectedHash)
    {
        Span<byte> computedHash = stackalloc byte[20];
        if (!SHA1.TryHashData(pieceData, computedHash, out var bytesWritten) || bytesWritten != 20)
        {
            _piecePicker?.MarkPieceInactive(infoHash, pieceIndex);
            _pieceStorage.MarkPieceCorrupted(infoHash, pieceIndex);
            return false;
        }

        var isMatch = CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
        _piecePicker?.MarkPieceInactive(infoHash, pieceIndex);

        if (isMatch)
        {
            _pieceStorage.MarkPieceVerified(infoHash, pieceIndex, pieceData.Length);
            return true;
        }

        _pieceStorage.MarkPieceCorrupted(infoHash, pieceIndex);
        return false;
    }
}
