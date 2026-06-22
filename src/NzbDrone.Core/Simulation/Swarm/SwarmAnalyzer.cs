using System;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Simulation.Swarm;

public interface ISwarmAnalyzer
{
    SwarmRecommendation Analyze(SwarmSnapshot snapshot);
    SwarmMetrics ComputeMetrics(SwarmSnapshot snapshot);
}

public class SwarmAnalyzer : ISwarmAnalyzer
{
    private const double RareContentSeedThreshold = 3;
    private const double RareContentAvailabilityThreshold = 2.0;
    private const double HighRatioThreshold = 2.0;
    private const double HealthyAvailabilityThreshold = 3.0;
    private const double SaturationThreshold = 0.8;
    private const double AvailabilityNormalizationCeiling = 5.0;
    private const double RatioNormalizationCeiling = 5.0;
    private const double SaturationAvailabilityCeiling = 3.0;
    private const double HealthyAvailabilityScoreFloor = 0.6;
    private const double HealthyRatioFloor = 0.5;

    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public SwarmAnalyzer(IConfigService configService)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public SwarmRecommendation Analyze(SwarmSnapshot snapshot)
    {
        if (!_configService.SwarmIntelligenceEnabled)
        {
            _logger.Debug("Swarm intelligence disabled, returning neutral Maintain recommendation");
            var neutralMetrics = ComputeMetrics(snapshot);
            return new SwarmRecommendation
            {
                Recommendation = SeedingRecommendation.Maintain,
                Metrics = neutralMetrics,
                Reason = "Swarm intelligence is disabled; maintaining current seeding level",
                Confidence = 1.0
            };
        }

        var metrics = ComputeMetrics(snapshot);
        var (recommendation, reason, confidence) = EvaluateRecommendation(snapshot, metrics);

        // Apply swarmAdaptationRate to scale confidence toward neutral (1.0 = full confidence, 0.0 = no adaptation)
        var adaptationRate = _configService.SwarmAdaptationRate;
        confidence *= adaptationRate;

        _logger.Debug(
            "Swarm analysis: seeds={0}, leeches={1}, ratio={2:F2}, availabilityScore={3:F2}, saturation={4:F2} -> {5} (confidence={6:F2}, reason={7}, adaptationRate={8:F2})",
            snapshot.SeedCount,
            snapshot.LeechCount,
            metrics.SeedLeechRatio,
            metrics.PieceAvailabilityScore,
            metrics.SwarmSaturationScore,
            recommendation,
            confidence,
            reason,
            adaptationRate);

        return new SwarmRecommendation
        {
            Recommendation = recommendation,
            Metrics = metrics,
            Reason = reason,
            Confidence = confidence
        };
    }

    public SwarmMetrics ComputeMetrics(SwarmSnapshot snapshot)
    {
        var seedCount = Math.Max(snapshot.SeedCount, 0);
        var leechCount = Math.Max(snapshot.LeechCount, 0);
        var availability = Math.Max(snapshot.PieceAvailability, 0.0);

        // Limit peer analysis depth: cap seed and leech counts used in analysis
        var analysisDepth = _configService.SwarmPeerAnalysisDepth;
        if (analysisDepth > 0)
        {
            seedCount = Math.Min(seedCount, analysisDepth);
            leechCount = Math.Min(leechCount, analysisDepth);
        }

        var seedLeechRatio = ComputeSeedLeechRatio(seedCount, leechCount);
        var availabilityScore = NormalizePieceAvailability(availability);
        var saturationScore = ComputeSwarmSaturation(seedLeechRatio, availability);
        var isRare = seedCount <= RareContentSeedThreshold
                     && availability < RareContentAvailabilityThreshold;
        var isHealthy = availabilityScore > HealthyAvailabilityScoreFloor
                        && seedLeechRatio >= HealthyRatioFloor;

        return new SwarmMetrics
        {
            SeedLeechRatio = seedLeechRatio,
            PieceAvailabilityScore = availabilityScore,
            SwarmSaturationScore = saturationScore,
            IsRareContent = isRare,
            IsSwarmHealthy = isHealthy
        };
    }

    private static double ComputeSeedLeechRatio(int seedCount, int leechCount)
    {
        return leechCount > 0
            ? (double)seedCount / leechCount
            : seedCount;
    }

    private static double NormalizePieceAvailability(double pieceAvailability)
    {
        return Math.Clamp(pieceAvailability / AvailabilityNormalizationCeiling, 0.0, 1.0);
    }

    private static double ComputeSwarmSaturation(double seedLeechRatio, double pieceAvailability)
    {
        var ratioFactor = Math.Clamp(seedLeechRatio / RatioNormalizationCeiling, 0.0, 1.0);
        var availabilityFactor = Math.Clamp(pieceAvailability / SaturationAvailabilityCeiling, 0.0, 1.0);
        return ratioFactor * availabilityFactor;
    }

    private (SeedingRecommendation Recommendation, string Reason, double Confidence) EvaluateRecommendation(
        SwarmSnapshot snapshot,
        SwarmMetrics metrics)
    {
        if (snapshot.LeechCount <= 0 && metrics.SwarmSaturationScore > SaturationThreshold)
        {
            var confidence = Math.Clamp(0.7 + (metrics.SwarmSaturationScore - SaturationThreshold), 0.0, 1.0);
            return (SeedingRecommendation.Pause,
                string.Format(
                    "No active leeches and swarm saturation is high ({0:F2}); seeding resources can be freed",
                    metrics.SwarmSaturationScore),
                confidence);
        }

        if (metrics.IsRareContent)
        {
            var confidence = Math.Clamp(1.0 - (metrics.PieceAvailabilityScore * 0.5), 0.0, 1.0);
            return (SeedingRecommendation.Boost,
                string.Format(
                    "Rare content: only {0} seed(s) with piece availability {1:F1}; boosting preserves swarm health",
                    snapshot.SeedCount,
                    snapshot.PieceAvailability),
                confidence);
        }

        if (metrics.SeedLeechRatio > HighRatioThreshold
            && snapshot.SeedCount > snapshot.LeechCount * 2
            && snapshot.PieceAvailability >= HealthyAvailabilityThreshold)
        {
            var confidence = Math.Clamp(0.6 + ((metrics.SeedLeechRatio - HighRatioThreshold) * 0.1), 0.0, 1.0);
            return (SeedingRecommendation.Reduce,
                string.Format(
                    "Oversaturated swarm: seed/leech ratio {0:F1} with {1} seeds vs {2} leeches; reducing frees bandwidth",
                    metrics.SeedLeechRatio,
                    snapshot.SeedCount,
                    snapshot.LeechCount),
                confidence);
        }

        if (snapshot.LeechCount <= 0)
        {
            return (SeedingRecommendation.Pause,
                "No active leeches requesting data; pausing until demand resumes",
                0.6);
        }

        var maintainConfidence = metrics.IsSwarmHealthy ? 0.8 : 0.5;
        var healthLabel = metrics.IsSwarmHealthy ? "healthy" : "moderate";
        return (SeedingRecommendation.Maintain,
            string.Format(
                "Swarm is {0}: seed/leech ratio {1:F1}, availability score {2:F2}; maintaining current seeding level",
                healthLabel,
                metrics.SeedLeechRatio,
                metrics.PieceAvailabilityScore),
            maintainConfidence);
    }
}
