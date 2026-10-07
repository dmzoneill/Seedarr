// Copyright (c) FeedItOut. All rights reserved.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.Terminal;

[TestFixture]
public class FallbackTerminalGeometryTest
{
    [Test]
    public void Clamp_matches_pty_terminal_service_bounds()
    {
        Assert.That(FallbackTerminalGeometry.Clamp(0, -1), Is.EqualTo((10, 5)));
        Assert.That(FallbackTerminalGeometry.Clamp(80, 24), Is.EqualTo((80, 24)));
        Assert.That(FallbackTerminalGeometry.Clamp(999, 999), Is.EqualTo((500, 200)));
    }

    [Test]
    public void ApplyToEnvironment_sets_lines_and_columns()
    {
        var startInfo = new ProcessStartInfo("/bin/sh");
        FallbackTerminalGeometry.ApplyToEnvironment(startInfo, 120, 40);

        Assert.That(startInfo.Environment["LINES"], Is.EqualTo("40"));
        Assert.That(startInfo.Environment["COLUMNS"], Is.EqualTo("120"));
    }

    [Test]
    public void BuildResizePayload_includes_clamped_dimensions()
    {
        var payload = Encoding.UTF8.GetString(FallbackTerminalGeometry.BuildResizePayload(1, 1));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.That(payload, Does.Contain("$env:LINES=5"));
            Assert.That(payload, Does.Contain("$env:COLUMNS=10"));
        }
        else
        {
            Assert.That(payload, Does.Contain("export LINES=5 COLUMNS=10"));
            Assert.That(payload, Does.Contain("stty rows 5 cols 10"));
        }
    }
}
