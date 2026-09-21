using System;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Mcp;
using NzbDrone.Core.Plugins;

namespace NzbDrone.Core.Test.Plugins;

[TestFixture]
public class SidecarProcessHostTest
{
    private string _tempDir;
    private PluginManifest _manifest;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_sidecar_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _manifest = new PluginManifest
        {
            Id = "test-sidecar",
            Name = "Test Sidecar Plugin",
            Version = "1.0.0",
            Entrypoint = "plugin.sh",
            Type = "sidecar"
        };
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void RecordCrash_should_apply_exponential_backoff_and_disable_after_5_crashes_in_60s()
    {
        using var host = new SidecarProcessHost(_manifest, _tempDir);
        host.PluginInfo.Enabled = true;

        var now = DateTime.UtcNow;

        // Crash 1: 1s backoff (2^0 = 1s)
        var backoff1 = host.RecordCrash(now);
        Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(1));
        Assert.That(host.PluginInfo.State, Is.EqualTo(PluginState.Crashed));
        Assert.That(backoff1, Is.EqualTo(TimeSpan.FromSeconds(1)));

        // Crash 2: 2s backoff (2^1 = 2s)
        var backoff2 = host.RecordCrash(now.AddSeconds(2));
        Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(2));
        Assert.That(host.PluginInfo.State, Is.EqualTo(PluginState.Crashed));
        Assert.That(backoff2, Is.EqualTo(TimeSpan.FromSeconds(2)));

        // Crash 3: 4s backoff (2^2 = 4s)
        var backoff3 = host.RecordCrash(now.AddSeconds(5));
        Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(3));
        Assert.That(backoff3, Is.EqualTo(TimeSpan.FromSeconds(4)));

        // Crash 4: 8s backoff (2^3 = 8s)
        var backoff4 = host.RecordCrash(now.AddSeconds(10));
        Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(4));
        Assert.That(backoff4, Is.EqualTo(TimeSpan.FromSeconds(8)));

        // Crash 5: Exceeds threshold! State becomes Errored, Enabled becomes false, backoff 0
        var backoff5 = host.RecordCrash(now.AddSeconds(15));
        Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(5));
        Assert.That(host.PluginInfo.State, Is.EqualTo(PluginState.Errored));
        Assert.That(host.PluginInfo.Enabled, Is.False);
        Assert.That(backoff5, Is.EqualTo(TimeSpan.Zero));
        Assert.That(host.PluginInfo.LastError, Does.Contain("exceeded maximum crash threshold"));
    }

    [Test]
    public void RecordCrash_should_purge_crashes_older_than_60_seconds()
    {
        using var host = new SidecarProcessHost(_manifest, _tempDir);
        host.PluginInfo.Enabled = true;

        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        host.RecordCrash(t0);
        host.RecordCrash(t0.AddSeconds(10));
        host.RecordCrash(t0.AddSeconds(20));
        Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(3));

        // 65 seconds after t0: first crash should be pruned
        var backoff = host.RecordCrash(t0.AddSeconds(65));
        Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(3)); // t0 pruned, leaving +10, +20, and +65
        Assert.That(host.PluginInfo.State, Is.EqualTo(PluginState.Crashed));
        Assert.That(backoff, Is.EqualTo(TimeSpan.FromSeconds(4)));
    }

    [Test]
    public void ProcessStdoutLine_should_raise_NotificationReceived_for_notifications()
    {
        using var host = new SidecarProcessHost(_manifest, _tempDir);

        string receivedMethod = null;
        string receivedPayload = null;
        host.NotificationReceived += (sender, args) =>
        {
            receivedMethod = args.Method;
            receivedPayload = args.RawMessage;
        };

        var notificationJson = "{\"jsonrpc\":\"2.0\",\"method\":\"indexer/sync_started\",\"params\":{\"count\":42}}";
        host.ProcessStdoutLine(notificationJson);

        Assert.That(receivedMethod, Is.EqualTo("indexer/sync_started"));
        Assert.That(receivedPayload, Is.EqualTo(notificationJson));
    }

    [Test]
    public void Kill_should_transition_state_to_Stopped()
    {
        using var host = new SidecarProcessHost(_manifest, _tempDir);
        host.Kill();

        Assert.That(host.PluginInfo.State, Is.EqualTo(PluginState.Stopped));
        Assert.That(host.PluginInfo.ProcessId, Is.Null);
    }
}
