using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.SignalR;

public class PieceBatchCompletedMessage : SignalRMessage
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

    public List<int> PieceIndexes { get; set; } = new();

    public long BytesDownloaded { get; set; }

    public PieceBatchCompletedMessage()
    {
        Name = "PieceBatchCompleted";
        Action = ModelAction.Updated;
        UpdateBody();
    }

    public PieceBatchCompletedMessage(string infoHash, IEnumerable<int> pieceIndexes, long bytesDownloaded = 0)
    {
        InfoHash = infoHash;
        PieceIndexes = pieceIndexes != null ? new List<int>(pieceIndexes) : new List<int>();
        BytesDownloaded = bytesDownloaded;
        Name = "PieceBatchCompleted";
        Action = ModelAction.Updated;
        UpdateBody();
    }

    private void UpdateBody()
    {
        Body = new
        {
            InfoHash,
            TorrentId = _torrentId,
            PieceIndexes,
            PieceIndices = PieceIndexes,
            BytesDownloaded
        };
    }
}
