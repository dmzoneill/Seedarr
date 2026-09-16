using System;
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

    [Test]
    public void TryParseVersionString_should_parse_semver_with_prerelease_suffix()
    {
        Assert.That(BuildInfo.TryParseVersionString("1.2.3-beta.1", out var v1), Is.True);
        Assert.That(v1, Is.EqualTo(new Version(1, 2, 3)));

        Assert.That(BuildInfo.TryParseVersionString("2.0.0-rc.2", out var v2), Is.True);
        Assert.That(v2, Is.EqualTo(new Version(2, 0, 0)));

        Assert.That(BuildInfo.TryParseVersionString("v1.5.0-rc1", out var v3), Is.True);
        Assert.That(v3, Is.EqualTo(new Version(1, 5, 0)));
    }

    [Test]
    public void TryParseVersionString_should_parse_semver_with_build_metadata()
    {
        Assert.That(BuildInfo.TryParseVersionString("1.0.0+build.123", out var v1), Is.True);
        Assert.That(v1, Is.EqualTo(new Version(1, 0, 0)));

        Assert.That(BuildInfo.TryParseVersionString("1.0.0-rc.1+build.123", out var v2), Is.True);
        Assert.That(v2, Is.EqualTo(new Version(1, 0, 0)));
    }

    [Test]
    public void TryParseVersionString_should_parse_plain_versions()
    {
        Assert.That(BuildInfo.TryParseVersionString("1.2.3", out var v1), Is.True);
        Assert.That(v1, Is.EqualTo(new Version(1, 2, 3)));

        Assert.That(BuildInfo.TryParseVersionString("1.2.3.4", out var v2), Is.True);
        Assert.That(v2, Is.EqualTo(new Version(1, 2, 3, 4)));
    }

    [Test]
    public void TryParseVersionString_should_return_false_for_invalid_input()
    {
        Assert.That(BuildInfo.TryParseVersionString(null, out _), Is.False);
        Assert.That(BuildInfo.TryParseVersionString("", out _), Is.False);
        Assert.That(BuildInfo.TryParseVersionString("   ", out _), Is.False);
        Assert.That(BuildInfo.TryParseVersionString("not-a-version", out _), Is.False);
    }
}
