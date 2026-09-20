using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class MemoryPressureCheckTest
{
    [Test]
    public void Check_returns_Ok_when_memory_usage_below_85_percent()
    {
        var subject = new MemoryPressureCheck(
            () => 800L * 1024L * 1024L,
            () => 1000L * 1024L * 1024L);

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_returns_Ok_at_exact_85_threshold()
    {
        var subject = new MemoryPressureCheck(
            () => 850L * 1024L * 1024L,
            () => 1000L * 1024L * 1024L);

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_returns_Warning_when_memory_usage_above_85_percent()
    {
        var subject = new MemoryPressureCheck(
            () => 860L * 1024L * 1024L,
            () => 1000L * 1024L * 1024L);

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("86%"));
        Assert.That(result.Message, Does.Contain("memory limits"));
    }

    [Test]
    public void Check_returns_Warning_at_exact_95_threshold()
    {
        var subject = new MemoryPressureCheck(
            () => 950L * 1024L * 1024L,
            () => 1000L * 1024L * 1024L);

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
    }

    [Test]
    public void Check_returns_Error_when_memory_usage_above_95_percent()
    {
        var subject = new MemoryPressureCheck(
            () => 960L * 1024L * 1024L,
            () => 1000L * 1024L * 1024L);

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("96%"));
        Assert.That(result.Message, Does.Contain("OOM kill risk"));
    }

    [Test]
    public void Check_returns_Ok_when_limit_is_zero_or_negative()
    {
        var subjectZero = new MemoryPressureCheck(
            () => 500L * 1024L * 1024L,
            () => 0L);

        var subjectNegative = new MemoryPressureCheck(
            () => 500L * 1024L * 1024L,
            () => -1L);

        Assert.That(subjectZero.Check().Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(subjectNegative.Check().Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_with_default_constructor_executes_successfully()
    {
        var subject = new MemoryPressureCheck();
        var result = subject.Check();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Source, Is.EqualTo(nameof(MemoryPressureCheck)));
    }
}
