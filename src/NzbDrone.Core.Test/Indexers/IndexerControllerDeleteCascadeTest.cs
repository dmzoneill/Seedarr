using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class IndexerControllerDeleteCascadeTest
{
    private IIndexerFactory _indexerFactory;
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITrackerEntryService _trackerEntryService;
    private ITorrentFileParser _torrentFileParser;
    private IDownloadHistoryService _downloadHistoryService;
    private IRssRuleRepository _rssRuleRepository;
    private IndexerController _controller;

    [SetUp]
    public void SetUp()
    {
        _indexerFactory = Substitute.For<IIndexerFactory>();
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _torrentFileParser = Substitute.For<ITorrentFileParser>();
        _downloadHistoryService = Substitute.For<IDownloadHistoryService>();
        _rssRuleRepository = Substitute.For<IRssRuleRepository>();

        _controller = new IndexerController(
            _indexerFactory,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _torrentFileParser,
            _downloadHistoryService,
            rssRuleRepository: _rssRuleRepository);
    }

    [Test]
    public void Delete_cascades_and_removes_indexer_id_from_rss_rules()
    {
        const int indexerIdToDelete = 42;

        var ruleWithIndexer = new RssRule
        {
            Id = 1,
            Name = "Rule 1",
            IndexerIds = new List<int> { 10, indexerIdToDelete, 20 }
        };

        var ruleWithoutIndexer = new RssRule
        {
            Id = 2,
            Name = "Rule 2",
            IndexerIds = new List<int> { 5, 99 }
        };

        _rssRuleRepository.All().Returns(new List<RssRule> { ruleWithIndexer, ruleWithoutIndexer });

        _controller.Delete(indexerIdToDelete);

        _indexerFactory.Received(1).Delete(indexerIdToDelete);
        _rssRuleRepository.Received(1).Update(Arg.Is<RssRule>(r =>
            r.Id == 1 &&
            !r.IndexerIds.Contains(indexerIdToDelete) &&
            r.IndexerIds.Contains(10) &&
            r.IndexerIds.Contains(20)));
        _rssRuleRepository.DidNotReceive().Update(Arg.Is<RssRule>(r => r.Id == 2));
    }
}
