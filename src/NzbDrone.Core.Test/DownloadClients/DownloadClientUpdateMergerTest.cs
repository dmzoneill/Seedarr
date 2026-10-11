using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;
using Seedarr.Api.V1.DownloadClients;

namespace NzbDrone.Core.Test.DownloadClients;

[TestFixture]
public class DownloadClientUpdateMergerTest
{
    [Test]
    public void Merge_when_existing_null_throws()
    {
        var incoming = new DownloadClientDefinition { Name = "new" };
        Assert.That(
            () => DownloadClientUpdateMerger.Merge(null!, incoming, new HashSet<string> { "name" }),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Merge_when_incoming_null_throws()
    {
        var existing = new DownloadClientDefinition { Name = "old" };
        Assert.That(
            () => DownloadClientUpdateMerger.Merge(existing, null!, new HashSet<string> { "name" }),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Merge_with_no_present_keys_keeps_existing_values()
    {
        var existing = new DownloadClientDefinition
        {
            Id = 3,
            Name = "keep",
            Host = "10.0.0.1",
            Port = 9091,
            Password = "secret",
            Tags = new List<int> { 1, 2 },
        };
        var incoming = new DownloadClientDefinition
        {
            Name = "replace",
            Host = "evil",
            Port = 1,
            Password = "new",
            Tags = new List<int> { 9 },
        };

        var merged = DownloadClientUpdateMerger.Merge(existing, incoming, new HashSet<string>());

        Assert.That(merged.Id, Is.EqualTo(3));
        Assert.That(merged.Name, Is.EqualTo("keep"));
        Assert.That(merged.Host, Is.EqualTo("10.0.0.1"));
        Assert.That(merged.Port, Is.EqualTo(9091));
        Assert.That(merged.Password, Is.EqualTo("secret"));
        Assert.That(merged.Tags, Is.EqualTo(new List<int> { 1, 2 }));
    }

    [Test]
    public void Merge_with_present_keys_applies_incoming_fields()
    {
        var existing = new DownloadClientDefinition { Name = "old", Host = "a", Port = 1, Enable = false };
        var incoming = new DownloadClientDefinition { Name = "new", Host = "b", Port = 2, Enable = true };
        var present = new HashSet<string> { "name", "host", "port", "enable" };

        var merged = DownloadClientUpdateMerger.Merge(existing, incoming, present);

        Assert.That(merged.Name, Is.EqualTo("new"));
        Assert.That(merged.Host, Is.EqualTo("b"));
        Assert.That(merged.Port, Is.EqualTo(2));
        Assert.That(merged.Enable, Is.True);
    }

    [Test]
    public void Merge_password_mask_keeps_existing_password()
    {
        var existing = new DownloadClientDefinition { Password = "stored" };
        var incoming = new DownloadClientDefinition { Password = DownloadClientUpdateMerger.PasswordMask };

        var merged = DownloadClientUpdateMerger.Merge(
            existing,
            incoming,
            new HashSet<string> { "password" });

        Assert.That(merged.Password, Is.EqualTo("stored"));
    }

    [Test]
    public void Merge_blank_incoming_password_keeps_existing_password()
    {
        var existing = new DownloadClientDefinition { Password = "stored" };
        var incoming = new DownloadClientDefinition { Password = "   " };

        var merged = DownloadClientUpdateMerger.Merge(
            existing,
            incoming,
            new HashSet<string> { "password" });

        Assert.That(merged.Password, Is.EqualTo("stored"));
    }

    [Test]
    public void Merge_new_password_replaces_existing()
    {
        var existing = new DownloadClientDefinition { Password = "stored" };
        var incoming = new DownloadClientDefinition { Password = "updated" };

        var merged = DownloadClientUpdateMerger.Merge(
            existing,
            incoming,
            new HashSet<string> { "password" });

        Assert.That(merged.Password, Is.EqualTo("updated"));
    }

    [Test]
    public void Merge_tags_present_uses_incoming_or_empty_list()
    {
        var existing = new DownloadClientDefinition { Tags = new List<int> { 1 } };
        var incoming = new DownloadClientDefinition { Tags = null };

        var merged = DownloadClientUpdateMerger.Merge(
            existing,
            incoming,
            new HashSet<string> { "tags" });

        Assert.That(merged.Tags, Is.Empty);
    }

    [Test]
    public void Merge_with_extended_fields_applies_incoming_values()
    {
        var existing = new DownloadClientDefinition
        {
            Implementation = "OldImpl",
            ConfigContract = "OldContract",
            Settings = "{\"a\":1}",
            ClientType = "qbittorrent",
            UseSsl = false,
            UrlBase = "/old",
            Username = "old-user",
            Category = "old-cat",
            Priority = 1,
        };
        var incoming = new DownloadClientDefinition
        {
            Implementation = "NewImpl",
            ConfigContract = "NewContract",
            Settings = "{\"b\":2}",
            ClientType = "deluge",
            UseSsl = true,
            UrlBase = "/new",
            Username = "new-user",
            Category = "new-cat",
            Priority = 5,
        };
        var present = new HashSet<string>
        {
            "implementation",
            "configContract",
            "settings",
            "clientType",
            "useSsl",
            "urlBase",
            "username",
            "category",
            "priority",
        };

        var merged = DownloadClientUpdateMerger.Merge(existing, incoming, present);

        Assert.That(merged.Implementation, Is.EqualTo("NewImpl"));
        Assert.That(merged.ConfigContract, Is.EqualTo("NewContract"));
        Assert.That(merged.Settings, Is.EqualTo("{\"b\":2}"));
        Assert.That(merged.ClientType, Is.EqualTo("deluge"));
        Assert.That(merged.UseSsl, Is.True);
        Assert.That(merged.UrlBase, Is.EqualTo("/new"));
        Assert.That(merged.Username, Is.EqualTo("new-user"));
        Assert.That(merged.Category, Is.EqualTo("new-cat"));
        Assert.That(merged.Priority, Is.EqualTo(5));
    }
}
