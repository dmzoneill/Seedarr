using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
    public void CreateStartInfo_rejects_a_blank_process_path()
    {
        Assert.Throws<ArgumentException>(() => ProcessRestart.CreateStartInfo(" ", null, null, 1));
    }

    [Test]
    public void CreateStartInfo_uses_the_supplied_entry_assembly_when_argv_has_no_dll()
    {
        var startInfo = ProcessRestart.CreateStartInfo(
            "/usr/bin/dotnet",
            new[] { "/usr/bin/dotnet", "-nobrowser", "-data=/tmp/seedarr" },
            null,
            5,
            "/app/Seedarr.Console.dll");

        Assert.That(startInfo.WorkingDirectory, Is.EqualTo(Environment.CurrentDirectory));
        Assert.That(startInfo.ArgumentList, Is.EqualTo(new[]
        {
            "/app/Seedarr.Console.dll",
            "-nobrowser",
            "-data=/tmp/seedarr",
            "--restart-wait-pid=5"
        }));
    }

    [Test]
    public void CreateStartInfo_rejects_dotnet_without_an_entry_assembly()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProcessRestart.CreateStartInfo("/usr/bin/dotnet.exe", new[] { "-data=/tmp/seedarr" }, "/app", 1));
    }

    [Test]
    public void CreateStartInfo_keeps_arguments_that_are_not_the_executable()
    {
        var startInfo = ProcessRestart.CreateStartInfo(
            "/opt/Seedarr.Console",
            new[] { " ", "-data=/x", "", "--restart-wait-pid", "/opt/./Seedarr.Console" },
            "/opt",
            8);

        Assert.That(startInfo.ArgumentList, Does.Contain(" "));
        Assert.That(startInfo.ArgumentList, Does.Contain("-data=/x"));
        Assert.That(startInfo.ArgumentList, Does.Contain(""));
        Assert.That(startInfo.ArgumentList, Does.Not.Contain("--restart-wait-pid"));
        Assert.That(startInfo.ArgumentList, Does.Contain("/opt/./Seedarr.Console"));
    }

    [Test]
    public void CreateStartInfo_skips_argv0_when_it_is_the_same_executable()
    {
        var startInfo = ProcessRestart.CreateStartInfo(
            "/opt/Seedarr.Console",
            new[] { "/opt/./Seedarr.Console", "-data=/x" },
            "/opt",
            8);

        Assert.That(startInfo.ArgumentList[0], Is.EqualTo("-data=/x"));
    }

    [Test]
    public void CreateStartInfo_treats_an_unreadable_path_as_a_different_executable()
    {
        var startInfo = ProcessRestart.CreateStartInfo(
            "/opt/Seedarr.Console",
            new[] { "bad\0path", "-data=/x" },
            "/opt",
            0);

        Assert.That(startInfo.ArgumentList[0], Is.EqualTo("bad\0path"));
        Assert.That(startInfo.ArgumentList, Does.Not.Contain("--restart-wait-pid=0"));
    }

    [Test]
    public void WaitForPreviousProcess_ignores_the_current_process_and_non_positive_pids()
    {
        Assert.DoesNotThrow(() => ProcessRestart.WaitForPreviousProcess(0, TimeSpan.FromMilliseconds(1)));
        Assert.DoesNotThrow(() => ProcessRestart.WaitForPreviousProcess(Environment.ProcessId, TimeSpan.FromMilliseconds(1)));
    }

    [Test]
    public void WaitForPreviousProcess_times_out_when_the_process_is_still_running()
    {
        using var process = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false });
        try
        {
            Assert.Throws<TimeoutException>(() =>
                ProcessRestart.WaitForPreviousProcess(process.Id, TimeSpan.FromMilliseconds(200)));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit();
            }
        }
    }

    [Test]
    public void PrepareForStart_waits_for_a_missing_pid_and_locks_the_data_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "seedarr-start-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new StartupContext("--data=" + dir, "--restart-wait-pid=" + int.MaxValue);
            var folder = ProcessRestart.PrepareForStart(context);
            Assert.That(folder, Is.EqualTo(dir));
            Assert.That(File.Exists(Path.Combine(dir, ProcessRestart.LockFileName)), Is.True);
        }
        finally
        {
            ProcessRestart.ReleaseDataDirectoryLock(dir);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Test]
    public void Data_directory_lock_retries_until_the_previous_holder_releases()
    {
        var dir = Path.Combine(Path.GetTempPath(), "seedarr-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, ProcessRestart.LockFileName);
        var blocker = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(120);
            blocker.Dispose();
        });

        try
        {
            Assert.DoesNotThrow(() => ProcessRestart.AcquireDataDirectoryLock(dir, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            release.GetAwaiter().GetResult();
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

    [Test]
    public void Data_directory_lock_rejects_a_blank_folder_and_release_ignores_blank_input()
    {
        Assert.Throws<ArgumentException>(() => ProcessRestart.AcquireDataDirectoryLock("  ", TimeSpan.Zero));
        Assert.DoesNotThrow(() => ProcessRestart.ReleaseDataDirectoryLock(null));
        Assert.DoesNotThrow(() => ProcessRestart.ReleaseDataDirectoryLock("   "));
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
