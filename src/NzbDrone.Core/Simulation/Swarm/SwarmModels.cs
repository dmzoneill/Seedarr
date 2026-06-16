using System;

namespace NzbDrone.Core.Simulation.Swarm;

public enum SeedingRecommendation
{
    Maintain,
    Boost,
    Reduce,
    Pause
}

public class SwarmSnapshot
{
    public int SeedCount { get; set; }
    public int LeechCount { get; set; }
    public double PieceAvailability { get; set; }
    public int TotalPieces { get; set; }
    public long TorrentSizeBytes { get; set; }
    public double UploadRateBytesPerSec { get; set; }
    public double DownloadRateBytesPerSec { get; set; }
    public double ShareRatio { get; set; }
    public TimeSpan SeedingDuration { get; set; }
}

public class SwarmMetrics
{
    public double SeedLeechRatio { get; set; }
    public double PieceAvailabilityScore { get; set; }
    public double SwarmSaturationScore { get; set; }
    public bool IsRareContent { get; set; }
    public bool IsSwarmHealthy { get; set; }
}

public class SwarmRecommendation
{
    public SeedingRecommendation Recommendation { get; set; }
    public SwarmMetrics Metrics { get; set; }
    public string Reason { get; set; }
    public double Confidence { get; set; }
}
