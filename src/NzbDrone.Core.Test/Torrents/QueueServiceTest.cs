using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class QueueServiceTest
{
    private ICategoryRepository _categoryRepo;
    private IEventAggregator _eventAggregator;
    private ITorrentRepository _torrentRepo;
    private CategoryService _categoryService;
    private QueueService _queueService;

    [SetUp]
    public void SetUp()
    {
        _categoryRepo = Substitute.For<ICategoryRepository>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _torrentRepo = Substitute.For<ITorrentRepository>();

        _categoryService = new CategoryService(_categoryRepo, _eventAggregator, _torrentRepo);
        _queueService = new QueueService(_categoryService);
    }

    [Test]
    public void CanDownload_ignores_extinct_and_stalled_torrents_in_active_downloads_count()
    {
        var category = new Category { Id = 1, Name = "TV", MaxActiveDownloads = 1 };
        _categoryRepo.GetByName("TV").Returns(category);
        _categoryRepo.All().Returns(new[] { category });

        var stalledExtinct = new Torrent
        {
            Id = 1,
            Category = "TV",
            Status = TorrentStatus.StalledNoSeeds,
            IsExtinct = true
        };

        var candidateTorrent = new Torrent { Id = 2, Category = "TV" };

        // Even though category max is 1 and stalledExtinct is present, CanDownload should be true
        var canDownload = _queueService.CanDownload(candidateTorrent, new[] { stalledExtinct });

        Assert.That(canDownload, Is.True);
    }

    [Test]
    public void EvaluateDownloadQueue_bypasses_extinct_active_torrents_to_promote_queued_torrents()
    {
        var category = new Category { Id = 1, Name = "General", MaxActiveDownloads = 1 };
        _categoryRepo.All().Returns(new[] { category });
        _categoryRepo.GetByName("General").Returns(category);

        var extinctActive = new Torrent
        {
            Id = 1,
            Category = "General",
            Status = TorrentStatus.StalledNoSeeds,
            IsExtinct = true
        };

        var queued = new Torrent
        {
            Id = 2,
            Category = "General",
            SortOrder = 1
        };

        // Active torrents contains 1 extinct torrent, limit is 1. The queued torrent should be promoted.
        var promoted = _queueService.EvaluateDownloadQueue(new[] { queued }, new[] { extinctActive }, 1);

        Assert.That(promoted.Select(t => t.Id), Contains.Item(2));
    }
}
