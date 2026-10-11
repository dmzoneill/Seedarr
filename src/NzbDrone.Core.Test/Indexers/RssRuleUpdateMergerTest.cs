using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class RssRuleUpdateMergerTest
{
    [Test]
    public void Merge_null_existing_throws()
    {
        Assert.That(
            () => RssRuleUpdateMerger.Merge(null!, new RssRule(), new HashSet<string>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Merge_null_incoming_throws()
    {
        Assert.That(
            () => RssRuleUpdateMerger.Merge(new RssRule(), null!, new HashSet<string>()),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Merge_with_no_present_keys_copies_existing_values()
    {
        var existing = new RssRule
        {
            Id = 4,
            Name = "rule-a",
            MinSeeders = 5,
            IndexerIds = new List<int> { 1, 2 },
            Tags = new List<int> { 9 },
            AllowedResolutions = new List<string> { "1080p" },
        };
        var incoming = new RssRule { Name = "ignored", MinSeeders = 99 };

        var merged = RssRuleUpdateMerger.Merge(existing, incoming, new HashSet<string>());

        Assert.That(merged.Id, Is.EqualTo(4));
        Assert.That(merged.Name, Is.EqualTo("rule-a"));
        Assert.That(merged.MinSeeders, Is.EqualTo(5));
        Assert.That(merged.IndexerIds, Is.EqualTo(new List<int> { 1, 2 }));
        Assert.That(merged.Tags, Is.EqualTo(new List<int> { 9 }));
        Assert.That(merged.AllowedResolutions, Is.EqualTo(new List<string> { "1080p" }));
    }

    [Test]
    public void Merge_with_all_present_keys_applies_incoming_and_null_lists_to_empty()
    {
        var existing = new RssRule
        {
            Name = "old",
            IndexerIds = new List<int> { 1 },
            Tags = new List<int> { 2 },
            AllowedResolutions = new List<string> { "720p" },
        };
        var incoming = new RssRule
        {
            Name = "new",
            IsEnabled = false,
            MustContain = "foo",
            MustNotContain = "bar",
            MinSeeders = 3,
            AllowUnknownSeeders = false,
            Priority = 7,
            MinSizeBytes = 100,
            MaxSizeBytes = 200,
            MaxAgeDays = 30,
            FreeleechOnly = true,
            CategoryId = 12,
            IndexerIds = null,
            Tags = null,
            AllowedResolutions = null,
            AllowedSources = null,
            AllowedCodecs = null,
            SavePath = "/downloads",
            SequentialDownload = true,
            InitialStatus = TorrentStatus.Paused,
        };
        var present = new HashSet<string>
        {
            "name",
            "isEnabled",
            "mustContain",
            "mustNotContain",
            "minSeeders",
            "allowUnknownSeeders",
            "priority",
            "minSizeBytes",
            "maxSizeBytes",
            "maxAgeDays",
            "freeleechOnly",
            "categoryId",
            "indexerIds",
            "tags",
            "allowedResolutions",
            "allowedSources",
            "allowedCodecs",
            "savePath",
            "sequentialDownload",
            "initialStatus",
        };

        var merged = RssRuleUpdateMerger.Merge(existing, incoming, present);

        Assert.That(merged.Name, Is.EqualTo("new"));
        Assert.That(merged.IsEnabled, Is.False);
        Assert.That(merged.MustContain, Is.EqualTo("foo"));
        Assert.That(merged.MustNotContain, Is.EqualTo("bar"));
        Assert.That(merged.MinSeeders, Is.EqualTo(3));
        Assert.That(merged.AllowUnknownSeeders, Is.False);
        Assert.That(merged.Priority, Is.EqualTo(7));
        Assert.That(merged.MinSizeBytes, Is.EqualTo(100));
        Assert.That(merged.MaxSizeBytes, Is.EqualTo(200));
        Assert.That(merged.MaxAgeDays, Is.EqualTo(30));
        Assert.That(merged.FreeleechOnly, Is.True);
        Assert.That(merged.CategoryId, Is.EqualTo(12));
        Assert.That(merged.IndexerIds, Is.Empty);
        Assert.That(merged.Tags, Is.Empty);
        Assert.That(merged.AllowedResolutions, Is.Empty);
        Assert.That(merged.AllowedSources, Is.Empty);
        Assert.That(merged.AllowedCodecs, Is.Empty);
        Assert.That(merged.SavePath, Is.EqualTo("/downloads"));
        Assert.That(merged.SequentialDownload, Is.True);
        Assert.That(merged.InitialStatus, Is.EqualTo(TorrentStatus.Paused));
    }

    [Test]
    public void Merge_tagIds_present_updates_tags()
    {
        var existing = new RssRule { Tags = new List<int> { 1 } };
        var incoming = new RssRule { Tags = new List<int> { 5, 6 } };

        var merged = RssRuleUpdateMerger.Merge(
            existing,
            incoming,
            new HashSet<string> { "tagIds" });

        Assert.That(merged.Tags, Is.EqualTo(new List<int> { 5, 6 }));
    }
}
