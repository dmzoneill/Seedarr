using System;

namespace NzbDrone.Core.Storage;

public interface IMultiFileStorage : IDisposable
{
    byte[] ReadBlock(int pieceIndex, int begin, int length);

    int ReadBlock(int pieceIndex, int begin, Memory<byte> destinationBuffer);

    void WriteBlock(int pieceIndex, int begin, ReadOnlyMemory<byte> data);

    void WriteBlock(int pieceIndex, int begin, byte[] data);

    byte[] ReadPiece(int pieceIndex);

    int ReadPiece(int pieceIndex, Memory<byte> destinationBuffer);

    void WritePiece(int pieceIndex, ReadOnlyMemory<byte> data);

    void WritePiece(int pieceIndex, byte[] data);

    void Flush();
}
