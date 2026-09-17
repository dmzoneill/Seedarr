using System;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Torrents;

public interface ITorrentRelocationService
{
    Task<bool> RelocateTorrentAsync(int torrentId, string newSavePath, CancellationToken cancellationToken = default);

    TorrentRelocationProgress GetProgress(int torrentId);

    bool IsRelocating(int torrentId);

    SemaphoreSlim GetTorrentLock(int torrentId);

    bool IsLocked(int torrentId);
}

public class TorrentRelocationProgress
{
    public int TorrentId { get; set; }

    private double? _progress;

    public long BytesTransferred { get; set; }

    public long TotalBytes { get; set; }

    public double Progress
    {
        get => _progress ?? (TotalBytes > 0 ? Math.Min(1.0, (double)BytesTransferred / TotalBytes) : 0.0);
        set => _progress = value;
    }

    public double BytesPerSecond { get; set; }

    public string CurrentFilePath { get; set; }

    public int CurrentFileIndex { get; set; }

    public int TotalFiles { get; set; }

    public bool IsComplete { get; set; }

    public string ErrorMessage { get; set; }

    public DateTime StartTimeUtc { get; set; } = DateTime.UtcNow;
}
