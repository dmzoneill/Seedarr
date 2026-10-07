using System;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Notifications;

namespace NzbDrone.Core.Test.Notifications;

[TestFixture]
public class CustomScriptPathPolicyTest
{
    [Test]
    public void IsInAllowedDirectory_should_include_configured_CustomScriptsDirectory()
    {
        var customDir = Path.Combine(Path.GetTempPath(), "seedarr-policy-test-1072");
        var scriptPath = Path.GetFullPath(Path.Combine(customDir, "hook.sh"));

        Assert.That(
            CustomScriptPathPolicy.IsInAllowedDirectory(scriptPath, customDir),
            Is.True,
            "Configured CustomScriptsDirectory must be honored for notification and custom-script APIs.");
    }

    [Test]
    public void IsInAllowedDirectory_should_include_scripts_root_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix /scripts root is not in the Windows allowlist.");
        }

        var scriptPath = Path.GetFullPath("/scripts/on_grab.sh");
        Assert.That(CustomScriptPathPolicy.IsInAllowedDirectory(scriptPath), Is.True);
    }

    [Test]
    public void ValidateAbsoluteScriptPath_should_return_null_for_path_under_custom_directory()
    {
        var customDir = Path.Combine(Path.GetTempPath(), "seedarr-validate-1072");
        var scriptPath = Path.Combine(customDir, "notify.sh");
        var error = CustomScriptPathPolicy.ValidateAbsoluteScriptPath(scriptPath, customDir);
        Assert.That(error, Is.Null);
    }
}
