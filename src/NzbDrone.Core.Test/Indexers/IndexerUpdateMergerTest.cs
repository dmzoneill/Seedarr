using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using Seedarr.Api.V1.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class IndexerUpdateMergerTest
{
    [Test]
    public void Merge_indexer_type_change_without_implementation_clears_implementation()
    {
        var existing = new IndexerDefinition
        {
            Name = "idx",
            IndexerType = "torznab",
            Implementation = "OldImpl",
        };
        var incoming = new IndexerDefinition { IndexerType = "newznab" };
        var present = new HashSet<string> { "indexerType" };

        var merged = IndexerUpdateMerger.Merge(existing, incoming, present);

        Assert.That(merged.IndexerType, Is.EqualTo("newznab"));
        Assert.That(merged.Implementation, Is.Null);
        Assert.That(merged.Name, Is.EqualTo("idx"));
    }

    [Test]
    public void Merge_indexer_type_change_with_implementation_present_keeps_incoming_implementation()
    {
        var existing = new IndexerDefinition
        {
            IndexerType = "torznab",
            Implementation = "OldImpl",
        };
        var incoming = new IndexerDefinition { IndexerType = "newznab", Implementation = "NewImpl" };
        var present = new HashSet<string> { "indexerType", "implementation" };

        var merged = IndexerUpdateMerger.Merge(existing, incoming, present);

        Assert.That(merged.Implementation, Is.EqualTo("NewImpl"));
    }

    [Test]
    public void Merge_null_existing_throws()
    {
        Assert.That(
            () => IndexerUpdateMerger.Merge(null!, new IndexerDefinition(), new HashSet<string>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Merge_indexer_type_unchanged_does_not_clear_implementation()
    {
        var existing = new IndexerDefinition
        {
            IndexerType = "torznab",
            Implementation = "Torznab",
        };
        var incoming = new IndexerDefinition { IndexerType = "torznab" };

        var merged = IndexerUpdateMerger.Merge(existing, incoming, new HashSet<string> { "indexerType" });

        Assert.That(merged.Implementation, Is.EqualTo("Torznab"));
    }

    [Test]
    public void Merge_with_all_present_keys_applies_incoming_fields()
    {
        var existing = new IndexerDefinition
        {
            Id = 2,
            Name = "old",
            Implementation = "OldImpl",
            ConfigContract = "OldContract",
            Settings = "{\"k\":1}",
            Enable = false,
            Priority = 1,
            IndexerType = "torznab",
            Url = "http://old",
            ApiKey = "old-key",
            ApiPath = "/old",
            EnableRss = false,
            EnableSearch = false,
            Categories = "1",
            DownloadClientId = 3,
            Tags = new List<int> { 4 },
        };
        var incoming = new IndexerDefinition
        {
            Name = "new",
            Implementation = "NewImpl",
            ConfigContract = "NewContract",
            Settings = "{\"k\":2}",
            Enable = true,
            Priority = 9,
            IndexerType = "newznab",
            Url = "http://new",
            ApiKey = "new-key",
            ApiPath = "/new",
            EnableRss = true,
            EnableSearch = true,
            Categories = "9",
            DownloadClientId = 8,
            Tags = new List<int> { 5, 6 },
        };
        var present = new HashSet<string>
        {
            "name",
            "implementation",
            "configContract",
            "settings",
            "enable",
            "priority",
            "indexerType",
            "url",
            "apiKey",
            "apiPath",
            "enableRss",
            "enableSearch",
            "categories",
            "downloadClientId",
            "tags",
        };

        var merged = IndexerUpdateMerger.Merge(existing, incoming, present);

        Assert.That(merged.Id, Is.EqualTo(2));
        Assert.That(merged.Name, Is.EqualTo("new"));
        Assert.That(merged.Implementation, Is.EqualTo("NewImpl"));
        Assert.That(merged.ConfigContract, Is.EqualTo("NewContract"));
        Assert.That(merged.Settings, Is.EqualTo(incoming.Settings));
        Assert.That(merged.Enable, Is.True);
        Assert.That(merged.Priority, Is.EqualTo(9));
        Assert.That(merged.IndexerType, Is.EqualTo("newznab"));
        Assert.That(merged.Url, Is.EqualTo("http://new"));
        Assert.That(merged.ApiKey, Is.EqualTo("new-key"));
        Assert.That(merged.ApiPath, Is.EqualTo("/new"));
        Assert.That(merged.EnableRss, Is.True);
        Assert.That(merged.EnableSearch, Is.True);
        Assert.That(merged.Categories, Is.EqualTo("9"));
        Assert.That(merged.DownloadClientId, Is.EqualTo(8));
        Assert.That(merged.Tags, Is.EqualTo(new List<int> { 5, 6 }));
    }
}
