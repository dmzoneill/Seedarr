using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using Seedarr.Api.V1.Automation;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class AutomationScriptUpdateMergerTest
{
    [Test]
    public void Merge_null_existing_throws()
    {
        Assert.That(
            () => AutomationScriptUpdateMerger.Merge(null!, new AutomationScriptResource(), new HashSet<string>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Merge_null_resource_throws()
    {
        Assert.That(
            () => AutomationScriptUpdateMerger.Merge(new AutomationScript(), null!, new HashSet<string>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Merge_with_no_present_keys_keeps_existing()
    {
        var existing = new AutomationScript
        {
            Id = 2,
            Name = "keep",
            Code = "console.log(1)",
            IsEnabled = false,
            TargetCategories = new List<string> { "tv" },
            TargetTagIds = new List<int> { 3 },
        };
        var incoming = new AutomationScriptResource
        {
            Name = "new",
            Code = "evil",
            IsEnabled = true,
        };

        var merged = AutomationScriptUpdateMerger.Merge(existing, incoming, new HashSet<string>());

        Assert.That(merged.Id, Is.EqualTo(2));
        Assert.That(merged.Name, Is.EqualTo("keep"));
        Assert.That(merged.Code, Is.EqualTo("console.log(1)"));
        Assert.That(merged.IsEnabled, Is.False);
        Assert.That(merged.TargetCategories, Is.EqualTo(new List<string> { "tv" }));
        Assert.That(merged.TargetTagIds, Is.EqualTo(new List<int> { 3 }));
    }

    [Test]
    public void Merge_with_present_keys_applies_incoming_and_defaults_lists()
    {
        var existing = new AutomationScript
        {
            Name = "old",
            TargetCategories = new List<string> { "a" },
            TargetTagIds = new List<int> { 1 },
        };
        var incoming = new AutomationScriptResource
        {
            Name = null,
            Code = null,
            TargetCategories = null,
            TargetTagIds = null,
            IsEnabled = true,
        };
        var present = new HashSet<string>
        {
            "name",
            "code",
            "isEnabled",
            "targetCategories",
            "targetTagIds",
        };

        var merged = AutomationScriptUpdateMerger.Merge(existing, incoming, present);

        Assert.That(merged.Name, Is.EqualTo(string.Empty));
        Assert.That(merged.Code, Is.EqualTo(string.Empty));
        Assert.That(merged.IsEnabled, Is.True);
        Assert.That(merged.TargetCategories, Is.Empty);
        Assert.That(merged.TargetTagIds, Is.Empty);
    }

    [Test]
    public void Merge_preserves_execution_metadata_from_existing()
    {
        var existing = new AutomationScript
        {
            LastExecutedAt = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            LastExecutionStatus = "ok",
            LastExecutionLog = "done",
        };
        var incoming = new AutomationScriptResource { Name = "x" };

        var merged = AutomationScriptUpdateMerger.Merge(
            existing,
            incoming,
            new HashSet<string> { "name" });

        Assert.That(merged.LastExecutedAt, Is.EqualTo(existing.LastExecutedAt));
        Assert.That(merged.LastExecutionStatus, Is.EqualTo("ok"));
        Assert.That(merged.LastExecutionLog, Is.EqualTo("done"));
    }
}
