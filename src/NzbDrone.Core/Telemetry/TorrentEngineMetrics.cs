namespace NzbDrone.Core.Telemetry;

public class TorrentEngineMetrics
{
    public string EngineId { get; set; } = "SeedarrSimulationEngine";

    public string DisplayName { get; set; } = "Seedarr Swarm Simulator";

    public string Version { get; set; } = "1.3.0";

    public bool IsRunning { get; set; } = true;

    public int ActiveTorrents { get; set; }

    public int DownloadingTorrents { get; set; }

    public int SeedingTorrents { get; set; }

    public int PausedTorrents { get; set; }

    public long TotalDownloadSpeed { get; set; }

    public long TotalUploadSpeed { get; set; }

    public long TotalProtocolDownloadSpeed { get; set; }

    public long TotalProtocolUploadSpeed { get; set; }

    public long TotalDataDownloaded { get; set; }

    public long TotalDataUploaded { get; set; }

    public long TotalProtocolDownloaded { get; set; }

    public long TotalProtocolUploaded { get; set; }

    public double ProtocolOverheadPercentage { get; set; }

    public int OpenConnections { get; set; }

    public int HalfOpenConnections { get; set; }

    public int MaxConnections { get; set; } = 500;

    public int ConnectedSeeds { get; set; }

    public int ConnectedLeechers { get; set; }

    public int TotalSwarmPeers { get; set; }

    public int DhtNodeCount { get; set; } = 384;

    public string DhtState { get; set; } = "Ready";

    public long DiskCacheBytesAllocated { get; set; }
}
