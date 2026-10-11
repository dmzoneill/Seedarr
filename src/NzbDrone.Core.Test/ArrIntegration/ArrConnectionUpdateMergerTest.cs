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

    [Test]
    public void Merge_null_existing_throws()
    {
        Assert.That(
            () => ArrConnectionUpdateMerger.Merge(null!, new ArrConnectionDefinition(), new HashSet<string>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Merge_with_all_present_keys_applies_incoming_fields()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 7,
            Name = "old",
            Implementation = "OldImpl",
            ConfigContract = "OldContract",
            Settings = "{\"a\":1}",
            Enable = false,
            Priority = 1,
            Url = "http://old",
            ApiKey = "old-key",
            ArrType = "sonarr",
            SyncIntervalMinutes = 10,
            SyncEnabled = false,
            EnableAutomaticAdd = false,
            WebhookEnabled = false,
            WebhookHost = "old-host",
            AcceptInvalidCertificates = false,
            Category = "old-cat",
            SavePath = "/old",
            Tags = new List<int> { 1 },
        };
        var incoming = new ArrConnectionDefinition
        {
            Name = "new",
            Implementation = "NewImpl",
            ConfigContract = "NewContract",
            Settings = "{\"b\":2}",
            Enable = true,
            Priority = 9,
            Url = "http://new",
            ApiKey = "new-key",
            ArrType = "radarr",
            SyncIntervalMinutes = 30,
            SyncEnabled = true,
            EnableAutomaticAdd = true,
            WebhookEnabled = true,
            WebhookHost = "new-host",
            AcceptInvalidCertificates = true,
            Category = "new-cat",
            SavePath = "/new",
            Tags = new List<int> { 2, 3 },
        };
        var present = new HashSet<string>
        {
            "name",
            "implementation",
            "configContract",
            "settings",
            "enable",
            "priority",
            "url",
            "apiKey",
            "arrType",
            "syncIntervalMinutes",
            "syncEnabled",
            "enableAutomaticAdd",
            "webhookEnabled",
            "webhookHost",
            "acceptInvalidCertificates",
            "category",
            "savePath",
            "tags",
        };

        var merged = ArrConnectionUpdateMerger.Merge(existing, incoming, present);

        Assert.That(merged.Id, Is.EqualTo(7));
        Assert.That(merged.Name, Is.EqualTo("new"));
        Assert.That(merged.Implementation, Is.EqualTo("NewImpl"));
        Assert.That(merged.ConfigContract, Is.EqualTo("NewContract"));
        Assert.That(merged.Settings, Is.EqualTo(incoming.Settings));
        Assert.That(merged.Enable, Is.True);
        Assert.That(merged.Priority, Is.EqualTo(9));
        Assert.That(merged.Url, Is.EqualTo("http://new"));
        Assert.That(merged.ApiKey, Is.EqualTo("new-key"));
        Assert.That(merged.ArrType, Is.EqualTo("radarr"));
        Assert.That(merged.SyncIntervalMinutes, Is.EqualTo(30));
        Assert.That(merged.SyncEnabled, Is.True);
        Assert.That(merged.EnableAutomaticAdd, Is.True);
        Assert.That(merged.WebhookEnabled, Is.True);
        Assert.That(merged.WebhookHost, Is.EqualTo("new-host"));
        Assert.That(merged.AcceptInvalidCertificates, Is.True);
        Assert.That(merged.Category, Is.EqualTo("new-cat"));
        Assert.That(merged.SavePath, Is.EqualTo("/new"));
        Assert.That(merged.Tags, Is.EqualTo(new List<int> { 2, 3 }));
    }
}
