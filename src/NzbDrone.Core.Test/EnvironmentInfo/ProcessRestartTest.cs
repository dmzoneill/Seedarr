using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Test.EnvironmentInfo;

[TestFixture]
public class ProcessRestartTest
{
    [Test]
    public void CreateStartInfo_keeps_data_and_nobrowser_for_an_apphost()
    {
        var startInfo = ProcessRestart.CreateStartInfo(
            "/opt/Seedarr.Console",
            new[] { "/opt/Seedarr.Console", "-nobrowser", "-data=/workspace/seedarr-scratch3" },
            "/opt",
            1470277);

        Assert.That(startInfo.FileName, Is.EqualTo("/opt/Seedarr.Console"));
        Assert.That(startInfo.UseShellExecute, Is.False);
        Assert.That(startInfo.ArgumentList, Is.EqualTo(new[]
        {
            "-nobrowser",
            "-data=/workspace/seedarr-scratch3",
            "--restart-wait-pid=1470277"
        }));
    }

    [Test]
    public void CreateStartInfo_includes_the_entry_assembly_when_the_host_is_dotnet()
    {
        var startInfo = ProcessRestart.CreateStartInfo(
            "/usr/bin/dotnet",
            new[] { "/app/Seedarr.Console.dll", "-nobrowser", "-data=/tmp/seedarr", "--restart-wait-pid=9" },
            "/app",
            42);

        Assert.That(startInfo.FileName, Is.EqualTo("/usr/bin/dotnet"));
        Assert.That(startInfo.ArgumentList, Is.EqualTo(new[]
        {
            "/app/Seedarr.Console.dll",
            "-nobrowser",
            "-data=/tmp/seedarr",
            "--restart-wait-pid=42"
        }));
    }

    [Test]
    public void CreateStartInfo_uses_the_dll_after_a_dotnet_argv0()
    {
        var startInfo = ProcessRestart.CreateStartInfo(
            "/usr/bin/dotnet",
            new List<string> { "/usr/bin/dotnet", "/app/Seedarr.Console.dll", "-data=/data" },
            "/app",
            7,
            "/ignored/because/argv/has/the/dll.dll");

        Assert.That(startInfo.ArgumentList[0], Is.EqualTo("/app/Seedarr.Console.dll"));
        Assert.That(startInfo.ArgumentList, Does.Contain("-data=/data"));
        Assert.That(startInfo.ArgumentList, Does.Contain("--restart-wait-pid=7"));
    }

    [Test]
    public void WaitForPreviousProcess_returns_when_the_pid_is_already_gone()
    {
        Assert.DoesNotThrow(() => ProcessRestart.WaitForPreviousProcess(int.MaxValue, TimeSpan.FromMilliseconds(50)));
    }

    [Test]
    public void Data_directory_lock_rejects_a_second_holder_and_can_be_released()
    {
        var dir = Path.Combine(Path.GetTempPath(), "seedarr-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, ProcessRestart.LockFileName);
        try
        {
            using (var blocker = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(blocker.CanWrite, Is.True);
                var ex = Assert.Throws<InvalidOperationException>(() => ProcessRestart.AcquireDataDirectoryLock(dir, TimeSpan.Zero));
                Assert.That(ex.Message, Does.Contain(Path.GetFullPath(dir)));
            }

            ProcessRestart.AcquireDataDirectoryLock(dir, TimeSpan.Zero);
            ProcessRestart.ReleaseDataDirectoryLock(dir);

            using var after = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Assert.That(after.CanWrite, Is.True);
        }
        finally
        {
            ProcessRestart.ReleaseDataDirectoryLock(dir);
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
