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
    SuggestPiece = 13,
    HaveAll = 14,
    HaveNone = 15,
    RejectRequest = 16,
    AllowedFast = 17,
    Extended = 20,
    HashRequest = 21,
    Hashes = 22,
    HashReject = 23
}

public class PeerMessage
{
    public PeerMessageType Type { get; set; }
    public byte[] Payload { get; set; }
    public int PayloadLength { get; set; } = -1;
    internal int EffectivePayloadLength => PayloadLength >= 0 ? PayloadLength : (Payload?.Length ?? 0);
    public int Length => EffectivePayloadLength + 1;
}
