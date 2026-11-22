using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding.Distribution;

namespace NzbDrone.Core.Test.Seeding.Distribution;

[TestFixture]
public class SpeedDistributionShapeTest
{
    [Test]
    public void Priority_weights_modulate_distributor_shape_instead_of_replacing_it()
    {
        var speeds = new long[] { 900_000, 100_000 };

        var even = SpeedDistributionManager.ApplyPriorityWeights(speeds, new[] { 1.0, 1.0 });
        var boosted = SpeedDistributionManager.ApplyPriorityWeights(speeds, new[] { 1.0, 10.0 });

        Assert.That(even.Sum(), Is.EqualTo(1_000_000));
        Assert.That(even[0], Is.GreaterThan(even[1] * 4));

        Assert.That(boosted.Sum(), Is.EqualTo(1_000_000));
        Assert.That(boosted[1], Is.GreaterThan(boosted[0]));
        Assert.That(boosted[0], Is.GreaterThan(200_000));
    }

    [Test]
    public void Configured_algorithm_changes_weighted_allocations()
    {
        var algorithms = new[] { "Equal", "Pareto", "PowerLaw", "LogNormal" };
        var results = algorithms.Select(name => Weighted(name, 10, 10_000_000)).ToArray();

        Assert.That(results.All(r => r.Sum() == 10_000_000), Is.True);
        Assert.That(results[0].Max() - results[0].Min(), Is.LessThanOrEqualTo(1));

        for (var i = 1; i < results.Length; i++)
        {
            Assert.That(results[i][0], Is.GreaterThan(results[i][results[i].Length - 1] * 2), algorithms[i]);
            Assert.That(results[i], Is.Not.EqualTo(results[0]), algorithms[i]);
        }
    }

    [Test]
    public void Zero_speed_items_receive_surplus_after_shaped_items_hit_their_cap()
    {
        var speeds = new long[] { 100_000, 0, 0 };
        var weights = new double[] { 1.0, 1.0, 3.0 };
        var caps = new long[] { 20_000L, 0L, 0L };

        var result = SpeedDistributionManager.ApplyPriorityWeights(speeds, weights, caps);

        Assert.That(result.Sum(), Is.EqualTo(100_000));
        Assert.That(result[0], Is.EqualTo(20_000));
        Assert.That(result[1], Is.EqualTo(22_560));
        Assert.That(result[2], Is.EqualTo(57_440));
    }

    [Test]
    public void Pareto_with_equal_priorities_keeps_a_heavy_tail()
    {
        var config = Config();
        config.UploadDistributionAlgorithm.Returns("Pareto");
        var manager = Manager(config);

        var raw = manager.DistributeUploadSpeeds(10, 10_000_000);
        manager.InvalidateCache();
        var weighted = manager.DistributeUploadSpeeds(10, 10_000_000, Enumerable.Repeat(1.0, 10).ToArray());

        Assert.That(raw.Sum(), Is.EqualTo(10_000_000));
        Assert.That(weighted.Sum(), Is.EqualTo(10_000_000));
        Assert.That(raw[0], Is.GreaterThan(raw[9] * 5));
        Assert.That(weighted[0], Is.GreaterThan(weighted[9] * 2));
        Assert.That(weighted.Distinct().Count(), Is.GreaterThan(1));
        Assert.That(weighted, Is.Not.EqualTo(Enumerable.Repeat(1_000_000L, 10).ToArray()));
    }

    private static long[] Weighted(string algorithm, int count, long total)
    {
        var config = Config();
        config.UploadDistributionAlgorithm.Returns(algorithm);
        var manager = Manager(config);
        return manager.DistributeUploadSpeeds(count, total, Enumerable.Repeat(1.0, count).ToArray());
    }

    private static IConfigService Config()
    {
        var config = Substitute.For<IConfigService>();
        config.UploadDistributionAlgorithm.Returns("Equal");
        config.UploadDistributionSpreadPercentage.Returns(100);
        config.UploadRedistributionMode.Returns("tick");
        config.UploadCustomIntervalMinutes.Returns(5);
        config.DownloadDistributionAlgorithm.Returns("Equal");
        config.DownloadDistributionSpreadPercentage.Returns(100);
        config.DownloadRedistributionMode.Returns("tick");
        config.DownloadCustomIntervalMinutes.Returns(5);
        return config;
    }

    private static SpeedDistributionManager Manager(IConfigService config)
    {
        return new SpeedDistributionManager(
            new ISpeedDistributor[]
            {
                new EqualDistributor(),
                new ParetoDistributor(),
                new PowerLawDistributor(),
                new LogNormalDistributor()
            },
            config,
            new SystemClock());
    }
}
