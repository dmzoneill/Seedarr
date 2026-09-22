namespace NzbDrone.Core.Telemetry;

public class TorrentResourceMetrics
{
    public int TorrentId { get; set; }

    public string InfoHash { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Status { get; set; } = "Stopped";

    public double Progress { get; set; }

    public long TotalBytes { get; set; }

    public long PayloadDownloadSpeed { get; set; }

    public long PayloadUploadSpeed { get; set; }

    public long ProtocolDownloadSpeed { get; set; }

    public long ProtocolUploadSpeed { get; set; }

    public long DownloadedPayload { get; set; }

    public long UploadedPayload { get; set; }

    public long ProtocolDownloaded { get; set; }

    public long ProtocolUploaded { get; set; }

    public double EfficiencyRatio { get; set; }

    public int ConnectedPeers { get; set; }
}
