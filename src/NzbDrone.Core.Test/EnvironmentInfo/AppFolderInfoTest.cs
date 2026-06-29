using System;
using System.IO;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Test.EnvironmentInfo;

[TestFixture]
public class AppFolderInfoTest
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"seedarr_test_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static bool CanCreateDirectory(string parentPath)
    {
        try
        {
            var probe = Path.Combine(parentPath, $"probe_{Guid.NewGuid():N}");
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Test]
    public void AppDataFolder_uses_data_arg_when_provided()
    {
        var context = new StartupContext($"--data={_tempDir}");
        var subject = new AppFolderInfo(context);

        Assert.That(subject.AppDataFolder, Is.EqualTo(_tempDir));
    }

    [Test]
    public void AppDataFolder_is_created_when_data_arg_is_provided()
    {
        var context = new StartupContext($"--data={_tempDir}");
        var subject = new AppFolderInfo(context);

        Assert.That(Directory.Exists(subject.AppDataFolder), Is.True);
    }

    [Test]
    public void AppDataFolder_uses_default_path_when_no_data_arg()
    {
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        Assume.That(CanCreateDirectory(commonData), "CommonApplicationData is not writable on this system");

        var context = new StartupContext();
        var subject = new AppFolderInfo(context);

        var expected = Path.Combine(commonData, "Seedarr");
        Assert.That(subject.AppDataFolder, Is.EqualTo(expected));

        // Cleanup to avoid leaving test artifacts
        if (Directory.Exists(expected))
        {
            Directory.Delete(expected);
        }
    }

    [Test]
    public void AppDataFolder_ends_with_seedarr_for_default_path()
    {
        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        Assume.That(CanCreateDirectory(commonData), "CommonApplicationData is not writable on this system");

        var context = new StartupContext();
        var subject = new AppFolderInfo(context);

        Assert.That(subject.AppDataFolder, Does.EndWith("Seedarr"));

        var created = subject.AppDataFolder;
        if (Directory.Exists(created))
        {
            Directory.Delete(created);
        }
    }

    [Test]
    public void StartUpFolder_equals_appdomain_base_directory()
    {
        var context = new StartupContext($"--data={_tempDir}");
        var subject = new AppFolderInfo(context);

        Assert.That(subject.StartUpFolder, Is.EqualTo(AppDomain.CurrentDomain.BaseDirectory));
    }

    [Test]
    public void AppDataFolder_is_not_null_or_empty_for_custom_path()
    {
        var context = new StartupContext($"--data={_tempDir}");
        var subject = new AppFolderInfo(context);

        Assert.That(subject.AppDataFolder, Is.Not.Null.And.Not.Empty);
    }
}
