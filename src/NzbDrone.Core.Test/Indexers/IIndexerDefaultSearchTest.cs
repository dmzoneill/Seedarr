using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class IIndexerDefaultSearchTest
{
    [Test]
    public void Default_search_checks_the_token_then_uses_the_query_search()
    {
        var indexer = new LegacyIndexer();
        var definition = new IndexerDefinition { Name = "Legacy" };
        var query = new SearchQuery { Query = "linux" };

        var results = ((IIndexer)indexer).Search(definition, query, CancellationToken.None);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Title, Is.EqualTo("linux"));
    }

    [Test]
    public void Default_search_throws_when_the_token_is_already_cancelled()
    {
        var indexer = new LegacyIndexer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => ((IIndexer)indexer)
            .Search(new IndexerDefinition(), new SearchQuery(), cts.Token));
        Assert.That(indexer.Searches, Is.EqualTo(0));
    }

    private sealed class LegacyIndexer : IIndexer
    {
        public int Searches { get; private set; }

        public string Name => "Legacy";

        public string IndexerType => "Legacy";

        public bool TestConnection(IndexerDefinition definition) => true;

        public IndexerTestResult TestConnectionDetailed(IndexerDefinition definition) => new() { Success = true };

        public byte[] FetchTorrentByHash(IndexerDefinition definition, string infoHash) => null;

        public List<ReleaseInfo> Search(
            IndexerDefinition definition,
            string query,
            string category = null,
            int offset = 0,
            int limit = 50) => new();

        public List<ReleaseInfo> Search(IndexerDefinition definition, SearchQuery searchQuery)
        {
            Searches++;
            return new List<ReleaseInfo> { new() { Title = searchQuery?.Query } };
        }

        public List<ReleaseInfo> Search(IndexerDefinition definition, TorznabSearchCriteria criteria) => new();
    }
}
