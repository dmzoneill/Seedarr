namespace NzbDrone.Core.Peers;

public class PeerRequest
{
    public int PieceIndex { get; set; }
    public int Begin { get; set; }
    public int Length { get; set; }

    public PeerRequest()
    {
    }

    public PeerRequest(int pieceIndex, int begin, int length)
    {
        PieceIndex = pieceIndex;
        Begin = begin;
        Length = length;
    }
}

public class PieceRequest : PeerRequest
{
    public PieceRequest()
    {
    }

    public PieceRequest(int pieceIndex, int begin, int length)
        : base(pieceIndex, begin, length)
    {
    }
}
