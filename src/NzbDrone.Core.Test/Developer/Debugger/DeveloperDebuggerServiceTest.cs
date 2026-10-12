using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Developer.Debugger;

namespace NzbDrone.Core.Test.Developer.Debugger;

[TestFixture]
public class DeveloperDebuggerServiceTest
{
    private DeveloperDebuggerService _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new DeveloperDebuggerService();
    }

    [Test]
    public void AddTracepoint_null_throws()
    {
        Assert.That(() => _subject.AddTracepoint(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void AddTracepoint_assigns_id_and_created_timestamp()
    {
        var tracepoint = new TracepointDefinition { FilePath = "a.cs", LineNumber = 10 };
        var added = _subject.AddTracepoint(tracepoint);

        Assert.That(added.Id, Is.Not.Empty);
        Assert.That(added.CreatedAtUtc, Is.Not.EqualTo(default(DateTime)));
        Assert.That(_subject.GetTracepoints(), Has.Count.EqualTo(1));
    }

    [Test]
    public void RemoveTracepoint_blank_id_returns_false()
    {
        Assert.That(_subject.RemoveTracepoint("  "), Is.False);
    }

    [Test]
    public void RecordSnapshot_increments_tracepoint_hit_count()
    {
        var tracepoint = _subject.AddTracepoint(new TracepointDefinition { Id = "tp1", IsEnabled = true });
        _subject.RecordSnapshot(new TracepointSnapshot { TracepointId = tracepoint.Id });

        var stored = _subject.GetTracepoints()[0];
        Assert.That(stored.HitCount, Is.EqualTo(1));
        Assert.That(_subject.GetSnapshots(), Has.Count.EqualTo(1));
    }

    [Test]
    public void CaptureSnapshot_disabled_tracepoint_returns_null()
    {
        var tracepoint = _subject.AddTracepoint(new TracepointDefinition { Id = "off", IsEnabled = false });
        var snapshot = _subject.CaptureSnapshot(tracepoint.Id, new { x = 1 }, "file.cs", 5);

        Assert.That(snapshot, Is.Null);
    }

    [Test]
    public void CaptureSnapshot_with_condition_filters_on_expression()
    {
        var tracepoint = _subject.AddTracepoint(new TracepointDefinition
        {
            Id = "cond",
            Condition = "x > 10",
            IsEnabled = true,
        });

        Assert.That(
            _subject.CaptureSnapshot(tracepoint.Id, new Dictionary<string, object> { ["x"] = 1 }, "f.cs", 1),
            Is.Null);
        Assert.That(
            _subject.CaptureSnapshot(tracepoint.Id, new Dictionary<string, object> { ["x"] = 20 }, "f.cs", 2),
            Is.Not.Null);
    }

    [Test]
    public void RegisterSession_updates_status_count()
    {
        _subject.RegisterSession("s1");
        _subject.RegisterSession("s2");
        Assert.That(_subject.GetStatus().AttachedSessionCount, Is.EqualTo(2));

        _subject.UnregisterSession("s1");
        Assert.That(_subject.GetStatus().AttachedSessionCount, Is.EqualTo(1));
    }

    [Test]
    public void GetSnapshots_respects_limit()
    {
        for (var i = 0; i < 5; i++)
        {
            _subject.RecordSnapshot(new TracepointSnapshot());
        }

        Assert.That(_subject.GetSnapshots(2), Has.Count.EqualTo(2));
    }

    [Test]
    public void ClearTracepoints_and_snapshots()
    {
        _subject.AddTracepoint(new TracepointDefinition());
        _subject.RecordSnapshot(new TracepointSnapshot());
        _subject.ClearTracepoints();
        _subject.ClearSnapshots();

        Assert.That(_subject.GetTracepoints(), Is.Empty);
        Assert.That(_subject.GetSnapshots(), Is.Empty);
    }

    [Test]
    public void GetSourceCode_rejects_path_traversal()
    {
        var response = _subject.GetSourceCode("../etc/passwd");

        Assert.That(response.Exists, Is.False);
        Assert.That(response.Content, Does.Contain("forbidden"));
    }

    [Test]
    public void GetKnownSourceFiles_includes_reflected_types()
    {
        var files = _subject.GetKnownSourceFiles();

        Assert.That(files, Is.Not.Empty);
        Assert.That(files.Any(f => f.FilePath.EndsWith("DeveloperDebuggerService.cs", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public void GetSourceCode_for_known_type_returns_metadata_when_file_missing()
    {
        var response = _subject.GetSourceCode("src/NzbDrone.Core/Developer/Debugger/DeveloperDebuggerService.cs");

        Assert.That(response.Exists, Is.True);
        Assert.That(response.Content, Is.Not.Empty);
    }

    [Test]
    public void RemoveTracepoint_existing_id_returns_true()
    {
        var tp = _subject.AddTracepoint(new TracepointDefinition { Id = "remove-me" });
        Assert.That(_subject.RemoveTracepoint(tp.Id), Is.True);
        Assert.That(_subject.GetTracepoints(), Is.Empty);
    }

    [Test]
    public void GetSourceCode_empty_path_returns_not_found_message()
    {
        var response = _subject.GetSourceCode("  ");
        Assert.That(response.Exists, Is.False);
        Assert.That(response.Content, Does.Contain("No file path"));
    }

    [Test]
    public void CaptureSnapshot_invalid_condition_defaults_to_capture()
    {
        var tracepoint = _subject.AddTracepoint(new TracepointDefinition
        {
            Id = "bad-cond",
            Condition = "this is not valid js",
            IsEnabled = true,
        });

        var snapshot = _subject.CaptureSnapshot(tracepoint.Id, null, "f.cs", 3);
        Assert.That(snapshot, Is.Not.Null);
    }

    [Test]
    public void RecordSnapshot_null_is_ignored()
    {
        _subject.RecordSnapshot(null);
        Assert.That(_subject.GetSnapshots(), Is.Empty);
    }

    [Test]
    public void RecordSnapshot_trims_to_max_snapshots()
    {
        for (var i = 0; i < 505; i++)
        {
            _subject.RecordSnapshot(new TracepointSnapshot { FilePath = $"f{i}.cs" });
        }

        Assert.That(_subject.GetSnapshots(600), Has.Count.EqualTo(500));
        Assert.That(_subject.GetSnapshots(1)[0].FilePath, Is.EqualTo("f504.cs"));
    }

    [Test]
    public void CaptureSnapshot_serializes_string_variables()
    {
        var snapshot = _subject.CaptureSnapshot(null, "raw-json", "vars.cs", 9);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.VariablesJson, Is.EqualTo("raw-json"));
    }

    [Test]
    public void GetSourceCode_unknown_file_returns_not_found_message()
    {
        var response = _subject.GetSourceCode("src/NzbDrone.Core/Does/Not/Exist.cs");

        Assert.That(response.Exists, Is.False);
        Assert.That(response.Content, Does.Contain("was not found"));
    }

    [Test]
    public void GetStatus_reports_tracepoints_sessions_and_dap_probe()
    {
        _subject.AddTracepoint(new TracepointDefinition { Id = "on", IsEnabled = true });
        _subject.AddTracepoint(new TracepointDefinition { Id = "off", IsEnabled = false });
        _subject.RegisterSession("session-a");
        _subject.RecordSnapshot(new TracepointSnapshot());

        var status = _subject.GetStatus();

        Assert.That(status.ActiveTracepointsCount, Is.EqualTo(1));
        Assert.That(status.AttachedSessionCount, Is.EqualTo(1));
        Assert.That(status.CapturedSnapshotsCount, Is.EqualTo(1));
        Assert.That(status.ServerTimestampUtc, Is.Not.EqualTo(default(System.DateTime)));
        Assert.That(status.IsDapAvailable, Is.TypeOf<bool>());
    }

    [Test]
    public void CaptureSnapshot_serializes_object_variables_as_json()
    {
        var snapshot = _subject.CaptureSnapshot(null, new { count = 3, label = "ok" }, "vars.cs", 4);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.VariablesJson, Does.Contain("count"));
        Assert.That(snapshot.VariablesJson, Does.Contain("3"));
    }

    [Test]
    public void RegisterSession_ignores_blank_ids()
    {
        _subject.RegisterSession("  ");
        _subject.UnregisterSession(null);
        Assert.That(_subject.GetStatus().AttachedSessionCount, Is.EqualTo(0));
    }

    [Test]
    public void CaptureSnapshot_evaluates_condition_with_anonymous_variables()
    {
        var tracepoint = _subject.AddTracepoint(new TracepointDefinition
        {
            Id = "anon",
            Condition = "!!variables",
            IsEnabled = true,
        });

        var snapshot = _subject.CaptureSnapshot(tracepoint.Id, new { variables = 2 }, "f.cs", 1);
        Assert.That(snapshot, Is.Not.Null);
    }

    [Test]
    public void GetSourceCode_rejects_rooted_paths()
    {
        var response = _subject.GetSourceCode("/etc/passwd");

        Assert.That(response.Exists, Is.False);
        Assert.That(response.Content, Does.Contain("forbidden"));
    }

    [Test]
    public void GetSourceCode_reads_physical_file_when_present_under_src()
    {
        var rel = "src/_debugger_source_probe/ProbeType.cs";
        var full = Path.Combine(Directory.GetCurrentDirectory(), rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "// probe line 1\n// probe line 2\n");

        try
        {
            var response = _subject.GetSourceCode(rel);

            Assert.That(response.Exists, Is.True);
            Assert.That(response.Content, Does.Contain("probe line 1"));
            Assert.That(response.LineCount, Is.GreaterThanOrEqualTo(2));
        }
        finally
        {
            if (File.Exists(full))
            {
                File.Delete(full);
            }

            var dir = Path.GetDirectoryName(full);
            if (dir != null && Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Test]
    public void CaptureSnapshot_assigns_snapshot_id_and_timestamp_when_missing()
    {
        var snapshot = _subject.CaptureSnapshot(null, null, "auto.cs", 12);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.SnapshotId, Is.Not.Empty);
        Assert.That(snapshot.TimestampUtc, Is.Not.EqualTo(default(DateTime)));
        Assert.That(snapshot.VariablesJson, Is.EqualTo("{}"));
    }

    [Test]
    public void GetStatus_detects_dap_executable_on_path()
    {
        var dapDir = Path.Combine(Path.GetTempPath(), "dap_probe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dapDir);
        var dapPath = Path.Combine(dapDir, "netcoredbg");
        File.WriteAllText(dapPath, string.Empty);
        var oldPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", dapDir + Path.PathSeparator + (oldPath ?? string.Empty));
            var subject = new DeveloperDebuggerService();
            var status = subject.GetStatus();

            Assert.That(status.IsDapAvailable, Is.True);
            Assert.That(status.DapPath, Is.EqualTo(dapPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            if (Directory.Exists(dapDir))
            {
                Directory.Delete(dapDir, true);
            }
        }
    }

    [Test]
    public void GetSourceCode_for_known_type_without_physical_file_returns_metadata_outline()
    {
        var response = _subject.GetSourceCode("src/NzbDrone.Core/Torrents/Torrent.cs");

        Assert.That(response.Exists, Is.True);
        Assert.That(response.Content, Does.Contain("Torrent"));
        Assert.That(response.LineCount, Is.GreaterThan(1));
    }
}
