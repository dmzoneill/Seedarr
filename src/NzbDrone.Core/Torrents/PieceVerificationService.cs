using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace NzbDrone.Core.Torrents;

public class PieceVerificationService : IPieceVerificationService
{
    private readonly IPieceStorage _pieceStorage;
    private readonly IPiecePicker _piecePicker;
    private readonly ITorrentStreamService _torrentStreamService;
    private readonly IMultiFilePieceStorage _multiFilePieceStorage;

    public PieceVerificationService(
        IPieceStorage pieceStorage,
        IPiecePicker piecePicker = null,
        ITorrentStreamService torrentStreamService = null,
        IMultiFilePieceStorage multiFilePieceStorage = null)
    {
        _pieceStorage = pieceStorage ?? throw new ArgumentNullException(nameof(pieceStorage));
        _piecePicker = piecePicker;
        _torrentStreamService = torrentStreamService;
        _multiFilePieceStorage = multiFilePieceStorage;
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
            _torrentStreamService?.NotifyPieceCompleted(infoHash, pieceIndex);
            return true;
        }

        _pieceStorage.MarkPieceCorrupted(infoHash, pieceIndex);
        return false;
    }

    public bool VerifyPieceFromStorage(
        Torrent torrent,
        IList<TorrentFile> files,
        int pieceIndex,
        byte[] expectedHash,
        IMultiFilePieceStorage storage = null,
        string baseDirectory = null)
    {
        if (expectedHash == null)
        {
            _piecePicker?.MarkPieceInactive(torrent?.InfoHash, pieceIndex);
            if (torrent?.InfoHash != null)
            {
                _pieceStorage.MarkPieceCorrupted(torrent.InfoHash, pieceIndex);
            }

            return false;
        }

        return VerifyPieceFromStorage(torrent, files, pieceIndex, expectedHash.AsSpan(), storage, baseDirectory);
    }

    public bool VerifyPieceFromStorage(
        Torrent torrent,
        IList<TorrentFile> files,
        int pieceIndex,
        ReadOnlySpan<byte> expectedHash,
        IMultiFilePieceStorage storage = null,
        string baseDirectory = null)
    {
        ReadOnlySpan<byte> targetHash;
        if (expectedHash.Length == 20)
        {
            targetHash = expectedHash;
        }
        else if (expectedHash.Length >= (pieceIndex + 1) * 20)
        {
            targetHash = expectedHash.Slice(pieceIndex * 20, 20);
        }
        else
        {
            _piecePicker?.MarkPieceInactive(torrent?.InfoHash, pieceIndex);
            if (torrent?.InfoHash != null)
            {
                _pieceStorage.MarkPieceCorrupted(torrent.InfoHash, pieceIndex);
            }

            return false;
        }

        if (torrent == null || files == null || files.Count == 0 || pieceIndex < 0 || torrent.PieceLength <= 0)
        {
            _piecePicker?.MarkPieceInactive(torrent?.InfoHash, pieceIndex);
            if (torrent?.InfoHash != null)
            {
                _pieceStorage.MarkPieceCorrupted(torrent.InfoHash, pieceIndex);
            }

            return false;
        }

        var totalSize = torrent.TotalSize > 0
            ? torrent.TotalSize
            : files.Where(f => f != null && f.Size > 0).Sum(f => f.Size);

        var pieceStartOffset = (long)pieceIndex * torrent.PieceLength;
        if (pieceStartOffset >= totalSize)
        {
            _piecePicker?.MarkPieceInactive(torrent.InfoHash, pieceIndex);
            _pieceStorage.MarkPieceCorrupted(torrent.InfoHash, pieceIndex);
            return false;
        }

        var actualPieceLength = (int)Math.Min((long)torrent.PieceLength, totalSize - pieceStartOffset);
        if (actualPieceLength <= 0)
        {
            _piecePicker?.MarkPieceInactive(torrent.InfoHash, pieceIndex);
            _pieceStorage.MarkPieceCorrupted(torrent.InfoHash, pieceIndex);
            return false;
        }

        var pieceStorageService = storage ?? _multiFilePieceStorage ?? new MultiFilePieceStorage();
        var rented = ArrayPool<byte>.Shared.Rent(actualPieceLength);

        try
        {
            var pieceBuffer = rented.AsMemory(0, actualPieceLength);
            pieceStorageService.ReadPiece(torrent, files, pieceIndex, pieceBuffer, baseDirectory);
            return VerifyPiece(torrent.InfoHash, pieceIndex, pieceBuffer.Span, targetHash);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
