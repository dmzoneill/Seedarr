using NzbDrone.Core.Datastore;

namespace NzbDrone.SignalR;

public class PieceCompletedMessage : SignalRMessage
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

    public long BytesDownloaded { get; set; }

    public PieceCompletedMessage()
    {
        Name = "PieceCompleted";
        Action = ModelAction.Updated;
        UpdateBody();
    }

    public PieceCompletedMessage(string infoHash, int pieceIndex, long bytesDownloaded = 0)
    {
        InfoHash = infoHash;
        PieceIndex = pieceIndex;
        BytesDownloaded = bytesDownloaded;
        Name = "PieceCompleted";
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
            BytesDownloaded
        };
    }
}
