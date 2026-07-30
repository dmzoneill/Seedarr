using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding.Distribution;

namespace NzbDrone.Core.Test.Seeding.Distribution;

[TestFixture]
public class SpeedDistributionManagerTest
{
    private IConfigService _configService;
    private ISpeedDistributor _equalDistributor;
    private ISpeedDistributor _paretoDistributor;
    private SpeedDistributionManager _manager;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _equalDistributor = Substitute.For<ISpeedDistributor>();
        _paretoDistributor = Substitute.For<ISpeedDistributor>();

        _equalDistributor.Name.Returns("Equal");
        _paretoDistributor.Name.Returns("Pareto");

        _configService.UploadDistributionAlgorithm.Returns("Equal");
        _configService.UploadDistributionSpreadPercentage.Returns(100);
        _configService.UploadRedistributionMode.Returns("tick");
        _configService.UploadCustomIntervalMinutes.Returns(5);
        _configService.DownloadDistributionAlgorithm.Returns("Equal");
        _configService.DownloadDistributionSpreadPercentage.Returns(100);
        _configService.DownloadRedistributionMode.Returns("tick");
        _configService.DownloadCustomIntervalMinutes.Returns(5);

        _equalDistributor.Distribute(Arg.Any<long>(), Arg.Any<int>())
            .Returns(callInfo =>
            {
                var total = callInfo.ArgAt<long>(0);
                var count = callInfo.ArgAt<int>(1);
                if (count == 0)
                {
                    return new long[0];
                }

                var share = total / count;
                return Enumerable.Repeat(share, count).ToArray();
            });

        _paretoDistributor.Distribute(Arg.Any<long>(), Arg.Any<int>())
            .Returns(callInfo =>
            {
                var total = callInfo.ArgAt<long>(0);
                var count = callInfo.ArgAt<int>(1);
                if (count == 0)
                {
                    return new long[0];
                }

                var result = new long[count];
                for (var i = 0; i < count; i++)
                {
                    result[i] = total / count;
                }

                result[0] += total - result.Sum();
                return result;
            });

        _manager = new SpeedDistributionManager(
            new List<ISpeedDistributor> { _equalDistributor, _paretoDistributor },
            _configService);
    }

    [Test]
    public void CurrentDistribution_should_return_configured_algorithm()
    {
        _configService.UploadDistributionAlgorithm.Returns("Pareto");

        Assert.That(_manager.CurrentDistribution, Is.EqualTo("Pareto"));
    }

    [Test]
    public void DistributeSpeeds_with_no_args_should_use_default_speed()
    {
        var speeds = _manager.DistributeSpeeds(3);

        Assert.That(speeds, Has.Length.EqualTo(3));
        _equalDistributor.Received(1).Distribute(1_048_576L, 3);
    }

    [Test]
    public void DistributeSpeeds_with_max_speed_should_use_provided_speed()
    {
        var speeds = _manager.DistributeSpeeds(3, 500_000L);

        Assert.That(speeds, Has.Length.EqualTo(3));
        _equalDistributor.Received(1).Distribute(500_000L, 3);
    }

    [Test]
    public void DistributeSpeeds_with_zero_max_speed_should_use_default()
    {
        _manager.DistributeSpeeds(3, 0);

        _equalDistributor.Received(1).Distribute(1_048_576L, 3);
    }

    [Test]
    public void DistributeSpeeds_should_select_configured_algorithm()
    {
        _configService.UploadDistributionAlgorithm.Returns("Pareto");

        _manager.DistributeSpeeds(3, 500_000L);

        _paretoDistributor.Received(1).Distribute(500_000L, 3);
        _equalDistributor.DidNotReceive().Distribute(Arg.Any<long>(), Arg.Any<int>());
    }

    [Test]
    public void DistributeSpeeds_should_fall_back_to_first_distributor_when_algorithm_not_found()
    {
        _configService.UploadDistributionAlgorithm.Returns("NonExistent");

        _manager.DistributeSpeeds(3, 500_000L);

        _equalDistributor.Received(1).Distribute(500_000L, 3);
    }

    [Test]
    public void DistributeSpeeds_should_apply_spread_percentage()
    {
        _configService.UploadDistributionSpreadPercentage.Returns(50);

        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 300_000, 200_000 });

        var speeds = _manager.DistributeSpeeds(2, 500_000);

        Assert.That(speeds, Has.Length.EqualTo(2));
        Assert.That(speeds[0], Is.Not.EqualTo(300_000));
    }

    [Test]
    public void DistributeUploadSpeeds_should_redistribute_on_tick_mode()
    {
        _configService.UploadRedistributionMode.Returns("tick");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeUploadSpeeds(3, 500_000L);

        _equalDistributor.Received(2).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeUploadSpeeds_should_not_redistribute_on_fixed_mode()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeUploadSpeeds(3, 500_000L);

        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeUploadSpeeds_fixed_mode_should_return_cached_array_when_inputs_unchanged()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        var first = _manager.DistributeUploadSpeeds(3, 500_000L);
        var second = _manager.DistributeUploadSpeeds(3, 500_000L);

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void DistributeUploadSpeeds_fixed_mode_should_return_new_array_when_inputs_change()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        var first = _manager.DistributeUploadSpeeds(3, 500_000L);
        var second = _manager.DistributeUploadSpeeds(4, 500_000L);

        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public void DistributeUploadSpeeds_should_redistribute_when_count_changes()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeUploadSpeeds(4, 500_000L);

        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 4);
    }

    [Test]
    public void DistributeUploadSpeeds_should_redistribute_when_max_speed_changes()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeUploadSpeeds(3, 600_000L);

        _equalDistributor.Received(2).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeUploadSpeeds_with_priority_weights_should_apply_weights()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 3)
            .Returns(new long[] { 100_000, 100_000, 100_000 });

        var weights = new double[] { 2.0, 1.0, 0.5 };
        var speeds = _manager.DistributeUploadSpeeds(3, 300_000L, weights);

        Assert.That(speeds, Has.Length.EqualTo(3));
        Assert.That(speeds[0], Is.GreaterThan(speeds[2]));
    }

    [Test]
    public void DistributeDownloadSpeeds_should_use_download_config()
    {
        _configService.DownloadDistributionAlgorithm.Returns("Equal");
        _configService.DownloadRedistributionMode.Returns("tick");

        _manager.DistributeDownloadSpeeds(3, 500_000L);

        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeDownloadSpeeds_should_not_redistribute_on_fixed_mode()
    {
        _configService.DownloadRedistributionMode.Returns("fixed");

        _manager.DistributeDownloadSpeeds(3, 500_000L);
        _manager.DistributeDownloadSpeeds(3, 500_000L);

        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeDownloadSpeeds_fixed_mode_should_return_cached_array_when_inputs_unchanged()
    {
        _configService.DownloadRedistributionMode.Returns("fixed");

        var first = _manager.DistributeDownloadSpeeds(3, 500_000L);
        var second = _manager.DistributeDownloadSpeeds(3, 500_000L);

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void DistributeDownloadSpeeds_fixed_mode_should_return_new_array_when_inputs_change()
    {
        _configService.DownloadRedistributionMode.Returns("fixed");

        var first = _manager.DistributeDownloadSpeeds(3, 500_000L);
        var second = _manager.DistributeDownloadSpeeds(3, 600_000L);

        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public void DistributeDownloadSpeeds_with_priority_weights_should_apply_weights()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 200_000, 200_000 });

        var weights = new double[] { 1.0, 2.0 };
        var speeds = _manager.DistributeDownloadSpeeds(2, 400_000L, weights);

        Assert.That(speeds, Has.Length.EqualTo(2));
        Assert.That(speeds[1], Is.GreaterThan(speeds[0]));
    }

    [Test]
    public void ApplyPriorityWeights_should_return_original_when_weights_null()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 250_000, 250_000 });

        var speeds = _manager.DistributeUploadSpeeds(2, 500_000L, null);

        Assert.That(speeds[0], Is.EqualTo(250_000));
        Assert.That(speeds[1], Is.EqualTo(250_000));
    }

    [Test]
    public void ApplyPriorityWeights_should_return_original_when_length_mismatch()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 250_000, 250_000 });

        var speeds = _manager.DistributeUploadSpeeds(2, 500_000L, new double[] { 1.0 });

        Assert.That(speeds[0], Is.EqualTo(250_000));
    }

    [Test]
    public void GetAvailableDistributions_should_return_all_distributor_names()
    {
        var names = _manager.GetAvailableDistributions();

        Assert.That(names, Has.Count.EqualTo(2));
        Assert.That(names, Does.Contain("Equal"));
        Assert.That(names, Does.Contain("Pareto"));
    }

    [Test]
    public void DistributeSpeeds_should_match_algorithm_case_insensitively()
    {
        _configService.UploadDistributionAlgorithm.Returns("pareto");

        _manager.DistributeSpeeds(3, 500_000L);

        _paretoDistributor.Received(1).Distribute(500_000L, 3);
    }

    [Test]
    public void DistributeUploadSpeeds_interval_mode_should_not_redistribute_before_interval()
    {
        _configService.UploadRedistributionMode.Returns("interval");
        _configService.UploadCustomIntervalMinutes.Returns(60);

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeUploadSpeeds(3, 500_000L);

        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
    }

    // The elapsed-interval branch of "interval" mode depends on DateTime.UtcNow, which is not
    // injectable; asserting a redistribution after the boundary would require either a flaky
    // real-time sleep or restructuring the manager around a clock abstraction. The cache-identity
    // tests above cover the redistribution decision indirectly (cached array returned when the
    // gate suppresses redistribution), so only the pre-boundary case is asserted here.
}
