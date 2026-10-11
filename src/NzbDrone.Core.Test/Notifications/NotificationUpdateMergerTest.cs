using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using Seedarr.Api.V1.Notifications;

namespace NzbDrone.Core.Test.Notifications;

[TestFixture]
public class NotificationUpdateMergerTest
{
    [Test]
    public void Merge_null_existing_throws()
    {
        Assert.That(
            () => NotificationUpdateMerger.Merge(null!, new NotificationResource { Name = "n" }, new HashSet<string>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Merge_null_incoming_throws()
    {
        Assert.That(
            () => NotificationUpdateMerger.Merge(new NotificationDefinition { Name = "n" }, null!, new HashSet<string>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Merge_with_no_present_keys_keeps_existing()
    {
        var existing = new NotificationDefinition
        {
            Id = 9,
            Name = "slack",
            Implementation = "Slack",
            Enable = false,
            Tags = new List<int> { 1 },
            Categories = new List<string> { "tv" },
        };
        var incoming = new NotificationResource
        {
            Name = "ignored",
            Implementation = "Webhook",
            Enable = true,
        };

        var merged = NotificationUpdateMerger.Merge(existing, incoming, new HashSet<string>());

        Assert.That(merged.Id, Is.EqualTo(9));
        Assert.That(merged.Name, Is.EqualTo("slack"));
        Assert.That(merged.Implementation, Is.EqualTo("Slack"));
        Assert.That(merged.Enable, Is.False);
        Assert.That(merged.Tags, Is.EqualTo(new List<int> { 1 }));
        Assert.That(merged.Categories, Is.EqualTo(new List<string> { "tv" }));
    }

    [Test]
    public void Merge_with_present_keys_applies_incoming_and_defaults_lists()
    {
        var existing = new NotificationDefinition
        {
            Name = "old",
            Implementation = "Slack",
            Tags = new List<int> { 1, 2 },
            Categories = new List<string> { "a" },
            OnGrab = false,
        };
        var incoming = new NotificationResource
        {
            Name = "new",
            Implementation = null,
            Tags = null,
            Categories = null,
            OnGrab = true,
            OnDownloadComplete = true,
            OnHealthIssue = false,
        };
        var present = new HashSet<string>
        {
            "name",
            "implementation",
            "tags",
            "categories",
            "onGrab",
            "onDownloadComplete",
            "onHealthIssue",
        };

        var merged = NotificationUpdateMerger.Merge(existing, incoming, present);

        Assert.That(merged.Name, Is.EqualTo("new"));
        Assert.That(merged.Implementation, Is.EqualTo("Slack"));
        Assert.That(merged.Tags, Is.Empty);
        Assert.That(merged.Categories, Is.Empty);
        Assert.That(merged.OnGrab, Is.True);
        Assert.That(merged.OnDownloadComplete, Is.True);
        Assert.That(merged.OnHealthIssue, Is.False);
    }
}
