using NzbDrone.Core.Datastore;

namespace NzbDrone.SignalR;

public class PieceMapUpdatedMessage : SignalRMessage
{
    private int _torrentId;

    public string InfoHash { get; set; }

    public int TorrentId
    {
        get => _torrentId;
        set
        {
            _torrentId = value;
            UpdateBody();
        }
    }

    public bool Cleared { get; set; } = true;

    public PieceMapUpdatedMessage()
    {
        Name = "pieceMapUpdated";
        Action = ModelAction.Updated;
        UpdateBody();
    }

    public PieceMapUpdatedMessage(string infoHash, bool cleared = true)
    {
        InfoHash = infoHash;
        Cleared = cleared;
        Name = "pieceMapUpdated";
        Action = ModelAction.Updated;
        UpdateBody();
    }

    private void UpdateBody()
    {
        Body = new
        {
            InfoHash,
            TorrentId = _torrentId,
            Cleared,
            Bitfield = string.Empty
        };
    }
}
