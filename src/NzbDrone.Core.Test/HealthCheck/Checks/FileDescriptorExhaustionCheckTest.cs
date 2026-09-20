using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Instrumentation;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class FileDescriptorExhaustionCheckTest
{
    private IFileDescriptorProvider _provider;
    private FileDescriptorExhaustionCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _provider = Substitute.For<IFileDescriptorProvider>();
        _subject = new FileDescriptorExhaustionCheck(_provider);
    }

    [Test]
    public void Check_returns_Ok_when_usage_percentage_is_null()
    {
        _provider.GetFileDescriptorUsagePercentage().Returns((double?)null);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_returns_Ok_when_usage_below_80_percent()
    {
        _provider.GetFileDescriptorUsagePercentage().Returns(79.9);
        _provider.GetOpenFileDescriptorCount().Returns(818);
        _provider.GetMaxFileDescriptors().Returns(1024);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_returns_Warning_when_usage_between_80_and_90_percent()
    {
        _provider.GetFileDescriptorUsagePercentage().Returns(82.5);
        _provider.GetOpenFileDescriptorCount().Returns(845);
        _provider.GetMaxFileDescriptors().Returns(1024);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("82.5%"));
        Assert.That(result.Message, Does.Contain("845/1024"));
        Assert.That(result.Message, Does.Contain("nofile"));
    }

    [Test]
    public void Check_returns_Error_when_usage_at_or_above_90_percent()
    {
        _provider.GetFileDescriptorUsagePercentage().Returns(92.0);
        _provider.GetOpenFileDescriptorCount().Returns(942);
        _provider.GetMaxFileDescriptors().Returns(1024);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("92%"));
        Assert.That(result.Message, Does.Contain("942/1024"));
        Assert.That(result.Message, Does.Contain("EMFILE"));
        Assert.That(result.Message, Does.Contain("ulimits"));
    }

    [Test]
    public void Check_returns_Warning_at_exact_80_threshold()
    {
        _provider.GetFileDescriptorUsagePercentage().Returns(80.0);
        _provider.GetOpenFileDescriptorCount().Returns(800);
        _provider.GetMaxFileDescriptors().Returns(1000);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
    }

    [Test]
    public void Check_returns_Error_at_exact_90_threshold()
    {
        _provider.GetFileDescriptorUsagePercentage().Returns(90.0);
        _provider.GetOpenFileDescriptorCount().Returns(900);
        _provider.GetMaxFileDescriptors().Returns(1000);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
    }
}
