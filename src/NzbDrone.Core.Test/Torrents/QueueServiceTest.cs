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
    [Test]
    public void CanDownload_ignores_slow_downloading_torrents_under_threshold_when_ignore_slow_enabled()
    {
        var category = new Category { Id = 1, Name = "TV", MaxActiveDownloads = 1 };
        _categoryRepo.GetByName("TV").Returns(category);
        _categoryRepo.All().Returns(new[] { category });

        var slowTorrent = new Torrent
        {
            Id = 1,
            Category = "TV",
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 5 * 1024 // 5 KB/s (< 10 KB/s threshold)
        };

        var candidateTorrent = new Torrent { Id = 2, Category = "TV" };

        var canDownload = _queueService.CanDownload(candidateTorrent, new[] { slowTorrent }, ignoreSlowTorrents: true);

        Assert.That(canDownload, Is.True);
    }

    [Test]
    public void CanDownload_counts_slow_downloading_torrents_when_ignore_slow_disabled()
    {
        var category = new Category { Id = 1, Name = "TV", MaxActiveDownloads = 1 };
        _categoryRepo.GetByName("TV").Returns(category);
        _categoryRepo.All().Returns(new[] { category });

        var slowTorrent = new Torrent
        {
            Id = 1,
            Category = "TV",
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 5 * 1024 // 5 KB/s (< 10 KB/s threshold)
        };

        var candidateTorrent = new Torrent { Id = 2, Category = "TV" };

        var canDownload = _queueService.CanDownload(candidateTorrent, new[] { slowTorrent }, ignoreSlowTorrents: false);

        Assert.That(canDownload, Is.False);
    }

    [Test]
    public void CanDownload_counts_healthy_downloading_torrents_above_threshold()
    {
        var category = new Category { Id = 1, Name = "TV", MaxActiveDownloads = 1 };
        _categoryRepo.GetByName("TV").Returns(category);
        _categoryRepo.All().Returns(new[] { category });

        var fastTorrent = new Torrent
        {
            Id = 1,
            Category = "TV",
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 20 * 1024 // 20 KB/s (>= 10 KB/s threshold)
        };

        var candidateTorrent = new Torrent { Id = 2, Category = "TV" };

        var canDownload = _queueService.CanDownload(candidateTorrent, new[] { fastTorrent }, ignoreSlowTorrents: true);

        Assert.That(canDownload, Is.False);
    }

    [Test]
    public void EvaluateDownloadQueue_bypasses_stalled_and_slow_torrents_to_promote_queued_torrents()
    {
        var category = new Category { Id = 1, Name = "General", MaxActiveDownloads = 2 };
        _categoryRepo.All().Returns(new[] { category });
        _categoryRepo.GetByName("General").Returns(category);

        var stalled = new Torrent
        {
            Id = 1,
            Category = "General",
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 0
        };

        var slow = new Torrent
        {
            Id = 2,
            Category = "General",
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 3 * 1024 // 3 KB/s
        };

        var queued1 = new Torrent { Id = 3, Category = "General", SortOrder = 1 };
        var queued2 = new Torrent { Id = 4, Category = "General", SortOrder = 2 };

        // Global limit = 2. 2 active downloads are stalled/slow. Both queued should be promoted.
        var promoted = _queueService.EvaluateDownloadQueue(new[] { queued1, queued2 }, new[] { stalled, slow }, 2, ignoreSlowTorrents: true);

        Assert.That(promoted.Select(t => t.Id), Is.EqualTo(new[] { 3, 4 }));
    }

    [Test]
    public void EvaluateDownloadQueue_respects_global_limit_when_ignore_slow_disabled()
    {
        var category = new Category { Id = 1, Name = "General", MaxActiveDownloads = 2 };
        _categoryRepo.All().Returns(new[] { category });
        _categoryRepo.GetByName("General").Returns(category);

        var stalled = new Torrent
        {
            Id = 1,
            Category = "General",
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 0
        };

        var slow = new Torrent
        {
            Id = 2,
            Category = "General",
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 3 * 1024 // 3 KB/s
        };

        var queued1 = new Torrent { Id = 3, Category = "General", SortOrder = 1 };
        var queued2 = new Torrent { Id = 4, Category = "General", SortOrder = 2 };

        // With ignoreSlowTorrents = false, stalled and slow torrents consume slots, so no queued torrents are promoted.
        var promoted = _queueService.EvaluateDownloadQueue(new[] { queued1, queued2 }, new[] { stalled, slow }, 2, ignoreSlowTorrents: false);

        Assert.That(promoted, Is.Empty);
    }
}
