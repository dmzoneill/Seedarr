using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Test.EnvironmentInfo;

[TestFixture]
public class BuildInfoTest
{
    [Test]
    public void AppName_should_return_seedarr()
    {
        Assert.That(BuildInfo.AppName, Is.EqualTo("Seedarr"));
    }

    [Test]
    public void Branch_should_return_main()
    {
        Assert.That(BuildInfo.Branch, Is.EqualTo("main"));
    }

    [Test]
    public void Version_should_not_be_null()
    {
        Assert.That(BuildInfo.Version, Is.Not.Null);
    }

    [Test]
    public void Version_should_have_positive_major_or_minor()
    {
        Assert.That(BuildInfo.Version.Major, Is.GreaterThanOrEqualTo(0));
    }
}
