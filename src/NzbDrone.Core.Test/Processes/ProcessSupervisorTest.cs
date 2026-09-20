using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Processes;

namespace NzbDrone.Core.Test.Processes;

[TestFixture]
public class ProcessSupervisorTest
{
    private static Process StartSleepProcess(int seconds = 30)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "sleep",
            Arguments = isWindows ? $"/c ping 127.0.0.1 -n {seconds} > nul" : $"{seconds}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        return Process.Start(psi);
    }

    [Test]
    public void RegisterProcess_should_track_active_child_process_and_UnregisterProcess_should_remove_it()
    {
        using var supervisor = new ProcessSupervisor();
        using var process = StartSleepProcess(30);

        try
        {
            Assert.That(process, Is.Not.Null);
            var pid = process.Id;

            supervisor.RegisterProcess(process);
            Assert.That(supervisor.ActiveProcessIds, Does.Contain(pid));

            supervisor.UnregisterProcess(pid);
            Assert.That(supervisor.ActiveProcessIds, Does.Not.Contain(pid));
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
    }

    [Test]
    public async Task Process_exited_event_should_automatically_unregister_from_supervisor()
    {
        using var supervisor = new ProcessSupervisor();
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/sh",
            Arguments = isWindows ? "/c exit 0" : "-c 'exit 0'",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);
        var pid = process.Id;

        supervisor.RegisterProcess(process);
        await process.WaitForExitAsync();

        // Allow up to 1 second for the Exited event callback to fire
        var unregistered = false;
        for (var i = 0; i < 20; i++)
        {
            if (!supervisor.ActiveProcessIds.Contains(pid))
            {
                unregistered = true;
                break;
            }

            await Task.Delay(50);
        }

        Assert.That(unregistered, Is.True, "Process should have been automatically unregistered upon exit");
    }

    [Test]
    public async Task ApplicationShutdownRequested_should_terminate_all_active_processes()
    {
        using var supervisor = new ProcessSupervisor();
        using var process1 = StartSleepProcess(30);
        using var process2 = StartSleepProcess(30);

        try
        {
            Assert.That(process1, Is.Not.Null);
            Assert.That(process2, Is.Not.Null);

            supervisor.RegisterProcess(process1);
            supervisor.RegisterProcess(process2);

            Assert.That(supervisor.ActiveProcessIds, Does.Contain(process1.Id));
            Assert.That(supervisor.ActiveProcessIds, Does.Contain(process2.Id));
            Assert.That(process1.HasExited, Is.False);
            Assert.That(process2.HasExited, Is.False);

            supervisor.Handle(new ApplicationShutdownRequested());

            Assert.That(supervisor.IsShuttingDown, Is.True);
            Assert.That(supervisor.ActiveProcessIds, Is.Empty);

            var exited1 = await Task.Run(() => process1.WaitForExit(3000));
            var exited2 = await Task.Run(() => process2.WaitForExit(3000));

            Assert.That(exited1, Is.True, "Process 1 should have been terminated on shutdown");
            Assert.That(exited2, Is.True, "Process 2 should have been terminated on shutdown");
            Assert.That(process1.HasExited, Is.True);
            Assert.That(process2.HasExited, Is.True);
        }
        finally
        {
            try
            {
                if (process1 != null && !process1.HasExited)
                {
                    process1.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            try
            {
                if (process2 != null && !process2.HasExited)
                {
                    process2.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
    }

    [Test]
    public void RegisterProcess_should_ignore_process_when_already_shutting_down()
    {
        using var supervisor = new ProcessSupervisor();
        supervisor.Handle(new ApplicationShutdownRequested());
        Assert.That(supervisor.IsShuttingDown, Is.True);

        using var process = StartSleepProcess(10);
        try
        {
            supervisor.RegisterProcess(process);
            Assert.That(supervisor.ActiveProcessIds, Does.Not.Contain(process.Id));
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
    }

    [Test]
    public void Dispose_should_terminate_all_registered_processes()
    {
        var supervisor = new ProcessSupervisor();
        using var process = StartSleepProcess(30);

        try
        {
            supervisor.RegisterProcess(process);
            Assert.That(supervisor.ActiveProcessIds, Does.Contain(process.Id));

            supervisor.Dispose();

            Assert.That(supervisor.IsShuttingDown, Is.True);
            Assert.That(supervisor.ActiveProcessIds, Is.Empty);
            Assert.That(process.WaitForExit(3000), Is.True);
            Assert.That(process.HasExited, Is.True);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
    }
}
