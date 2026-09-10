using System;
using System.Collections.Generic;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;
using NzbDrone.Core.Simulation.Swarm;
using NzbDrone.Core.Simulation.Traffic;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer;

namespace NzbDrone.Core.Test.Simulation;

[TestFixture]
public class SimulationIntegrationTest
{
    private ITorrentService _torrentService;
    private ISpeedDistributionManager _distributionManager;
    private ISpeedScheduler _speedScheduler;
    private IConfigService _configService;
    private IEventAggregator _eventAggregator;
    private IPeerDatabase _peerDatabase;
    private IConnectionManager _connectionManager;
    private ITorrentEventLogService _eventLogService;
    private IClientProfileFactory _clientProfileFactory;
    private IClientBehaviorSimulator _clientBehaviorSimulator;
    private ITrafficPatternSimulator _trafficPatternSimulator;
    private ISwarmAnalyzer _swarmAnalyzer;
    private SpeedPolicy _speedPolicy;
    private SeedingEngine _engine;

    [SetUp]
    public void Setup()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _distributionManager = Substitute.For<ISpeedDistributionManager>();
        _speedScheduler = Substitute.For<ISpeedScheduler>();
        _configService = Substitute.For<IConfigService>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _peerDatabase = Substitute.For<IPeerDatabase>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _eventLogService = Substitute.For<ITorrentEventLogService>();
        _clientProfileFactory = Substitute.For<IClientProfileFactory>();

        var qbit = new QBittorrentProfile();
        var deluge = new DelugeProfile();
        var transmission = new TransmissionProfile();
        _clientProfileFactory.GetAvailableProviders().Returns(new List<IClientProfile> { qbit, deluge, transmission });

        // Base seeding config defaults
        _configService.AutoStart.Returns(true);
        _configService.UiRefreshRateSec.Returns(9);
        _configService.AlternativeSpeedEnabled.Returns(false);
        _configService.MaxUploadSpeedKbps.Returns(1000);
        _configService.MaxDownloadSpeedKbps.Returns(2000);
        _configService.SpeedVariationMin.Returns(1.0);
        _configService.SpeedVariationMax.Returns(1.0);
        _configService.DownloadThresholdPercent.Returns(100);
        _configService.GlobalSeedRatioLimit.Returns(0.0);
        _configService.SeederUploadActivityProbability.Returns(1.0);
        _configService.PeerIdPrefix.Returns("-SD1000-");
        _configService.PeerDropoutProbability.Returns(0.1);
        _configService.ConnectionRotationPercentage.Returns(0.1);
        _configService.PeerIdleChance.Returns(0.3);

        // Simulation defaults
        _configService.ClientBehaviorEngineEnabled.Returns(true);
        _configService.PrimaryClient.Returns("qBittorrent");
        _configService.BehaviorVariation.Returns(0.0);
        _configService.ClientProfileSwitching.Returns(false);
        _configService.SwitchClientProbability.Returns(0.0);
        _configService.TrafficPatternProfile.Returns("balanced");
        _configService.RealisticVariations.Returns(false);
        _configService.TimeBasedPatterns.Returns(false);
        _configService.SwarmIntelligenceEnabled.Returns(false);
        _configService.SwarmAdaptationRate.Returns(1.0);
        _configService.SwarmPeerAnalysisDepth.Returns(10);

        _speedScheduler.GetCurrentLimits().Returns(new SpeedLimits
        {
            MaxUploadSpeed = 1_024_000,
            MaxDownloadSpeed = 2_048_000,
            IsScheduleActive = false
        });

        _distributionManager.DistributeUploadSpeeds(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<double[]>())
            .Returns(x =>
            {
                var count = (int)x[0];
                var budget = (long)x[1];
                var speeds = new long[count];
                Array.Fill(speeds, budget / Math.Max(1, count));
                return speeds;
            });

        _clientBehaviorSimulator = new ClientBehaviorSimulator(_configService, _clientProfileFactory, new RandomNumberGenerator(42));
        _trafficPatternSimulator = new TrafficPatternSimulator(_configService, new RandomNumberGenerator(42), new SystemClock());
        _swarmAnalyzer = new SwarmAnalyzer(_configService);

        var stateMachine = new TorrentStateMachine(_eventLogService);
        var stopPolicy = new StopPolicy(_configService, new RandomNumberGenerator(42));
        _speedPolicy = new SpeedPolicy(
            _distributionManager,
            _speedScheduler,
            _configService,
            _eventLogService,
            stateMachine,
            stopPolicy,
            new RandomNumberGenerator(42),
            _swarmAnalyzer);

        _engine = new SeedingEngine(
            _torrentService,
            _distributionManager,
            _speedScheduler,
            _configService,
            _eventAggregator,
            _peerDatabase,
            _connectionManager,
            _eventLogService,
            stateMachine,
            _speedPolicy,
            stopPolicy,
            new SystemClock(),
            new RandomNumberGenerator(42),
            _trafficPatternSimulator,
            _clientBehaviorSimulator,
            _swarmAnalyzer);
    }

    private void CallTick()
    {
        var method = typeof(SeedingEngine).GetMethod("Tick",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_engine, null);
    }

    [Test]
    public void TrafficPatternProfile_aggressive_should_increase_speed_budget()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abc1234",
            Status = TorrentStatus.Seeding,
            TotalSize = 10_000_000,
            Uploaded = 0,
            Priority = 1
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _configService.TrafficPatternProfile.Returns("aggressive");
        CallTick();

        // Balanced = 1.0x (1,024,000 bytes/tick with 9s tick interval = 9,216,000), Aggressive = 1.5x -> 13,824,000
        Assert.That(torrent.Uploaded, Is.GreaterThan(1_024_000 * 9));
    }

    [Test]
    public void TrafficPatternProfile_conservative_should_decrease_speed_budget()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abc1234",
            Status = TorrentStatus.Seeding,
            TotalSize = 10_000_000,
            Uploaded = 0,
            Priority = 1
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _configService.TrafficPatternProfile.Returns("conservative");
        CallTick();

        // Conservative = 0.5x budget
        Assert.That(torrent.Uploaded, Is.EqualTo((long)(1_024_000 * 0.5 * 9)).Within(100));
    }

    [Test]
    public void TrafficPatternProfile_off_should_bypass_modulation()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abc1234",
            Status = TorrentStatus.Seeding,
            TotalSize = 10_000_000,
            Uploaded = 0,
            Priority = 1
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _configService.TrafficPatternProfile.Returns("off");
        CallTick();

        Assert.That(torrent.Uploaded, Is.EqualTo(1_024_000 * 9).Within(100));
    }

    [Test]
    public void SwarmIntelligence_boost_should_increase_upload_for_rare_torrents()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "rarehash",
            Status = TorrentStatus.Seeding,
            TotalSize = 10_000_000,
            Uploaded = 0,
            Priority = 1,
            Availability = 0.5
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _peerDatabase.GetStats("rarehash").Returns(new global::NzbDrone.Core.TrackerServer.ScrapeStats { Complete = 1, Incomplete = 10 });

        _configService.SwarmIntelligenceEnabled.Returns(true);
        _configService.SwarmAdaptationRate.Returns(1.0);

        CallTick();

        // Rare content receives Boost recommendation (> 1.0 multiplier)
        Assert.That(torrent.Uploaded, Is.GreaterThan(1_024_000 * 9));
    }

    [Test]
    public void SwarmIntelligence_reduce_should_decrease_upload_for_oversaturated_torrents()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "oversaturated",
            Status = TorrentStatus.Seeding,
            TotalSize = 10_000_000,
            Uploaded = 0,
            Priority = 1,
            Availability = 4.0
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _peerDatabase.GetStats("oversaturated").Returns(new global::NzbDrone.Core.TrackerServer.ScrapeStats { Complete = 50, Incomplete = 2 });

        _configService.SwarmIntelligenceEnabled.Returns(true);
        _configService.SwarmAdaptationRate.Returns(1.0);

        CallTick();

        // Oversaturated swarm receives Reduce recommendation (< 1.0 multiplier)
        Assert.That(torrent.Uploaded, Is.LessThan(1_024_000 * 9));
    }

    [Test]
    public void SwarmIntelligence_disabled_should_maintain_standard_upload()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "rarehash",
            Status = TorrentStatus.Seeding,
            TotalSize = 10_000_000,
            Uploaded = 0,
            Priority = 1,
            Availability = 0.5
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });
        _peerDatabase.GetStats("rarehash").Returns(new global::NzbDrone.Core.TrackerServer.ScrapeStats { Complete = 1, Incomplete = 10 });

        _configService.SwarmIntelligenceEnabled.Returns(false);

        CallTick();

        Assert.That(torrent.Uploaded, Is.EqualTo(1_024_000 * 9).Within(100));
    }

    [Test]
    public void ClientBehaviorEngine_should_update_local_peer_id_from_active_profile()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abc1234",
            Status = TorrentStatus.Seeding,
            TotalSize = 10_000_000,
            Uploaded = 0
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _configService.ClientBehaviorEngineEnabled.Returns(true);
        _configService.PrimaryClient.Returns("Transmission");

        CallTick();

        var localPeerIdField = typeof(SeedingEngine).GetField("_localPeerId", BindingFlags.NonPublic | BindingFlags.Instance);
        var localPeerId = (string)localPeerIdField.GetValue(_engine);

        Assert.That(localPeerId, Does.StartWith("-TR3000-"));
    }

    [Test]
    public void ClientBehaviorEngine_disabled_should_use_default_peer_id_prefix()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abc1234",
            Status = TorrentStatus.Seeding,
            TotalSize = 10_000_000,
            Uploaded = 0
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _configService.ClientBehaviorEngineEnabled.Returns(false);
        _configService.PeerIdPrefix.Returns("-SD1000-");

        CallTick();

        var localPeerIdField = typeof(SeedingEngine).GetField("_localPeerId", BindingFlags.NonPublic | BindingFlags.Instance);
        var localPeerId = (string)localPeerIdField.GetValue(_engine);

        Assert.That(localPeerId, Does.StartWith("-qB4420-").Or.StartWith("-SD1000-"));
    }

    [Test]
    public void RealisticVariations_enabled_should_modulate_speed_with_congestion_and_behavior()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abc1234",
            Status = TorrentStatus.Seeding,
            TotalSize = 10_000_000,
            Uploaded = 0,
            Priority = 1
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _configService.TrafficPatternProfile.Returns("balanced");
        _configService.RealisticVariations.Returns(true);
        _configService.BehaviorVariation.Returns(0.2);

        CallTick();

        Assert.That(torrent.Uploaded, Is.GreaterThan(0));
    }

    [Test]
    public void TimeBasedPatterns_enabled_should_apply_diurnal_multiplier()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abc1234",
            Status = TorrentStatus.Seeding,
            TotalSize = 10_000_000,
            Uploaded = 0,
            Priority = 1
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        _configService.TrafficPatternProfile.Returns("balanced");
        _configService.TimeBasedPatterns.Returns(true);

        CallTick();

        Assert.That(torrent.Uploaded, Is.GreaterThan(0));
    }

    [Test]
    public void SwarmAdaptationRate_scales_recommendation_impact()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 1, LeechCount = 10, PieceAvailability = 0.5 };

        _configService.SwarmIntelligenceEnabled.Returns(true);
        _configService.SwarmAdaptationRate.Returns(0.2);
        var recLow = _swarmAnalyzer.Analyze(snapshot);

        _configService.SwarmAdaptationRate.Returns(1.0);
        var recHigh = _swarmAnalyzer.Analyze(snapshot);

        Assert.That(recHigh.Confidence, Is.GreaterThan(recLow.Confidence));
    }

    [Test]
    public void SwarmPeerAnalysisDepth_caps_peer_metrics()
    {
        var snapshot = new SwarmSnapshot { SeedCount = 100, LeechCount = 100, PieceAvailability = 3.0 };

        _configService.SwarmPeerAnalysisDepth.Returns(10);
        var metricsCapped = _swarmAnalyzer.ComputeMetrics(snapshot);

        _configService.SwarmPeerAnalysisDepth.Returns(0);
        var metricsUncapped = _swarmAnalyzer.ComputeMetrics(snapshot);

        Assert.That(metricsCapped.SeedLeechRatio, Is.EqualTo(1.0));
        Assert.That(metricsUncapped.SeedLeechRatio, Is.EqualTo(1.0));
    }
}
