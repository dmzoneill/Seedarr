using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.Seeding.Distribution;

namespace NzbDrone.Core.Test.Seeding.Distribution;

[TestFixture]
public class PowerLawDistributorTest
{
    private PowerLawDistributor _distributor;

    [SetUp]
    public void Setup()
    {
        _distributor = new PowerLawDistributor();
    }

    [Test]
    public void Name_should_return_powerlaw()
    {
        Assert.That(_distributor.Name, Is.EqualTo("PowerLaw"));
    }

    [Test]
    public void Distribute_should_return_empty_array_for_zero_torrents()
    {
        var speeds = _distributor.Distribute(1_000_000L, 0);

        Assert.That(speeds, Is.Empty);
    }

    [Test]
    public void Distribute_should_return_single_element_equal_to_total_for_one_torrent()
    {
        var speeds = _distributor.Distribute(500_000L, 1);

        Assert.That(speeds, Has.Length.EqualTo(1));
        Assert.That(speeds[0], Is.EqualTo(500_000L));
    }

    [Test]
    public void Distribute_should_return_correct_count()
    {
        var speeds = _distributor.Distribute(1_000_000L, 5);

        Assert.That(speeds, Has.Length.EqualTo(5));
    }

    [Test]
    public void Distribute_should_sum_to_at_most_total_speed()
    {
        var totalSpeed = 1_000_000L;
        var speeds = _distributor.Distribute(totalSpeed, 10);

        var sum = speeds.Sum();
        Assert.That(sum, Is.LessThanOrEqualTo(totalSpeed));
        Assert.That(sum, Is.GreaterThan(totalSpeed * 0.90));
    }

    [Test]
    public void Distribute_should_return_all_non_negative_speeds()
    {
        var speeds = _distributor.Distribute(1_000_000L, 20);

        Assert.That(speeds, Is.All.GreaterThanOrEqualTo(0L));
    }

    [Test]
    public void Distribute_should_assign_highest_speed_to_first_torrent()
    {
        var speeds = _distributor.Distribute(1_000_000L, 5);

        Assert.That(speeds[0], Is.GreaterThan(speeds[4]));
    }

    [Test]
    public void Distribute_should_produce_descending_speeds()
    {
        var speeds = _distributor.Distribute(1_000_000L, 10);

        for (var i = 1; i < speeds.Length; i++)
        {
            Assert.That(
                speeds[i],
                Is.LessThanOrEqualTo(speeds[i - 1]),
                $"Speed at index {i} should be <= speed at index {i - 1}");
        }
    }

    [Test]
    public void Distribute_should_handle_zero_total_speed()
    {
        var speeds = _distributor.Distribute(0L, 5);

        Assert.That(speeds, Has.Length.EqualTo(5));
        Assert.That(speeds, Is.All.EqualTo(0L));
    }

    [Test]
    public void Distribute_should_give_disproportionate_share_to_first_torrent()
    {
        var totalSpeed = 1_000_000L;
        var speeds = _distributor.Distribute(totalSpeed, 10);

        var equalShare = totalSpeed / 10;
        Assert.That(speeds[0], Is.GreaterThan(equalShare));
    }

    [Test]
    public void Distribute_should_handle_two_torrents()
    {
        var speeds = _distributor.Distribute(1_000_000L, 2);

        Assert.That(speeds, Has.Length.EqualTo(2));
        Assert.That(speeds[0], Is.GreaterThan(speeds[1]));
    }

    [Test]
    public void Distribute_should_handle_large_torrent_count()
    {
        var speeds = _distributor.Distribute(10_000_000L, 100);

        Assert.That(speeds, Has.Length.EqualTo(100));
        Assert.That(speeds, Is.All.GreaterThanOrEqualTo(0L));
    }

    [Test]
    public void Constructor_should_clamp_alpha_below_minimum()
    {
        var distributor = new PowerLawDistributor(0.1);
        var speeds = distributor.Distribute(1_000_000L, 5);

        Assert.That(speeds, Has.Length.EqualTo(5));
        Assert.That(speeds.Sum(), Is.GreaterThan(0L));
    }

    [Test]
    public void Constructor_should_clamp_alpha_above_maximum()
    {
        var distributor = new PowerLawDistributor(10.0);
        var speeds = distributor.Distribute(1_000_000L, 5);

        Assert.That(speeds, Has.Length.EqualTo(5));
        Assert.That(speeds.Sum(), Is.GreaterThan(0L));
    }

    [Test]
    public void Higher_alpha_should_produce_more_skewed_distribution()
    {
        var lowAlpha = new PowerLawDistributor(0.5);
        var highAlpha = new PowerLawDistributor(3.0);

        var lowSpeeds = lowAlpha.Distribute(1_000_000L, 10);
        var highSpeeds = highAlpha.Distribute(1_000_000L, 10);

        var lowRange = lowSpeeds[0] - lowSpeeds[9];
        var highRange = highSpeeds[0] - highSpeeds[9];

        Assert.That(highRange, Is.GreaterThan(lowRange));
    }

    [Test]
    public void Default_constructor_should_use_alpha_of_1_point_5()
    {
        var defaultDistributor = new PowerLawDistributor();
        var explicitDistributor = new PowerLawDistributor(1.5);

        var defaultSpeeds = defaultDistributor.Distribute(1_000_000L, 5);
        var explicitSpeeds = explicitDistributor.Distribute(1_000_000L, 5);

        Assert.That(defaultSpeeds, Is.EqualTo(explicitSpeeds));
    }
}
