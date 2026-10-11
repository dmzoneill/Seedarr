using System;
using System.Collections.Generic;
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
}
