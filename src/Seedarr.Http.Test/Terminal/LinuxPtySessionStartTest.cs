// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.Terminal;

[TestFixture]
public class LinuxPtySessionStartTest
{
    [Test]
    [Platform(Include = "Linux")]
    public async Task Start_spawns_shell_with_valid_argv_and_env()
    {
        var cwd = Path.Combine(Path.GetTempPath(), $"seedarr-pty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);

        var session = LinuxPtySession.Start(cwd, 80, 24);
        Assert.That(session.ProcessId, Is.GreaterThan(0));
        Assert.That(session.IsActive, Is.True);

        var command = Encoding.UTF8.GetBytes("echo seedarr-pty-ok\n");
        await session.WriteAsync(command, CancellationToken.None);

        var buffer = new byte[256];
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var output = new StringBuilder();

        while (DateTime.UtcNow < deadline && !output.ToString().Contains("seedarr-pty-ok", StringComparison.Ordinal))
        {
            var read = await session.ReadAsync(buffer, CancellationToken.None);
            if (read > 0)
            {
                output.Append(Encoding.UTF8.GetString(buffer.AsSpan(0, read)));
            }
            else
            {
                await Task.Delay(50);
            }
        }

        try
        {
            Assert.That(output.ToString(), Does.Contain("seedarr-pty-ok"));
        }
        finally
        {
            await session.DisposeAsync();
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
        }
    }

    [Test]
    [Platform(Include = "Linux")]
    public async Task Start_does_not_pass_through_non_whitelisted_daemon_environment()
    {
        const string leakKey = "MY_DEPLOYMENT_LEAK";
        const string leakValue = "supersecret";
        Environment.SetEnvironmentVariable(leakKey, leakValue);

        var cwd = Path.Combine(Path.GetTempPath(), $"seedarr-pty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);

        var session = LinuxPtySession.Start(cwd, 80, 24);
        Assert.That(session.ProcessId, Is.GreaterThan(0));

        var command = Encoding.UTF8.GetBytes("env\n");
        await session.WriteAsync(command, CancellationToken.None);

        var output = await ReadPtyOutputAsync(session, TimeSpan.FromSeconds(5));

        try
        {
            Assert.That(output, Does.Not.Contain(leakKey, StringComparison.Ordinal));
            Assert.That(output, Does.Not.Contain(leakValue, StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(leakKey, null);
            await session.DisposeAsync();
            if (Directory.Exists(cwd))
            {
                Directory.Delete(cwd, recursive: true);
            }
        }
    }

    private static async Task<string> ReadPtyOutputAsync(LinuxPtySession session, TimeSpan timeout)
    {
        var buffer = new byte[4096];
        var output = new StringBuilder();
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            var read = await session.ReadAsync(buffer, CancellationToken.None);
            if (read > 0)
            {
                output.Append(Encoding.UTF8.GetString(buffer.AsSpan(0, read)));
                if (output.ToString().Contains("PWD=", StringComparison.Ordinal))
                {
                    break;
                }
            }
            else
            {
                await Task.Delay(50);
            }
        }

        return output.ToString();
    }
}
