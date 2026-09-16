using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.Seeding.Distribution;

namespace NzbDrone.Core.Test.Seeding.Distribution;

[TestFixture]
public class SpeedDistributorTest
{
    private EqualDistributor _equalDistributor;
    private ParetoDistributor _paretoDistributor;

    [SetUp]
    public void Setup()
    {
        _equalDistributor = new EqualDistributor();
        _paretoDistributor = new ParetoDistributor();
    }

    // --- EqualDistributor tests ---

    [Test]
    public void Equal_should_distribute_evenly_across_torrents()
    {
        var totalSpeed = 1_000_000L;
        var count = 4;

        var speeds = _equalDistributor.Distribute(totalSpeed, count);

        Assert.That(speeds, Has.Length.EqualTo(count));
        Assert.That(speeds, Is.All.EqualTo(250_000L));
    }

    [Test]
    public void Equal_should_handle_single_torrent()
    {
        var totalSpeed = 500_000L;

        var speeds = _equalDistributor.Distribute(totalSpeed, 1);

        Assert.That(speeds, Has.Length.EqualTo(1));
        Assert.That(speeds[0], Is.EqualTo(500_000L));
    }

    [Test]
    public void Equal_should_return_empty_array_for_zero_torrents()
    {
        var speeds = _equalDistributor.Distribute(1_000_000L, 0);

        Assert.That(speeds, Is.Empty);
    }

    [Test]
    public void Equal_should_handle_zero_total_speed()
    {
        var speeds = _equalDistributor.Distribute(0L, 5);

        Assert.That(speeds, Has.Length.EqualTo(5));
        Assert.That(speeds, Is.All.EqualTo(0L));
    }

    [Test]
    public void Equal_should_distribute_remainder_without_truncation_loss()
    {
        // 1000 / 3 = 333 per torrent, remainder 1 is assigned to first torrent
        var speeds = _equalDistributor.Distribute(1000L, 3);

        Assert.That(speeds, Has.Length.EqualTo(3));
        Assert.That(speeds[0], Is.EqualTo(334L));
        Assert.That(speeds[1], Is.EqualTo(333L));
        Assert.That(speeds[2], Is.EqualTo(333L));
        Assert.That(speeds.Sum(), Is.EqualTo(1000L));
    }

    [Test]
    public void Equal_should_handle_large_torrent_count()
    {
        var totalSpeed = 10_485_760L; // 10 MB/s
        var count = 1000;

        var speeds = _equalDistributor.Distribute(totalSpeed, count);

        Assert.That(speeds, Has.Length.EqualTo(count));
        Assert.That(speeds.Sum(), Is.EqualTo(totalSpeed));
        Assert.That(speeds[0], Is.EqualTo(10_486L));
        Assert.That(speeds[999], Is.EqualTo(10_485L));
    }

    [TestCase(7, 1_000_000L)]
    [TestCase(50, 5_000_000L)]
    [TestCase(100, 10_000_000L)]
    public void Equal_should_conserve_bandwidth_exactly_and_prevent_tail_starvation(int count, long totalSpeed)
    {
        var speeds = _equalDistributor.Distribute(totalSpeed, count);

        Assert.That(speeds, Has.Length.EqualTo(count));
        Assert.That(speeds.Sum(), Is.EqualTo(totalSpeed));
        Assert.That(speeds, Is.All.GreaterThanOrEqualTo(1L));
    }

    [Test]
    public void Equal_name_should_be_equal()
    {
        Assert.That(_equalDistributor.Name, Is.EqualTo("Equal"));
    }

    // --- ParetoDistributor tests ---

    [Test]
    public void Pareto_should_return_correct_number_of_speeds()
    {
        var count = 5;

        var speeds = _paretoDistributor.Distribute(1_000_000L, count);

        Assert.That(speeds, Has.Length.EqualTo(count));
    }

    [Test]
    public void Pareto_should_sum_to_exact_total_speed()
    {
        var totalSpeed = 1_000_000L;
        var count = 10;

        var speeds = _paretoDistributor.Distribute(totalSpeed, count);

        Assert.That(speeds.Sum(), Is.EqualTo(totalSpeed));
    }

    [TestCase(7, 1_000_000L)]
    [TestCase(50, 5_000_000L)]
    [TestCase(100, 10_000_000L)]
    public void Pareto_should_conserve_bandwidth_exactly_and_prevent_tail_starvation(int count, long totalSpeed)
    {
        var speeds = _paretoDistributor.Distribute(totalSpeed, count);

        Assert.That(speeds, Has.Length.EqualTo(count));
        Assert.That(speeds.Sum(), Is.EqualTo(totalSpeed));
        Assert.That(speeds, Is.All.GreaterThanOrEqualTo(1L));
    }

    [Test]
    public void Pareto_should_assign_highest_speed_to_first_torrent()
    {
        var speeds = _paretoDistributor.Distribute(1_000_000L, 5);

        Assert.That(speeds[0], Is.GreaterThan(speeds[1]));
        Assert.That(speeds[0], Is.GreaterThan(speeds[4]));
    }

    [Test]
    public void Pareto_should_produce_descending_speeds()
    {
        var speeds = _paretoDistributor.Distribute(1_000_000L, 10);

        for (var i = 1; i < speeds.Length; i++)
        {
            Assert.That(
                speeds[i],
                Is.LessThanOrEqualTo(speeds[i - 1]),
                $"Speed at index {i} should be <= speed at index {i - 1}");
        }
    }

    [Test]
    public void Pareto_should_return_empty_array_for_zero_torrents()
    {
        var speeds = _paretoDistributor.Distribute(1_000_000L, 0);

        Assert.That(speeds, Is.Empty);
    }

    [Test]
    public void Pareto_should_handle_single_torrent()
    {
        var totalSpeed = 500_000L;

        var speeds = _paretoDistributor.Distribute(totalSpeed, 1);

        Assert.That(speeds, Has.Length.EqualTo(1));
        Assert.That(speeds[0], Is.EqualTo(totalSpeed));
    }

    [Test]
    public void Pareto_should_handle_zero_total_speed()
    {
        var speeds = _paretoDistributor.Distribute(0L, 5);

        Assert.That(speeds, Has.Length.EqualTo(5));
        Assert.That(speeds, Is.All.EqualTo(0L));
    }

    [Test]
    public void Pareto_should_give_disproportionate_share_to_top_torrents()
    {
        var totalSpeed = 1_000_000L;
        var count = 10;

        var speeds = _paretoDistributor.Distribute(totalSpeed, count);

        // Top 20% (2 torrents) should get significantly more than 20% of the total
        var topTwoSum = speeds[0] + speeds[1];
        var equalShare = totalSpeed * 2 / count; // 200,000

        Assert.That(
            topTwoSum,
            Is.GreaterThan(equalShare),
            "Top 20% of torrents should receive more than equal share under Pareto distribution");
    }

    [Test]
    public void Pareto_name_should_be_pareto()
    {
        Assert.That(_paretoDistributor.Name, Is.EqualTo("Pareto"));
    }

    [Test]
    public void Pareto_should_return_all_non_negative_speeds()
    {
        var speeds = _paretoDistributor.Distribute(1_000_000L, 20);

        Assert.That(speeds, Is.All.GreaterThanOrEqualTo(0L));
    }

    // --- Cross-distributor comparison tests ---

    [Test]
    public void Equal_and_pareto_should_distribute_same_total_differently()
    {
        var totalSpeed = 1_000_000L;
        var count = 5;

        var equalSpeeds = _equalDistributor.Distribute(totalSpeed, count);
        var paretoSpeeds = _paretoDistributor.Distribute(totalSpeed, count);

        // Equal should have all the same values
        Assert.That(equalSpeeds.Distinct().Count(), Is.EqualTo(1));

        // Pareto should have different values (descending)
        Assert.That(paretoSpeeds.Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public void Both_distributors_should_handle_two_torrents()
    {
        var totalSpeed = 1_000_000L;
        var count = 2;

        var equalSpeeds = _equalDistributor.Distribute(totalSpeed, count);
        var paretoSpeeds = _paretoDistributor.Distribute(totalSpeed, count);

        Assert.That(equalSpeeds, Has.Length.EqualTo(2));
        Assert.That(paretoSpeeds, Has.Length.EqualTo(2));

        // Equal: both should be 500k
        Assert.That(equalSpeeds[0], Is.EqualTo(500_000L));
        Assert.That(equalSpeeds[1], Is.EqualTo(500_000L));

        // Pareto: first should get more than second
        Assert.That(paretoSpeeds[0], Is.GreaterThan(paretoSpeeds[1]));
    }

    // --- ISpeedDistributor interface compliance ---

    [TestCase(typeof(EqualDistributor))]
    [TestCase(typeof(ParetoDistributor))]
    public void Distributor_should_implement_ISpeedDistributor(System.Type distributorType)
    {
        Assert.That(typeof(ISpeedDistributor).IsAssignableFrom(distributorType));
    }
}
