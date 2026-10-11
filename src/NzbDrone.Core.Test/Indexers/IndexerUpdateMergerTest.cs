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
}
