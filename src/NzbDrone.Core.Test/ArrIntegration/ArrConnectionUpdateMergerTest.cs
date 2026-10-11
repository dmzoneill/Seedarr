using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using Seedarr.Api.V1.ArrIntegration;

namespace NzbDrone.Core.Test.ArrIntegration;

[TestFixture]
public class ArrConnectionUpdateMergerTest
{
    [Test]
    public void Merge_partial_update_keeps_unmentioned_fields()
    {
        var existing = new ArrConnectionDefinition
        {
            Name = "sonarr",
            Url = "http://localhost:8989",
            ApiKey = "key-a",
            SyncEnabled = true,
            Tags = new List<int> { 4 },
        };
        var incoming = new ArrConnectionDefinition
        {
            Name = "ignored",
            Url = "http://evil",
            SyncEnabled = false,
        };
        var present = new HashSet<string> { "syncEnabled" };

        var merged = ArrConnectionUpdateMerger.Merge(existing, incoming, present);

        Assert.That(merged.Name, Is.EqualTo("sonarr"));
        Assert.That(merged.Url, Is.EqualTo("http://localhost:8989"));
        Assert.That(merged.ApiKey, Is.EqualTo("key-a"));
        Assert.That(merged.SyncEnabled, Is.False);
        Assert.That(merged.Tags, Is.EqualTo(new List<int> { 4 }));
    }

    [Test]
    public void Merge_tags_present_uses_incoming_or_empty()
    {
        var existing = new ArrConnectionDefinition { Tags = new List<int> { 1, 2 } };
        var incoming = new ArrConnectionDefinition { Tags = null };

        var merged = ArrConnectionUpdateMerger.Merge(
            existing,
            incoming,
            new HashSet<string> { "tags" });

        Assert.That(merged.Tags, Is.Empty);
    }

    [Test]
    public void Merge_null_incoming_throws()
    {
        Assert.That(
            () => ArrConnectionUpdateMerger.Merge(new ArrConnectionDefinition(), null!, new HashSet<string>()),
            Throws.TypeOf<ArgumentNullException>());
    }
}
