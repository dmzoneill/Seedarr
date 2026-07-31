using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Simulation.Swarm;

namespace NzbDrone.Core.Test.Simulation.Swarm;

[TestFixture]
public class SwarmAnalyzerTest
{
    private IConfigService _configService;
    private SwarmAnalyzer _analyzer;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.SwarmIntelligenceEnabled.Returns(true);
        _configService.SwarmAdaptationRate.Returns(1.0);
        _configService.SwarmPeerAnalysisDepth.Returns(0);
        _analyzer = new SwarmAnalyzer(_configService);
    }

    [Test]
    public void Analyze_should_return_maintain_when_swarm_intelligence_disabled()
    {
        _configService.SwarmIntelligenceEnabled.Returns(false);

        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 5, PieceAvailability = 3.0 };
        var result = _analyzer.Analyze(snapshot);

        Assert.That(result.Recommendation, Is.EqualTo(SeedingRecommendation.Maintain));
        Assert.That(result.Confidence, Is.EqualTo(1.0));
        Assert.That(result.Reason, Does.Contain("disabled"));
    }

    [Test]
    public void Analyze_should_return_boost_for_rare_content()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 2, LeechCount = 5, PieceAvailability = 1.0 };
        var result = _analyzer.Analyze(snapshot);

        Assert.That(result.Recommendation, Is.EqualTo(SeedingRecommendation.Boost));
        Assert.That(result.Reason, Does.Contain("Rare"));
    }

    [Test]
    public void Analyze_should_return_pause_when_no_leeches_and_saturated()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 20, LeechCount = 0, PieceAvailability = 5.0 };
        var result = _analyzer.Analyze(snapshot);

        Assert.That(result.Recommendation, Is.EqualTo(SeedingRecommendation.Pause));
        Assert.That(result.Reason, Does.Contain("No active leeches"));
    }

    [Test]
    public void Analyze_should_return_reduce_for_oversaturated_swarm()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 30, LeechCount = 5, PieceAvailability = 4.0 };
        var result = _analyzer.Analyze(snapshot);

        Assert.That(result.Recommendation, Is.EqualTo(SeedingRecommendation.Reduce));
        Assert.That(result.Reason, Does.Contain("Oversaturated"));
    }

    [Test]
    public void Analyze_should_return_pause_when_no_leeches_and_low_saturation()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 0, PieceAvailability = 1.0 };
        var result = _analyzer.Analyze(snapshot);

        Assert.That(result.Recommendation, Is.EqualTo(SeedingRecommendation.Pause));
        Assert.That(result.Reason, Does.Contain("No active leeches"));
    }

    [Test]
    public void Analyze_should_return_maintain_for_healthy_swarm()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 10, PieceAvailability = 4.0 };
        var result = _analyzer.Analyze(snapshot);

        Assert.That(result.Recommendation, Is.EqualTo(SeedingRecommendation.Maintain));
        Assert.That(result.Reason, Does.Contain("healthy"));
    }

    [Test]
    public void Analyze_should_return_maintain_moderate_for_moderate_swarm()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 5, LeechCount = 10, PieceAvailability = 2.5 };
        var result = _analyzer.Analyze(snapshot);

        Assert.That(result.Recommendation, Is.EqualTo(SeedingRecommendation.Maintain));
        Assert.That(result.Reason, Does.Contain("moderate"));
    }

    [Test]
    public void Analyze_should_scale_confidence_by_adaptation_rate()
    {
        _configService.SwarmAdaptationRate.Returns(0.5);

        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 10, PieceAvailability = 4.0 };
        var result = _analyzer.Analyze(snapshot);

        Assert.That(result.Confidence, Is.LessThanOrEqualTo(0.5));
    }

    [Test]
    public void Analyze_should_have_full_confidence_when_adaptation_rate_is_1()
    {
        _configService.SwarmAdaptationRate.Returns(1.0);

        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 10, PieceAvailability = 4.0 };
        var result = _analyzer.Analyze(snapshot);

        Assert.That(result.Confidence, Is.GreaterThan(0));
    }

    [Test]
    public void ComputeMetrics_should_calculate_seed_leech_ratio()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 5, PieceAvailability = 3.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.SeedLeechRatio, Is.EqualTo(2.0));
    }

    [Test]
    public void ComputeMetrics_should_handle_zero_leeches()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 0, PieceAvailability = 3.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.SeedLeechRatio, Is.EqualTo(10.0));
    }

    [Test]
    public void ComputeMetrics_should_normalize_piece_availability()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 5, PieceAvailability = 5.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.PieceAvailabilityScore, Is.EqualTo(1.0));
    }

    [Test]
    public void ComputeMetrics_should_clamp_availability_above_ceiling()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 5, PieceAvailability = 10.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.PieceAvailabilityScore, Is.EqualTo(1.0));
    }

    [Test]
    public void ComputeMetrics_should_handle_zero_availability()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 5, PieceAvailability = 0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.PieceAvailabilityScore, Is.EqualTo(0));
    }

    [Test]
    public void ComputeMetrics_should_detect_rare_content()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 2, LeechCount = 10, PieceAvailability = 1.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.IsRareContent, Is.True);
    }

    [Test]
    public void ComputeMetrics_should_not_flag_rare_when_seeds_above_threshold()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 5, PieceAvailability = 1.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.IsRareContent, Is.False);
    }

    [Test]
    public void ComputeMetrics_should_not_flag_rare_when_availability_above_threshold()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 2, LeechCount = 5, PieceAvailability = 3.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.IsRareContent, Is.False);
    }

    [Test]
    public void ComputeMetrics_should_detect_healthy_swarm()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 10, PieceAvailability = 4.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.IsSwarmHealthy, Is.True);
    }

    [Test]
    public void ComputeMetrics_should_detect_unhealthy_swarm()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 1, LeechCount = 100, PieceAvailability = 0.5 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.IsSwarmHealthy, Is.False);
    }

    [Test]
    public void ComputeMetrics_should_compute_saturation_score()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 5, PieceAvailability = 3.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.SwarmSaturationScore, Is.GreaterThan(0));
        Assert.That(metrics.SwarmSaturationScore, Is.LessThanOrEqualTo(1.0));
    }

    [Test]
    public void ComputeMetrics_should_clamp_negative_seed_count()
    {
        var snapshot = new SwarmSnapshot { SeedCount = -5, LeechCount = 10, PieceAvailability = 3.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.SeedLeechRatio, Is.EqualTo(0));
    }

    [Test]
    public void ComputeMetrics_should_clamp_negative_leech_count()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = -5, PieceAvailability = 3.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.SeedLeechRatio, Is.EqualTo(10.0));
    }

    [Test]
    public void ComputeMetrics_should_clamp_negative_availability()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 10, LeechCount = 5, PieceAvailability = -1.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.PieceAvailabilityScore, Is.EqualTo(0));
    }

    [Test]
    public void ComputeMetrics_should_cap_with_peer_analysis_depth()
    {
        _configService.SwarmPeerAnalysisDepth.Returns(5);

        var snapshot = new SwarmSnapshot { SeedCount = 100, LeechCount = 200, PieceAvailability = 3.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.SeedLeechRatio, Is.EqualTo(1.0));
    }

    [Test]
    public void ComputeMetrics_should_not_cap_when_depth_is_zero()
    {
        _configService.SwarmPeerAnalysisDepth.Returns(0);

        var snapshot = new SwarmSnapshot { SeedCount = 100, LeechCount = 200, PieceAvailability = 3.0 };
        var metrics = _analyzer.ComputeMetrics(snapshot);

        Assert.That(metrics.SeedLeechRatio, Is.EqualTo(0.5));
    }

    [Test]
    public void Analyze_should_return_non_null_metrics()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 5, LeechCount = 5, PieceAvailability = 2.5 };
        var result = _analyzer.Analyze(snapshot);

        Assert.That(result.Metrics, Is.Not.Null);
        Assert.That(result.Reason, Is.Not.Empty);
    }

    [Test]
    public void Analyze_reduce_confidence_should_scale_with_ratio()
    {
        var snapshot1 = new SwarmSnapshot { SeedCount = 30, LeechCount = 5, PieceAvailability = 4.0 };
        var result1 = _analyzer.Analyze(snapshot1);

        var snapshot2 = new SwarmSnapshot { SeedCount = 100, LeechCount = 5, PieceAvailability = 4.0 };
        var result2 = _analyzer.Analyze(snapshot2);

        Assert.That(result2.Confidence, Is.GreaterThanOrEqualTo(result1.Confidence));
    }
}
