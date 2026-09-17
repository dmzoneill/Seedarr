using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;

namespace NzbDrone.Core.Peers.Extensions;

public interface IMetadataExchange
{
    const int MaxMetadataSize = 10 * 1024 * 1024; // 10 MiB
    const int MetadataBlockSize = 16384; // 16 KiB

    byte[] BuildMetadataRequest(int piece);
    byte[] BuildMetadataResponse(int piece, int totalSize, byte[] data);
    byte[] BuildMetadataReject(int piece);
    MetadataMessage ParseMetadataMessage(byte[] data);
    bool ValidateMetadata(byte[] assembledRawBytes, string expectedInfoHash, string torrentName = null);
    bool VerifyMetadataHash(byte[] assembledRawBytes, string expectedInfoHash, string torrentName = null);
    byte[] ReassembleMetadata(byte[][] pieces, int totalSize, string expectedInfoHash, string torrentName = null);
    byte[] ReassembleMetadata(IEnumerable<MetadataMessage> messages, int totalSize, string expectedInfoHash, string torrentName = null);
    bool TryReassembleMetadata(byte[][] pieces, int totalSize, string expectedInfoHash, out byte[] assembledMetadata, string torrentName = null);
}

public class MetadataMessage
{
    public int MessageType { get; set; }
    public int Piece { get; set; }
    public int TotalSize { get; set; }
    public byte[] Data { get; set; }
}

public class MetadataExchange : IMetadataExchange
{
    public const int MaxMetadataSize = 10 * 1024 * 1024; // 10 MiB
    public const int MetadataBlockSize = 16384; // 16 KiB

    private readonly Logger _logger;

    public MetadataExchange()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public byte[] BuildMetadataRequest(int piece)
    {
        var dict = new BDictionary
        {
            ["msg_type"] = new BNumber(0),
            ["piece"] = new BNumber(piece)
        };

        return dict.EncodeAsBytes();
    }

    public byte[] BuildMetadataResponse(int piece, int totalSize, byte[] data)
    {
        var dict = new BDictionary
        {
            ["msg_type"] = new BNumber(1),
            ["piece"] = new BNumber(piece),
            ["total_size"] = new BNumber(totalSize)
        };

        var header = dict.EncodeAsBytes();
        var result = new byte[header.Length + data.Length];
        Array.Copy(header, 0, result, 0, header.Length);
        Array.Copy(data, 0, result, header.Length, data.Length);
        return result;
    }

    public byte[] BuildMetadataReject(int piece)
    {
        var dict = new BDictionary
        {
            ["msg_type"] = new BNumber(2),
            ["piece"] = new BNumber(piece)
        };

        return dict.EncodeAsBytes();
    }

    public MetadataMessage ParseMetadataMessage(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return new MetadataMessage();
        }

        try
        {
            var parser = new BencodeParser();
            using var stream = new MemoryStream(data);
            var dict = parser.Parse<BDictionary>(stream);

            var messageType = dict.TryGetValue("msg_type", out var msgTypeObj) && msgTypeObj is BNumber msgTypeNum
                ? (int)msgTypeNum.Value
                : 0;

            var piece = dict.TryGetValue("piece", out var pieceObj) && pieceObj is BNumber pieceNum
                ? (int)pieceNum.Value
                : 0;

            if (piece < 0)
            {
                _logger.Warn("Peer reported invalid metadata piece index: {0}", piece);
                return new MetadataMessage
                {
                    MessageType = 2,
                    Piece = piece
                };
            }

            var hasTotalSize = dict.TryGetValue("total_size", out var totalSizeObj) && totalSizeObj is BNumber totalSizeNum;
            var totalSize = hasTotalSize ? (int)((BNumber)totalSizeObj).Value : 0;

            if ((hasTotalSize && (totalSize <= 0 || totalSize > MaxMetadataSize)) ||
                (messageType == 1 && (totalSize <= 0 || totalSize > MaxMetadataSize)))
            {
                _logger.Warn("Peer reported invalid metadata total_size: {0} bytes (max allowed: {1})", totalSize, MaxMetadataSize);
                return new MetadataMessage
                {
                    MessageType = 2,
                    Piece = piece
                };
            }

            byte[] pieceData = null;
            if (stream.Position < stream.Length)
            {
                var remaining = stream.Length - stream.Position;
                if (remaining > MetadataBlockSize)
                {
                    _logger.Warn("Incoming metadata chunk size {0} exceeds max block size {1}", remaining, MetadataBlockSize);
                    return new MetadataMessage
                    {
                        MessageType = 2,
                        Piece = piece
                    };
                }

                if (messageType == 1 && totalSize > 0)
                {
                    var totalPieces = (int)Math.Ceiling((double)totalSize / MetadataBlockSize);
                    if (piece == totalPieces - 1)
                    {
                        var expectedSize = totalSize - (piece * MetadataBlockSize);
                        if (expectedSize > 0 && remaining > expectedSize)
                        {
                            _logger.Warn("Incoming metadata chunk size {0} exceeds expected size {1} for final piece {2}", remaining, expectedSize, piece);
                            return new MetadataMessage
                            {
                                MessageType = 2,
                                Piece = piece
                            };
                        }
                    }
                }

                pieceData = new byte[remaining];
                stream.ReadExactly(pieceData, 0, (int)remaining);
            }

            return new MetadataMessage
            {
                MessageType = messageType,
                Piece = piece,
                TotalSize = totalSize,
                Data = pieceData
            };
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to parse metadata message");
            return new MetadataMessage();
        }
    }

    public bool ValidateMetadata(byte[] assembledRawBytes, string expectedInfoHash, string torrentName = null)
    {
        if (assembledRawBytes == null || assembledRawBytes.Length == 0)
        {
            _logger.Warn("Cannot validate metadata: raw bytes are null or empty");
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedInfoHash))
        {
            _logger.Warn("Cannot validate metadata: expected infohash is missing");
            return false;
        }

        if (assembledRawBytes.Length > MaxMetadataSize)
        {
            _logger.Warn("Assembled metadata size {0} exceeds max allowed {1}", assembledRawBytes.Length, MaxMetadataSize);
            return false;
        }

        var hash = SHA1.HashData(assembledRawBytes);
        var calculatedHash = Convert.ToHexString(hash);

        if (!calculatedHash.Equals(expectedInfoHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error(
                "Metadata hash mismatch for {0}: expected {1}, got {2}. Discarding poisoned metadata.",
                torrentName ?? "torrent",
                expectedInfoHash,
                calculatedHash);
            return false;
        }

        return true;
    }

    public bool ValidateMetadata(byte[] assembledRawBytes, byte[] expectedInfoHashBytes, string torrentName = null)
    {
        if (expectedInfoHashBytes == null)
        {
            return false;
        }

        return ValidateMetadata(assembledRawBytes, Convert.ToHexString(expectedInfoHashBytes), torrentName);
    }

    public bool VerifyMetadataHash(byte[] assembledRawBytes, string expectedInfoHash, string torrentName = null)
    {
        return ValidateMetadata(assembledRawBytes, expectedInfoHash, torrentName);
    }

    public byte[] ReassembleMetadata(byte[][] pieces, int totalSize, string expectedInfoHash, string torrentName = null)
    {
        if (totalSize <= 0 || totalSize > MaxMetadataSize)
        {
            _logger.Warn("Cannot reassemble metadata: total_size {0} is invalid (max allowed: {1})", totalSize, MaxMetadataSize);
            return null;
        }

        if (pieces == null)
        {
            _logger.Warn("Cannot reassemble metadata: pieces array is null");
            return null;
        }

        var totalPieces = (int)Math.Ceiling((double)totalSize / MetadataBlockSize);
        if (pieces.Length < totalPieces)
        {
            _logger.Warn("Cannot reassemble metadata: expected {0} pieces but only have {1}", totalPieces, pieces.Length);
            return null;
        }

        var assembledRawBytes = new byte[totalSize];
        var offset = 0;

        for (var i = 0; i < totalPieces; i++)
        {
            var piece = pieces[i];
            if (piece == null)
            {
                _logger.Warn("Cannot reassemble metadata: piece {0} is missing", i);
                return null;
            }

            var expectedChunkSize = (i == totalPieces - 1)
                ? totalSize - (i * MetadataBlockSize)
                : MetadataBlockSize;

            if (piece.Length != expectedChunkSize)
            {
                _logger.Warn(
                    "Cannot reassemble metadata: piece {0} length {1} does not match expected {2}",
                    i,
                    piece.Length,
                    expectedChunkSize);
                return null;
            }

            Array.Copy(piece, 0, assembledRawBytes, offset, piece.Length);
            offset += piece.Length;
        }

        if (!ValidateMetadata(assembledRawBytes, expectedInfoHash, torrentName))
        {
            return null;
        }

        return assembledRawBytes;
    }

    public byte[] ReassembleMetadata(IEnumerable<MetadataMessage> messages, int totalSize, string expectedInfoHash, string torrentName = null)
    {
        if (messages == null || totalSize <= 0 || totalSize > MaxMetadataSize)
        {
            return null;
        }

        var totalPieces = (int)Math.Ceiling((double)totalSize / MetadataBlockSize);
        var pieceArray = new byte[totalPieces][];

        foreach (var msg in messages)
        {
            if (msg != null && msg.Piece >= 0 && msg.Piece < totalPieces && msg.Data != null)
            {
                pieceArray[msg.Piece] = msg.Data;
            }
        }

        return ReassembleMetadata(pieceArray, totalSize, expectedInfoHash, torrentName);
    }

    public bool TryReassembleMetadata(byte[][] pieces, int totalSize, string expectedInfoHash, out byte[] assembledMetadata, string torrentName = null)
    {
        assembledMetadata = ReassembleMetadata(pieces, totalSize, expectedInfoHash, torrentName);
        return assembledMetadata != null;
    }
}
