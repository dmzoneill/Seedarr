using NUnit.Framework;
using NzbDrone.Core.Seeding.Scheduling;

namespace NzbDrone.Core.Test.Seeding.Scheduling;

[TestFixture]
public class SpeedLimitMergerTest
{
    [Test]
    public void Apply_with_null_limits_returns_null()
    {
        var result = SpeedLimitMerger.Apply(null, 1024, 1024);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Apply_with_unlimited_limits_and_positive_speeds_sets_speeds()
    {
        var limits = new SpeedLimits
        {
            MaxUploadSpeed = SpeedLimits.Unlimited,
            MaxDownloadSpeed = SpeedLimits.Unlimited
        };

        var result = SpeedLimitMerger.Apply(limits, 500_000, 1_000_000);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(500_000));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(1_000_000));
    }

    [Test]
    public void Apply_with_unlimited_limits_and_zero_speeds_sets_zero_pause()
    {
        var limits = new SpeedLimits
        {
            MaxUploadSpeed = SpeedLimits.Unlimited,
            MaxDownloadSpeed = SpeedLimits.Unlimited
        };

        var result = SpeedLimitMerger.Apply(limits, 0, 0);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(0));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(0));
    }

    [Test]
    public void Apply_with_unlimited_limits_and_minus_one_speeds_remains_unlimited()
    {
        var limits = new SpeedLimits
        {
            MaxUploadSpeed = SpeedLimits.Unlimited,
            MaxDownloadSpeed = SpeedLimits.Unlimited
        };

        var result = SpeedLimitMerger.Apply(limits, -1, -1);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(SpeedLimits.Unlimited));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(SpeedLimits.Unlimited));
    }

    [Test]
    public void Apply_with_scheduled_limits_and_higher_config_keeps_lower_scheduled()
    {
        var limits = new SpeedLimits
        {
            MaxUploadSpeed = 200_000,
            MaxDownloadSpeed = 400_000
        };

        var result = SpeedLimitMerger.Apply(limits, 500_000, 800_000);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(200_000));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(400_000));
    }

    [Test]
    public void Apply_with_scheduled_limits_and_lower_config_uses_lower_config()
    {
        var limits = new SpeedLimits
        {
            MaxUploadSpeed = 500_000,
            MaxDownloadSpeed = 800_000
        };

        var result = SpeedLimitMerger.Apply(limits, 200_000, 400_000);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(200_000));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(400_000));
    }

    [Test]
    public void Apply_with_scheduled_zero_pause_and_positive_config_keeps_zero_pause()
    {
        var limits = new SpeedLimits
        {
            MaxUploadSpeed = 0,
            MaxDownloadSpeed = 0
        };

        var result = SpeedLimitMerger.Apply(limits, 500_000, 800_000);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(0));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(0));
    }

    [Test]
    public void Apply_with_positive_scheduled_and_zero_config_uses_zero_pause()
    {
        var limits = new SpeedLimits
        {
            MaxUploadSpeed = 500_000,
            MaxDownloadSpeed = 800_000
        };

        var result = SpeedLimitMerger.Apply(limits, 0, 0);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(0));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(0));
    }
}
