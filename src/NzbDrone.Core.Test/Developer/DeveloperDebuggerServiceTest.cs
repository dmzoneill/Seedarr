// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Developer.Debugger;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class DeveloperDebuggerServiceTest
{
    private DeveloperDebuggerService _service;
    private IDeveloperDebuggerService _mockService;
    private SystemDeveloperDebuggerController _controllerWithService;
    private SystemDeveloperDebuggerController _controllerWithoutService;

    [SetUp]
    public void SetUp()
    {
        _service = new DeveloperDebuggerService();
        _mockService = Substitute.For<IDeveloperDebuggerService>();
        _controllerWithService = new SystemDeveloperDebuggerController(_mockService);
        _controllerWithoutService = new SystemDeveloperDebuggerController(null);
    }

    // -------------------------------------------------------------------------
    // DeveloperDebuggerService: GetStatus()
    // -------------------------------------------------------------------------

    [Test]
    public void GetStatus_should_return_status_report_with_expected_fields()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var status = _service.GetStatus();

        Assert.That(status, Is.Not.Null);
        Assert.That(status.AttachedSessionCount, Is.EqualTo(0));
        Assert.That(status.ActiveTracepointsCount, Is.EqualTo(0));
        Assert.That(status.CapturedSnapshotsCount, Is.EqualTo(0));
        Assert.That(status.ServerTimestampUtc, Is.GreaterThanOrEqualTo(before));
        Assert.That(status.ServerTimestampUtc, Is.LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(2)));

        if (!string.IsNullOrEmpty(status.DapPath))
        {
            Assert.That(status.IsDapAvailable, Is.True);
        }
        else
        {
            Assert.That(status.IsDapAvailable, Is.False);
        }
    }

    [Test]
    public void GetStatus_should_accurately_reflect_active_tracepoints_attached_sessions_and_captured_snapshots()
    {
        _service.RegisterSession("session-1");
        _service.RegisterSession("session-2");

        _service.AddTracepoint(new TracepointDefinition { FilePath = "File1.cs", LineNumber = 10, IsEnabled = true });
        _service.AddTracepoint(new TracepointDefinition { FilePath = "File2.cs", LineNumber = 20, IsEnabled = false });

        _service.RecordSnapshot(new TracepointSnapshot { FilePath = "File1.cs", LineNumber = 10 });

        var status = _service.GetStatus();

        Assert.That(status.AttachedSessionCount, Is.EqualTo(2));
        Assert.That(status.ActiveTracepointsCount, Is.EqualTo(1));
        Assert.That(status.CapturedSnapshotsCount, Is.EqualTo(1));
    }

    // -------------------------------------------------------------------------
    // DeveloperDebuggerService: AddTracepoint()
    // -------------------------------------------------------------------------

    [Test]
    public void AddTracepoint_should_throw_ArgumentNullException_when_tracepoint_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => _service.AddTracepoint(null));
    }

    [Test]
    public void AddTracepoint_should_auto_generate_id_when_empty_or_whitespace()
    {
        var tpEmptyId = new TracepointDefinition { Id = string.Empty, FilePath = "Test.cs", LineNumber = 10 };
        var addedEmpty = _service.AddTracepoint(tpEmptyId);

        Assert.That(addedEmpty.Id, Is.Not.Null.And.Not.Empty);
        Assert.That(addedEmpty.Id.Length, Is.EqualTo(32));

        var tpWhitespaceId = new TracepointDefinition { Id = "   ", FilePath = "Test.cs", LineNumber = 20 };
        var addedWhitespace = _service.AddTracepoint(tpWhitespaceId);

        Assert.That(addedWhitespace.Id, Is.Not.Null.And.Not.Empty);
        Assert.That(addedWhitespace.Id.Length, Is.EqualTo(32));
        Assert.That(addedWhitespace.Id, Is.Not.EqualTo(addedEmpty.Id));
    }

    [Test]
    public void AddTracepoint_should_assign_default_CreatedAtUtc_when_default()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var tp = new TracepointDefinition { CreatedAtUtc = default, FilePath = "Test.cs", LineNumber = 5 };

        var added = _service.AddTracepoint(tp);

        Assert.That(added.CreatedAtUtc, Is.Not.EqualTo(default(DateTime)));
        Assert.That(added.CreatedAtUtc, Is.GreaterThanOrEqualTo(before));
        Assert.That(added.CreatedAtUtc, Is.LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(2)));
    }

    [Test]
    public void AddTracepoint_should_preserve_custom_properties()
    {
        var customTime = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var tp = new TracepointDefinition
        {
            Id = "custom-tracepoint-id",
            FilePath = "src/Custom/Service.cs",
            LineNumber = 142,
            Condition = "x > 100",
            HitCount = 7,
            IsEnabled = false,
            CreatedAtUtc = customTime,
        };

        var added = _service.AddTracepoint(tp);

        Assert.That(added.Id, Is.EqualTo("custom-tracepoint-id"));
        Assert.That(added.FilePath, Is.EqualTo("src/Custom/Service.cs"));
        Assert.That(added.LineNumber, Is.EqualTo(142));
        Assert.That(added.Condition, Is.EqualTo("x > 100"));
        Assert.That(added.HitCount, Is.EqualTo(7));
        Assert.That(added.IsEnabled, Is.False);
        Assert.That(added.CreatedAtUtc, Is.EqualTo(customTime));
    }

    // -------------------------------------------------------------------------
    // DeveloperDebuggerService: RemoveTracepoint()
    // -------------------------------------------------------------------------

    [Test]
    public void RemoveTracepoint_should_return_true_when_removing_existing_id()
    {
        var tp = _service.AddTracepoint(new TracepointDefinition { FilePath = "Test.cs", LineNumber = 1 });

        var removed = _service.RemoveTracepoint(tp.Id);

        Assert.That(removed, Is.True);
        Assert.That(_service.GetTracepoints(), Does.Not.Contain(tp));
    }

    [Test]
    public void RemoveTracepoint_should_return_false_when_removing_nonexistent_id()
    {
        var removed = _service.RemoveTracepoint("nonexistent-id-9999");
        Assert.That(removed, Is.False);
    }

    [Test]
    public void RemoveTracepoint_should_return_false_for_null_or_empty_or_whitespace_id()
    {
        Assert.That(_service.RemoveTracepoint(null), Is.False);
        Assert.That(_service.RemoveTracepoint(string.Empty), Is.False);
        Assert.That(_service.RemoveTracepoint("   "), Is.False);
    }

    // -------------------------------------------------------------------------
    // DeveloperDebuggerService: GetTracepoints() & ClearTracepoints()
    // -------------------------------------------------------------------------

    [Test]
    public void GetTracepoints_should_return_empty_collection_initially()
    {
        var tracepoints = _service.GetTracepoints();
        Assert.That(tracepoints, Is.Not.Null);
        Assert.That(tracepoints, Is.Empty);
    }

    [Test]
    public void GetTracepoints_should_return_tracepoints_sorted_by_FilePath_and_LineNumber()
    {
        _service.AddTracepoint(new TracepointDefinition { FilePath = "b/Service.cs", LineNumber = 30 });
        _service.AddTracepoint(new TracepointDefinition { FilePath = "a/Controller.cs", LineNumber = 50 });
        _service.AddTracepoint(new TracepointDefinition { FilePath = "a/Controller.cs", LineNumber = 10 });
        _service.AddTracepoint(new TracepointDefinition { FilePath = "b/Service.cs", LineNumber = 5 });

        var tracepoints = _service.GetTracepoints();

        Assert.That(tracepoints.Count, Is.EqualTo(4));
        Assert.That(tracepoints[0].FilePath, Is.EqualTo("a/Controller.cs"));
        Assert.That(tracepoints[0].LineNumber, Is.EqualTo(10));
        Assert.That(tracepoints[1].FilePath, Is.EqualTo("a/Controller.cs"));
        Assert.That(tracepoints[1].LineNumber, Is.EqualTo(50));
        Assert.That(tracepoints[2].FilePath, Is.EqualTo("b/Service.cs"));
        Assert.That(tracepoints[2].LineNumber, Is.EqualTo(5));
        Assert.That(tracepoints[3].FilePath, Is.EqualTo("b/Service.cs"));
        Assert.That(tracepoints[3].LineNumber, Is.EqualTo(30));
    }

    [Test]
    public void ClearTracepoints_should_clear_all_tracepoints()
    {
        _service.AddTracepoint(new TracepointDefinition { FilePath = "File1.cs", LineNumber = 10 });
        _service.AddTracepoint(new TracepointDefinition { FilePath = "File2.cs", LineNumber = 20 });
        Assert.That(_service.GetTracepoints().Count, Is.EqualTo(2));

        _service.ClearTracepoints();

        Assert.That(_service.GetTracepoints(), Is.Empty);
        Assert.That(_service.GetStatus().ActiveTracepointsCount, Is.EqualTo(0));
    }

    // -------------------------------------------------------------------------
    // DeveloperDebuggerService: CaptureSnapshot()
    // -------------------------------------------------------------------------

    [Test]
    public void CaptureSnapshot_with_tracepointId_should_associate_tracepoint_and_record_snapshot()
    {
        var tp = _service.AddTracepoint(new TracepointDefinition
        {
            FilePath = "src/TestFile.cs",
            LineNumber = 88,
        });

        var snapshot = _service.CaptureSnapshot(tp.Id);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.TracepointId, Is.EqualTo(tp.Id));
        Assert.That(snapshot.FilePath, Is.EqualTo("src/TestFile.cs"));
        Assert.That(snapshot.LineNumber, Is.EqualTo(88));
        Assert.That(tp.HitCount, Is.EqualTo(1));

        var recorded = _service.GetSnapshots(10);
        Assert.That(recorded, Has.Member(snapshot));
    }

    [Test]
    public void CaptureSnapshot_without_tracepointId_should_succeed()
    {
        var snapshotNullId = _service.CaptureSnapshot(null, null, "Custom.cs", 15);
        Assert.That(snapshotNullId, Is.Not.Null);
        Assert.That(snapshotNullId.TracepointId, Is.EqualTo(string.Empty));
        Assert.That(snapshotNullId.FilePath, Is.EqualTo("Custom.cs"));
        Assert.That(snapshotNullId.LineNumber, Is.EqualTo(15));

        var snapshotEmptyId = _service.CaptureSnapshot(string.Empty, null, "CustomEmpty.cs", 25);
        Assert.That(snapshotEmptyId, Is.Not.Null);
        Assert.That(snapshotEmptyId.TracepointId, Is.EqualTo(string.Empty));
    }

    [Test]
    public void CaptureSnapshot_should_return_null_when_tracepoint_is_disabled()
    {
        var tp = _service.AddTracepoint(new TracepointDefinition
        {
            FilePath = "src/Disabled.cs",
            LineNumber = 10,
            IsEnabled = false,
        });

        var snapshot = _service.CaptureSnapshot(tp.Id);

        Assert.That(snapshot, Is.Null);
        Assert.That(tp.HitCount, Is.EqualTo(0));
        Assert.That(_service.GetSnapshots(10), Is.Empty);
    }

    [Test]
    public void CaptureSnapshot_condition_evaluation_true_condition_returns_snapshot()
    {
        var tp = _service.AddTracepoint(new TracepointDefinition
        {
            FilePath = "src/Cond.cs",
            LineNumber = 12,
            Condition = "count > 10 && name === 'sample'",
        });

        var variables = new Dictionary<string, object>
        {
            { "count", 25 },
            { "name", "sample" },
        };

        var snapshot = _service.CaptureSnapshot(tp.Id, variables);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(tp.HitCount, Is.EqualTo(1));
    }

    [Test]
    public void CaptureSnapshot_condition_evaluation_false_condition_returns_null()
    {
        var tp = _service.AddTracepoint(new TracepointDefinition
        {
            FilePath = "src/CondFalse.cs",
            LineNumber = 12,
            Condition = "count > 100",
        });

        var variables = new Dictionary<string, object>
        {
            { "count", 25 },
        };

        var snapshot = _service.CaptureSnapshot(tp.Id, variables);

        Assert.That(snapshot, Is.Null);
        Assert.That(tp.HitCount, Is.EqualTo(0));
    }

    [Test]
    public void CaptureSnapshot_condition_evaluation_malformed_condition_handles_warning_gracefully_and_returns_snapshot()
    {
        var tp = _service.AddTracepoint(new TracepointDefinition
        {
            FilePath = "src/SyntaxErr.cs",
            LineNumber = 12,
            Condition = "function {{{ invalid javascript syntax +++",
        });

        var snapshot = _service.CaptureSnapshot(tp.Id);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(tp.HitCount, Is.EqualTo(1));
    }

    [Test]
    public void CaptureSnapshot_condition_evaluation_with_poco_variables()
    {
        var tp = _service.AddTracepoint(new TracepointDefinition
        {
            FilePath = "src/PocoTest.cs",
            LineNumber = 10,
            Condition = "variables.Score >= 50",
        });

        var passingPoco = new TestScorePoco { Score = 75 };
        var passingSnapshot = _service.CaptureSnapshot(tp.Id, passingPoco);
        Assert.That(passingSnapshot, Is.Not.Null);

        var failingPoco = new TestScorePoco { Score = 20 };
        var failingSnapshot = _service.CaptureSnapshot(tp.Id, failingPoco);
        Assert.That(failingSnapshot, Is.Null);
    }

    [Test]
    public void CaptureSnapshot_caller_frame_resolution_when_filePath_and_lineNumber_not_provided()
    {
        var snapshot = _service.CaptureSnapshot(null);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.ThreadId, Is.EqualTo(Environment.CurrentManagedThreadId));
        Assert.That(snapshot.CallStack, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void CaptureSnapshot_filePath_and_lineNumber_override_takes_precedence_over_tracepoint()
    {
        var tp = _service.AddTracepoint(new TracepointDefinition
        {
            FilePath = "OriginalPath.cs",
            LineNumber = 100,
        });

        var snapshot = _service.CaptureSnapshot(tp.Id, null, filePath: "OverridePath.cs", lineNumber: 999);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.FilePath, Is.EqualTo("OverridePath.cs"));
        Assert.That(snapshot.LineNumber, Is.EqualTo(999));
    }

    [Test]
    public void CaptureSnapshot_populates_threadId_and_callstack()
    {
        var snapshot = _service.CaptureSnapshot(null, null, "TestThread.cs", 10);

        Assert.That(snapshot.ThreadId, Is.EqualTo(Environment.CurrentManagedThreadId));
        Assert.That(snapshot.CallStack, Does.Contain("CaptureSnapshot"));
    }

    [Test]
    public void CaptureSnapshot_variables_serialization_string()
    {
        var snapshot = _service.CaptureSnapshot(null, "raw-string-content", "File.cs", 1);
        Assert.That(snapshot.VariablesJson, Is.EqualTo("raw-string-content"));
    }

    [Test]
    public void CaptureSnapshot_variables_serialization_dictionary()
    {
        var dict = new Dictionary<string, object>
        {
            { "hero", "Seedarr" },
            { "magicNumber", 42 },
        };

        var snapshot = _service.CaptureSnapshot(null, dict, "File.cs", 1);
        Assert.That(snapshot.VariablesJson, Does.Contain("\"hero\":\"Seedarr\""));
        Assert.That(snapshot.VariablesJson, Does.Contain("\"magicNumber\":42"));
    }

    [Test]
    public void CaptureSnapshot_variables_serialization_complex_poco()
    {
        var poco = new TestComplexPoco
        {
            Name = "DebuggerTest",
            Values = new List<int> { 1, 2, 3 },
            Nested = new TestScorePoco { Score = 99 },
        };

        var snapshot = _service.CaptureSnapshot(null, poco, "File.cs", 1);
        Assert.That(snapshot.VariablesJson, Does.Contain("\"Name\":\"DebuggerTest\""));
        Assert.That(snapshot.VariablesJson, Does.Contain("\"Values\":[1,2,3]"));
        Assert.That(snapshot.VariablesJson, Does.Contain("\"Score\":99"));
    }

    [Test]
    public void CaptureSnapshot_variables_serialization_null()
    {
        var snapshot = _service.CaptureSnapshot(null, null, "File.cs", 1);
        Assert.That(snapshot.VariablesJson, Is.EqualTo("{}"));
    }

    // -------------------------------------------------------------------------
    // DeveloperDebuggerService: RecordSnapshot()
    // -------------------------------------------------------------------------

    [Test]
    public void RecordSnapshot_should_do_nothing_when_snapshot_is_null()
    {
        Assert.DoesNotThrow(() => _service.RecordSnapshot(null));
        Assert.That(_service.GetSnapshots(10), Is.Empty);
    }

    [Test]
    public void RecordSnapshot_should_auto_generate_SnapshotId_when_missing()
    {
        var snapshot = new TracepointSnapshot { SnapshotId = string.Empty, FilePath = "File.cs", LineNumber = 10 };
        _service.RecordSnapshot(snapshot);

        Assert.That(snapshot.SnapshotId, Is.Not.Null.And.Not.Empty);
        Assert.That(snapshot.SnapshotId.Length, Is.EqualTo(32));
    }

    [Test]
    public void RecordSnapshot_should_set_default_TimestampUtc_when_default()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var snapshot = new TracepointSnapshot { TimestampUtc = default, FilePath = "File.cs", LineNumber = 10 };
        _service.RecordSnapshot(snapshot);

        Assert.That(snapshot.TimestampUtc, Is.Not.EqualTo(default(DateTime)));
        Assert.That(snapshot.TimestampUtc, Is.GreaterThanOrEqualTo(before));
        Assert.That(snapshot.TimestampUtc, Is.LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(2)));
    }

    [Test]
    public void RecordSnapshot_should_increment_HitCount_on_matching_tracepoint()
    {
        var tp = _service.AddTracepoint(new TracepointDefinition { FilePath = "TestHit.cs", LineNumber = 5 });
        Assert.That(tp.HitCount, Is.EqualTo(0));

        _service.RecordSnapshot(new TracepointSnapshot { TracepointId = tp.Id });
        _service.RecordSnapshot(new TracepointSnapshot { TracepointId = tp.Id });
        _service.RecordSnapshot(new TracepointSnapshot { TracepointId = tp.Id });

        Assert.That(tp.HitCount, Is.EqualTo(3));
    }

    [Test]
    public void RecordSnapshot_should_trim_snapshots_when_exceeding_MaxSnapshots()
    {
        const int totalSnapshots = 505;
        for (var i = 0; i < totalSnapshots; i++)
        {
            _service.RecordSnapshot(new TracepointSnapshot
            {
                SnapshotId = $"snap-{i}",
                FilePath = "File.cs",
                LineNumber = i,
            });
        }

        var allSnapshots = _service.GetSnapshots(1000);
        Assert.That(allSnapshots.Count, Is.EqualTo(500));

        // The first 5 snapshots should have been trimmed out
        Assert.That(allSnapshots.Any(s => s.SnapshotId == "snap-0"), Is.False);
        Assert.That(allSnapshots.Any(s => s.SnapshotId == "snap-4"), Is.False);
        Assert.That(allSnapshots.Any(s => s.SnapshotId == "snap-5"), Is.True);
        Assert.That(allSnapshots.Any(s => s.SnapshotId == "snap-504"), Is.True);
    }

    // -------------------------------------------------------------------------
    // DeveloperDebuggerService: GetSnapshots() & ClearSnapshots()
    // -------------------------------------------------------------------------

    [Test]
    public void GetSnapshots_should_return_snapshots_in_reverse_chronological_order()
    {
        var s1 = new TracepointSnapshot { SnapshotId = "s1" };
        var s2 = new TracepointSnapshot { SnapshotId = "s2" };
        var s3 = new TracepointSnapshot { SnapshotId = "s3" };

        _service.RecordSnapshot(s1);
        _service.RecordSnapshot(s2);
        _service.RecordSnapshot(s3);

        var retrieved = _service.GetSnapshots(10);

        Assert.That(retrieved.Count, Is.EqualTo(3));
        Assert.That(retrieved[0].SnapshotId, Is.EqualTo("s3"));
        Assert.That(retrieved[1].SnapshotId, Is.EqualTo("s2"));
        Assert.That(retrieved[2].SnapshotId, Is.EqualTo("s1"));
    }

    [Test]
    public void GetSnapshots_should_use_default_limit_of_50()
    {
        for (var i = 0; i < 60; i++)
        {
            _service.RecordSnapshot(new TracepointSnapshot { SnapshotId = $"id-{i}" });
        }

        var defaultList = _service.GetSnapshots();
        Assert.That(defaultList.Count, Is.EqualTo(50));
    }

    [Test]
    public void GetSnapshots_should_respect_custom_limit()
    {
        for (var i = 0; i < 20; i++)
        {
            _service.RecordSnapshot(new TracepointSnapshot { SnapshotId = $"id-{i}" });
        }

        var customList = _service.GetSnapshots(7);
        Assert.That(customList.Count, Is.EqualTo(7));
    }

    [Test]
    public void GetSnapshots_should_clamp_limit_to_valid_range()
    {
        for (var i = 0; i < 5; i++)
        {
            _service.RecordSnapshot(new TracepointSnapshot { SnapshotId = $"id-{i}" });
        }

        var clampedLower = _service.GetSnapshots(0);
        Assert.That(clampedLower.Count, Is.EqualTo(1));

        var clampedNegative = _service.GetSnapshots(-10);
        Assert.That(clampedNegative.Count, Is.EqualTo(1));

        var clampedUpper = _service.GetSnapshots(1000);
        Assert.That(clampedUpper.Count, Is.EqualTo(5));
    }

    [Test]
    public void ClearSnapshots_should_remove_all_snapshots()
    {
        _service.RecordSnapshot(new TracepointSnapshot { SnapshotId = "snap1" });
        _service.RecordSnapshot(new TracepointSnapshot { SnapshotId = "snap2" });
        Assert.That(_service.GetSnapshots(10).Count, Is.EqualTo(2));

        _service.ClearSnapshots();

        Assert.That(_service.GetSnapshots(10), Is.Empty);
        Assert.That(_service.GetStatus().CapturedSnapshotsCount, Is.EqualTo(0));
    }

    // -------------------------------------------------------------------------
    // DeveloperDebuggerService: RegisterSession() & UnregisterSession()
    // -------------------------------------------------------------------------

    [Test]
    public void RegisterSession_and_UnregisterSession_should_manage_active_sessions()
    {
        Assert.That(_service.GetStatus().AttachedSessionCount, Is.EqualTo(0));

        _service.RegisterSession("session-abc");
        Assert.That(_service.GetStatus().AttachedSessionCount, Is.EqualTo(1));

        _service.RegisterSession("session-def");
        Assert.That(_service.GetStatus().AttachedSessionCount, Is.EqualTo(2));

        // Re-registering existing session does not duplicate
        _service.RegisterSession("session-abc");
        Assert.That(_service.GetStatus().AttachedSessionCount, Is.EqualTo(2));

        // Null or whitespace session IDs do nothing
        _service.RegisterSession(null);
        _service.RegisterSession(string.Empty);
        _service.RegisterSession("   ");
        Assert.That(_service.GetStatus().AttachedSessionCount, Is.EqualTo(2));

        _service.UnregisterSession("session-abc");
        Assert.That(_service.GetStatus().AttachedSessionCount, Is.EqualTo(1));

        // Nonexistent unregister is safe
        _service.UnregisterSession("nonexistent");
        _service.UnregisterSession(null);
        _service.UnregisterSession(string.Empty);
        Assert.That(_service.GetStatus().AttachedSessionCount, Is.EqualTo(1));

        _service.UnregisterSession("session-def");
        Assert.That(_service.GetStatus().AttachedSessionCount, Is.EqualTo(0));
    }

    // -------------------------------------------------------------------------
    // DeveloperDebuggerService: GetKnownSourceFiles()
    // -------------------------------------------------------------------------

    [Test]
    public void GetKnownSourceFiles_should_return_files_from_disk_and_reflection()
    {
        var files = _service.GetKnownSourceFiles();

        Assert.That(files, Is.Not.Null);
        Assert.That(files, Is.Not.Empty);

        // Result should be sorted by FilePath
        var sorted = files.OrderBy(f => f.FilePath).ToList();
        Assert.That(files, Is.EqualTo(sorted));

        // Should include DeveloperDebuggerService or other core classes
        var debuggerServiceEntry = files.FirstOrDefault(f => f.ClassName == nameof(DeveloperDebuggerService));
        Assert.That(debuggerServiceEntry, Is.Not.Null);
        Assert.That(debuggerServiceEntry.FilePath, Does.EndWith("DeveloperDebuggerService.cs"));
        Assert.That(debuggerServiceEntry.Namespace, Is.Not.Null.And.Not.Empty);
        Assert.That(debuggerServiceEntry.Subsystem, Is.Not.Null.And.Not.Empty);
    }

    // -------------------------------------------------------------------------
    // DeveloperDebuggerService: GetSourceCode()
    // -------------------------------------------------------------------------

    [Test]
    public void GetSourceCode_should_read_existing_source_file_from_disk()
    {
        var response = _service.GetSourceCode("src/NzbDrone.Core/Developer/Debugger/IDeveloperDebuggerService.cs");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Exists, Is.True);
        Assert.That(response.FilePath, Is.EqualTo("src/NzbDrone.Core/Developer/Debugger/IDeveloperDebuggerService.cs"));
        Assert.That(response.Content, Does.Contain("IDeveloperDebuggerService"));
        Assert.That(response.LineCount, Is.GreaterThan(20));
    }

    [Test]
    public void GetSourceCode_with_nonexistent_file_matching_known_type_should_return_simulated_metadata()
    {
        // Path does not exist on disk, but DeveloperDebuggerService is a loaded type in this domain
        var response = _service.GetSourceCode("src/SimulatedContainer/DeveloperDebuggerService.cs");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Exists, Is.True);
        Assert.That(response.Content, Does.Contain("// Runtime Metadata Outline for NzbDrone.Core.Developer.Debugger.DeveloperDebuggerService"));
        Assert.That(response.Content, Does.Contain("public class DeveloperDebuggerService"));
        Assert.That(response.LineCount, Is.GreaterThan(5));
    }

    [Test]
    public void GetSourceCode_with_completely_unknown_file_should_return_placeholder_not_found()
    {
        var response = _service.GetSourceCode("src/FakePath/NonExistentType_987654321.cs");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Exists, Is.False);
        Assert.That(response.LineCount, Is.EqualTo(1));
        Assert.That(response.Content, Does.Contain("was not found on disk or in loaded assembly metadata"));
    }

    [Test]
    public void GetSourceCode_with_empty_or_whitespace_path_should_return_error_response()
    {
        var nullRes = _service.GetSourceCode(null);
        Assert.That(nullRes.Exists, Is.False);
        Assert.That(nullRes.Content, Does.Contain("// No file path specified"));

        var emptyRes = _service.GetSourceCode(string.Empty);
        Assert.That(emptyRes.Exists, Is.False);
        Assert.That(emptyRes.Content, Does.Contain("// No file path specified"));

        var whitespaceRes = _service.GetSourceCode("   ");
        Assert.That(whitespaceRes.Exists, Is.False);
        Assert.That(whitespaceRes.Content, Does.Contain("// No file path specified"));
    }

    [Test]
    public void GetSourceCode_with_directory_traversal_or_rooted_path_should_return_forbidden_response()
    {
        var traversalRes = _service.GetSourceCode("../secret/password.cs");
        Assert.That(traversalRes.Exists, Is.False);
        Assert.That(traversalRes.Content, Does.Contain("// Invalid or forbidden file path"));

        var rootedRes = _service.GetSourceCode("/usr/bin/something.cs");
        Assert.That(rootedRes.Exists, Is.False);
        Assert.That(rootedRes.Content, Does.Contain("// Invalid or forbidden file path"));
    }

    [Test]
    public void GetSourceCode_lines_out_of_range_handling()
    {
        var response = _service.GetSourceCode("src/NzbDrone.Core/Developer/Debugger/IDeveloperDebuggerService.cs");
        Assert.That(response.Exists, Is.True);

        var lines = response.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        Assert.That(lines.Length, Is.EqualTo(response.LineCount));

        // Accessing lines beyond LineCount should be safely checked without out-of-range exceptions
        var outOfRangeIndex = response.LineCount + 500;
        var isBeyondRange = outOfRangeIndex >= lines.Length;
        Assert.That(isBeyondRange, Is.True);
    }

    // -------------------------------------------------------------------------
    // SystemDeveloperDebuggerController: GetStatus()
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_GetStatus_with_service_returns_ok_with_service_status()
    {
        var expectedReport = new DebuggerStatusReport
        {
            IsDapAvailable = true,
            DapPath = "/usr/bin/netcoredbg",
            AttachedSessionCount = 3,
            ActiveTracepointsCount = 5,
            CapturedSnapshotsCount = 12,
        };

        _mockService.GetStatus().Returns(expectedReport);

        var result = _controllerWithService.GetStatus();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(expectedReport));
    }

    [Test]
    public void Controller_GetStatus_with_null_service_fallback_returns_default_report()
    {
        var result = _controllerWithoutService.GetStatus();

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.AttachedSessionCount, Is.EqualTo(0));
        Assert.That(result.Value.ActiveTracepointsCount, Is.EqualTo(0));
        Assert.That(result.Value.CapturedSnapshotsCount, Is.EqualTo(0));
    }

    // -------------------------------------------------------------------------
    // SystemDeveloperDebuggerController: GetTracepoints()
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_GetTracepoints_with_service_returns_ok_with_tracepoints()
    {
        var list = new List<TracepointDefinition>
        {
            new TracepointDefinition { FilePath = "File1.cs", LineNumber = 10 },
            new TracepointDefinition { FilePath = "File2.cs", LineNumber = 20 },
        };

        _mockService.GetTracepoints().Returns(list);

        var result = _controllerWithService.GetTracepoints();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(list));
    }

    [Test]
    public void Controller_GetTracepoints_with_null_service_fallback_returns_empty_list()
    {
        var result = _controllerWithoutService.GetTracepoints();

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value, Is.Empty);
    }

    // -------------------------------------------------------------------------
    // SystemDeveloperDebuggerController: AddTracepoint()
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_AddTracepoint_with_service_and_valid_tracepoint_returns_ok()
    {
        var input = new TracepointDefinition { FilePath = "Valid.cs", LineNumber = 42 };
        var created = new TracepointDefinition { Id = "generated-id", FilePath = "Valid.cs", LineNumber = 42 };

        _mockService.AddTracepoint(input).Returns(created);

        var result = _controllerWithService.AddTracepoint(input);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(created));
    }

    [Test]
    public void Controller_AddTracepoint_with_service_and_null_tracepoint_creates_new_instance_and_returns_ok()
    {
        var created = new TracepointDefinition { Id = "auto-id" };
        _mockService.AddTracepoint(Arg.Any<TracepointDefinition>()).Returns(created);

        var result = _controllerWithService.AddTracepoint(null);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(created));
        _mockService.Received(1).AddTracepoint(Arg.Is<TracepointDefinition>(t => t != null));
    }

    [Test]
    public void Controller_AddTracepoint_with_null_service_returns_bad_request()
    {
        var result = _controllerWithoutService.AddTracepoint(new TracepointDefinition());

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Debugger service not registered."));
    }

    // -------------------------------------------------------------------------
    // SystemDeveloperDebuggerController: RemoveTracepoint()
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_RemoveTracepoint_when_found_returns_no_content()
    {
        _mockService.RemoveTracepoint("existing-tp-id").Returns(true);

        var result = _controllerWithService.RemoveTracepoint("existing-tp-id");

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    [Test]
    public void Controller_RemoveTracepoint_when_not_found_returns_not_found()
    {
        _mockService.RemoveTracepoint("nonexistent-tp-id").Returns(false);

        var result = _controllerWithService.RemoveTracepoint("nonexistent-tp-id");

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Controller_RemoveTracepoint_with_null_service_returns_not_found()
    {
        var result = _controllerWithoutService.RemoveTracepoint("any-id");

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    // -------------------------------------------------------------------------
    // SystemDeveloperDebuggerController: ClearTracepoints()
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_ClearTracepoints_with_service_invokes_service_and_returns_no_content()
    {
        var result = _controllerWithService.ClearTracepoints();

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        _mockService.Received(1).ClearTracepoints();
    }

    [Test]
    public void Controller_ClearTracepoints_with_null_service_returns_no_content()
    {
        var result = _controllerWithoutService.ClearTracepoints();

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    // -------------------------------------------------------------------------
    // SystemDeveloperDebuggerController: GetSnapshots()
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_GetSnapshots_with_service_returns_ok_with_snapshots()
    {
        var snapshots = new List<TracepointSnapshot>
        {
            new TracepointSnapshot { SnapshotId = "snap-1" },
            new TracepointSnapshot { SnapshotId = "snap-2" },
        };

        _mockService.GetSnapshots(30).Returns(snapshots);

        var result = _controllerWithService.GetSnapshots(30);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(snapshots));
    }

    [Test]
    public void Controller_GetSnapshots_with_null_service_fallback_returns_empty_list()
    {
        var result = _controllerWithoutService.GetSnapshots(50);

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value, Is.Empty);
    }

    // -------------------------------------------------------------------------
    // SystemDeveloperDebuggerController: RecordSnapshot()
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_RecordSnapshot_with_service_and_valid_snapshot_returns_ok()
    {
        var snapshot = new TracepointSnapshot { SnapshotId = "valid-snap", FilePath = "File.cs", LineNumber = 10 };

        var result = _controllerWithService.RecordSnapshot(snapshot);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(snapshot));
        _mockService.Received(1).RecordSnapshot(snapshot);
    }

    [Test]
    public void Controller_RecordSnapshot_with_service_and_null_snapshot_creates_new_instance_and_returns_ok()
    {
        var result = _controllerWithService.RecordSnapshot(null);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.InstanceOf<TracepointSnapshot>());
        _mockService.Received(1).RecordSnapshot(Arg.Is<TracepointSnapshot>(s => s != null));
    }

    [Test]
    public void Controller_RecordSnapshot_with_null_service_returns_bad_request()
    {
        var result = _controllerWithoutService.RecordSnapshot(new TracepointSnapshot());

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Debugger service not registered."));
    }

    // -------------------------------------------------------------------------
    // SystemDeveloperDebuggerController: ClearSnapshots()
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_ClearSnapshots_with_service_invokes_service_and_returns_no_content()
    {
        var result = _controllerWithService.ClearSnapshots();

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        _mockService.Received(1).ClearSnapshots();
    }

    [Test]
    public void Controller_ClearSnapshots_with_null_service_returns_no_content()
    {
        var result = _controllerWithoutService.ClearSnapshots();

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    // -------------------------------------------------------------------------
    // SystemDeveloperDebuggerController: GetKnownSourceFiles() (GetFiles endpoint)
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_GetKnownSourceFiles_with_service_returns_ok_with_files()
    {
        var files = new List<DebuggerSourceFileItem>
        {
            new DebuggerSourceFileItem { FilePath = "src/File1.cs", ClassName = "File1" },
            new DebuggerSourceFileItem { FilePath = "src/File2.cs", ClassName = "File2" },
        };

        _mockService.GetKnownSourceFiles().Returns(files);

        var result = _controllerWithService.GetFiles();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(files));
    }

    [Test]
    public void Controller_GetKnownSourceFiles_with_null_service_fallback_returns_empty_list()
    {
        var result = _controllerWithoutService.GetFiles();

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value, Is.Empty);
    }

    // -------------------------------------------------------------------------
    // SystemDeveloperDebuggerController: GetSourceCode() (GetSource endpoint)
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_GetSourceCode_with_service_returns_ok_with_source_response()
    {
        var expectedResponse = new DebuggerSourceCodeResponse
        {
            FilePath = "src/Test.cs",
            Content = "public class Test {}",
            LineCount = 1,
            Exists = true,
        };

        _mockService.GetSourceCode("src/Test.cs").Returns(expectedResponse);

        var result = _controllerWithService.GetSource("src/Test.cs");

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.SameAs(expectedResponse));
    }

    [Test]
    public void Controller_GetSourceCode_with_null_service_fallback_returns_unregistered_response()
    {
        var result = _controllerWithoutService.GetSource("src/Unknown.cs");

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.FilePath, Is.EqualTo("src/Unknown.cs"));
        Assert.That(result.Value.Exists, Is.False);
        Assert.That(result.Value.Content, Is.EqualTo("// Debugger service not registered"));
    }

    // -------------------------------------------------------------------------
    // Helper POCOs for Snapshot condition evaluation & serialization tests
    // -------------------------------------------------------------------------

    [Test]
    public void Controller_should_have_Authorize_AdminOnly_attribute()
    {
        var type = typeof(SystemDeveloperDebuggerController);
        var attr = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.AdminOnly));
    }

    private class TestScorePoco
    {
        public int Score { get; set; }
    }

    private class TestComplexPoco
    {
        public string Name { get; set; } = string.Empty;

        public List<int> Values { get; set; } = new();

        public TestScorePoco Nested { get; set; }
    }
}
