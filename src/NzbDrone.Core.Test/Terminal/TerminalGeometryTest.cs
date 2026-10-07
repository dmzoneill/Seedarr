// Copyright (c) FeedItOut. All rights reserved.

using NUnit.Framework;
using NzbDrone.Core.Terminal;

namespace NzbDrone.Core.Test.Terminal;

[TestFixture]
public class TerminalGeometryTest
{
    [Test]
    public void Clamp_enforces_terminal_bounds()
    {
        Assert.That(TerminalGeometry.Clamp(0, -1), Is.EqualTo((10, 5)));
        Assert.That(TerminalGeometry.Clamp(80, 24), Is.EqualTo((80, 24)));
        Assert.That(TerminalGeometry.Clamp(999, 999), Is.EqualTo((500, 200)));
        Assert.That(TerminalGeometry.Clamp(int.MaxValue, int.MaxValue), Is.EqualTo((500, 200)));
    }

    [Test]
    public void NormalizeSignalRDimensions_applies_defaults_then_clamps()
    {
        Assert.That(TerminalGeometry.NormalizeSignalRDimensions(0, -10), Is.EqualTo((80, 24)));
        Assert.That(TerminalGeometry.NormalizeSignalRDimensions(int.MaxValue, int.MaxValue), Is.EqualTo((500, 200)));
    }
}
