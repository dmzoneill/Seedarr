// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.Terminal;

[TestFixture]
public class FallbackProcessSessionBackpressureTest
{
    [Test]
    [Timeout(15000)]
    public async Task Slow_consumer_does_not_block_child_when_stderr_floods()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Fallback shell flood test targets Unix redirect pipes");
        }

        var cwd = Environment.GetEnvironmentVariable("HOME") ?? "/tmp";
        var session = FallbackProcessSession.Start(cwd, 80, 24);
        try
        {
            Assert.That(session.IsActive, Is.True);

            var flood = Encoding.UTF8.GetBytes("while true; do echo flood >&2; done &\n");
            await session.WriteAsync(flood, CancellationToken.None);

            await Task.Delay(1500);

            var probe = Encoding.UTF8.GetBytes("echo SEEDARR_ALIVE\n");
            await session.WriteAsync(probe, CancellationToken.None);

            var readBuffer = new byte[4096];
            var deadline = DateTime.UtcNow.AddSeconds(8);
            var found = false;
            while (DateTime.UtcNow < deadline && !found)
            {
                var n = await session.ReadAsync(readBuffer, CancellationToken.None);
                if (n <= 0)
                {
                    await Task.Delay(50);
                    continue;
                }

                var text = Encoding.UTF8.GetString(readBuffer.AsSpan(0, n));
                if (text.Contains("SEEDARR_ALIVE", StringComparison.Ordinal))
                {
                    found = true;
                }
            }

            Assert.That(found, Is.True, "Child shell should stay responsive when output channel is not drained");
        }
        finally
        {
            session.Kill();
        }
    }
}
