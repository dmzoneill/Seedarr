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

    [Test]
    public void Apply_with_active_schedule_and_higher_scheduled_speed_preserves_scheduled_boost()
    {
        var limits = new SpeedLimits
        {
            IsScheduleActive = true,
            MaxUploadSpeed = 20_000_000,
            MaxDownloadSpeed = 50_000_000
        };

        var result = SpeedLimitMerger.Apply(limits, 5_000_000, 10_000_000, false);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(20_000_000));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(50_000_000));
    }

    [Test]
    public void Apply_with_active_schedule_and_alternative_speed_enabled_clamps_to_alt_limit()
    {
        var limits = new SpeedLimits
        {
            IsScheduleActive = true,
            MaxUploadSpeed = 20_000_000,
            MaxDownloadSpeed = 50_000_000
        };

        var result = SpeedLimitMerger.Apply(limits, 50_000, 100_000, true);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(50_000));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(100_000));
    }

    [Test]
    public void Apply_with_active_schedule_and_unlimited_scheduled_speed_falls_back_to_config()
    {
        var limits = new SpeedLimits
        {
            IsScheduleActive = true,
            MaxUploadSpeed = SpeedLimits.Unlimited,
            MaxDownloadSpeed = SpeedLimits.Unlimited
        };

        var result = SpeedLimitMerger.Apply(limits, 5_000_000, 10_000_000, false);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(5_000_000));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(10_000_000));
    }

    [Test]
    public void Apply_with_active_schedule_and_zero_scheduled_speed_keeps_zero_pause()
    {
        var limits = new SpeedLimits
        {
            IsScheduleActive = true,
            MaxUploadSpeed = 0,
            MaxDownloadSpeed = 0
        };

        var result = SpeedLimitMerger.Apply(limits, 5_000_000, 10_000_000, false);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(0));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(0));
    }

    [Test]
    public void Apply_with_inactive_schedule_uses_config_limits()
    {
        var limits = new SpeedLimits
        {
            IsScheduleActive = false,
            MaxUploadSpeed = SpeedLimits.Unlimited,
            MaxDownloadSpeed = SpeedLimits.Unlimited
        };

        var result = SpeedLimitMerger.Apply(limits, 5_000_000, 10_000_000, false);

        Assert.That(result.MaxUploadSpeed, Is.EqualTo(5_000_000));
        Assert.That(result.MaxDownloadSpeed, Is.EqualTo(10_000_000));
    }
}
