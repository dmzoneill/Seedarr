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
}
