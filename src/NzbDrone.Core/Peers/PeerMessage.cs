namespace NzbDrone.Core.Peers;

public enum PeerMessageType : byte
{
    Choke = 0,
    Unchoke = 1,
    Interested = 2,
    NotInterested = 3,
    Have = 4,
    Bitfield = 5,
    Request = 6,
    Piece = 7,
    Cancel = 8,
    Port = 9,
    Extended = 20
}

public class PeerMessage
{
    public PeerMessageType Type { get; set; }
    public byte[] Payload { get; set; }
    public int Length => (Payload?.Length ?? 0) + 1;
}
