using NzbDrone.Core.Datastore;

namespace NzbDrone.SignalR;

public class PieceCompletedMessage : SignalRMessage
{
    public string InfoHash { get; set; }

    public int PieceIndex { get; set; }

    public long BytesDownloaded { get; set; }

    public PieceCompletedMessage()
    {
        Name = "PieceCompleted";
        Action = ModelAction.Updated;
        Body = this;
    }

    public PieceCompletedMessage(string infoHash, int pieceIndex, long bytesDownloaded = 0)
    {
        InfoHash = infoHash;
        PieceIndex = pieceIndex;
        BytesDownloaded = bytesDownloaded;
        Name = "PieceCompleted";
        Action = ModelAction.Updated;
        Body = this;
    }
}
