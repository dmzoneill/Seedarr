using NzbDrone.Core.Datastore;

namespace NzbDrone.SignalR;

public class PieceCorruptedMessage : SignalRMessage
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

    public int PieceIndex { get; set; }

    public int PieceState { get; set; } = 3;

    public PieceCorruptedMessage()
    {
        Name = "PieceCorrupted";
        Action = ModelAction.Updated;
        UpdateBody();
    }

    public PieceCorruptedMessage(string infoHash, int pieceIndex)
    {
        InfoHash = infoHash;
        PieceIndex = pieceIndex;
        PieceState = 3;
        Name = "PieceCorrupted";
        Action = ModelAction.Updated;
        UpdateBody();
    }

    private void UpdateBody()
    {
        Body = new
        {
            InfoHash,
            TorrentId = _torrentId,
            PieceIndex,
            PieceState,
            State = PieceState
        };
    }
}
