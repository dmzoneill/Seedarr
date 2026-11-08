using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Core.Terminal;

namespace NzbDrone.Core.Test.Terminal;

[TestFixture]
public class WindowsPtyProcessTest
{
    [Test]
    public void Dispose_releases_fallback_process_and_master_stream()
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "sleep",
            Arguments = OperatingSystem.IsWindows() ? "/c ping 127.0.0.1 -n 30 >NUL" : "30",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);

        var ctor = typeof(WindowsPtyProcess).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(Process) },
            null);
        Assert.That(ctor, Is.Not.Null);

        var pty = (WindowsPtyProcess)ctor.Invoke(new object[] { process });
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var processField = typeof(WindowsPtyProcess).GetField("_fallbackProcess", flags);
        var streamField = typeof(WindowsPtyProcess).GetField("_masterStream", flags);
        Assert.That(processField.GetValue(pty), Is.SameAs(process));
        Assert.That(streamField.GetValue(pty), Is.Not.Null);

        var disposeBool = typeof(WindowsPtyProcess).GetMethod("Dispose", flags, null, new[] { typeof(bool) }, null);
        disposeBool.Invoke(pty, new object[] { false });
        Assert.That(processField.GetValue(pty), Is.SameAs(process));

        pty.Dispose();
        Assert.DoesNotThrow(() => pty.Dispose());

        Assert.Throws<ObjectDisposedException>(() =>
        {
            var unused = process.HasExited;
        });
    }
}
