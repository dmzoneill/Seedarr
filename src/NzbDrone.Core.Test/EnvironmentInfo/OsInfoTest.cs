using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Test.EnvironmentInfo;

[TestFixture]
public class OsInfoTest
{
    [Test]
    public void Version_should_return_host_os_version()
    {
        Assert.That(OsInfo.Version, Is.EqualTo(Environment.OSVersion.Version.ToString()));
    }

    [Test]
    public void Version_should_not_be_null_or_empty()
    {
        Assert.That(OsInfo.Version, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Version_should_not_equal_framework_description()
    {
        Assert.That(OsInfo.Version, Is.Not.EqualTo(RuntimeInformation.FrameworkDescription));
    }

    [Test]
    public void Os_should_match_runtime_os_description()
    {
        Assert.That(OsInfo.Os, Is.EqualTo(RuntimeInformation.OSDescription));
    }

    [Test]
    public void Platform_flags_should_match_runtime_information()
    {
        Assert.That(OsInfo.IsWindows, Is.EqualTo(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)));
        Assert.That(OsInfo.IsLinux, Is.EqualTo(RuntimeInformation.IsOSPlatform(OSPlatform.Linux)));
        Assert.That(OsInfo.IsOsx, Is.EqualTo(RuntimeInformation.IsOSPlatform(OSPlatform.OSX)));
    }
}
