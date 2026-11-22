using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Seeding.Distribution;
using NzbDrone.Core.Seeding.Scheduling;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class DownloadSimulationInvariantTest
{
    [Test]
    public void Unlimited_download_stays_inside_the_torrent_and_a_client_realistic_rate()
    {
        var policy = CreatePolicy();
        var torrent = TorrentOf(1, 700L * 1024 * 1024);
        var torrents = new List<Torrent> { torrent };

        policy.ProcessDownloading(torrents, Unlimited(), TimeSpan.FromSeconds(1));

        Assert.That(torrent.Downloaded, Is.GreaterThan(0));
        Assert.That(torrent.Downloaded, Is.LessThanOrEqualTo(torrent.TotalSize));
        Assert.That(torrent.Downloaded, Is.LessThanOrEqualTo(SpeedPolicy.DefaultUnlimitedDownloadPerTorrentBps));
        Assert.That(torrent.Downloaded, Is.Not.EqualTo(1_000_000_000L));
        Assert.That(torrent.Progress, Is.InRange(0.0, 1.0));
        Assert.That(torrent.DownloadSpeed, Is.LessThanOrEqualTo(SpeedPolicy.DefaultUnlimitedDownloadPerTorrentBps));
    }

    [Test]
    public void Unlimited_download_across_hundreds_of_torrents_stays_under_the_aggregate_ceiling()
    {
        const int count = 200;
        var policy = CreatePolicy();
        var torrents = new List<Torrent>();
        for (var i = 0; i < count; i++)
        {
            torrents.Add(TorrentOf(i + 1, 2L * 1024 * 1024 * 1024));
        }

        policy.BeginSharedDownloadBudget(Unlimited());
        policy.ProcessDownloading(torrents, Unlimited(), TimeSpan.FromSeconds(1));
        policy.EndSharedDownloadBudget();

        var total = torrents.Sum(t => t.Downloaded);
        Assert.That(total, Is.EqualTo(SpeedPolicy.DefaultUnlimitedDownloadAggregateBps));
        Assert.That(total, Is.LessThan(1_000_000_000L));
        Assert.That(torrents.All(t => t.Downloaded <= SpeedPolicy.DefaultUnlimitedDownloadPerTorrentBps), Is.True);
        Assert.That(torrents.All(t => t.Downloaded <= t.TotalSize), Is.True);
    }

    [Test]
    public void Last_tick_does_not_download_past_the_selected_size()
    {
        var policy = CreatePolicy();
        var torrent = TorrentOf(1, 1_000_000);
        torrent.Downloaded = 990_000;
        var torrents = new List<Torrent> { torrent };

        policy.ProcessDownloading(torrents, Cap(1_000_000), TimeSpan.FromSeconds(1));

        Assert.That(torrent.Downloaded, Is.EqualTo(1_000_000));
        Assert.That(torrent.Progress, Is.EqualTo(1.0));

        policy.ProcessDownloading(torrents, Cap(1_000_000), TimeSpan.FromSeconds(1));

        Assert.That(torrent.Downloaded, Is.EqualTo(1_000_000));
        Assert.That(torrent.Progress, Is.EqualTo(1.0));
    }

    [Test]
    public void Selective_download_clamps_to_the_wanted_size()
    {
        var policy = CreatePolicy();
        var torrent = TorrentOf(1, 10_000);
        torrent.Files = new List<TorrentFile>
        {
            new TorrentFile { ByteOffset = 0, Size = 2_000, Wanted = true },
            new TorrentFile { ByteOffset = 2_000, Size = 8_000, Wanted = false }
        };
        var torrents = new List<Torrent> { torrent };

        policy.ProcessDownloading(torrents, Cap(50_000), TimeSpan.FromSeconds(1));

        Assert.That(torrent.Downloaded, Is.EqualTo(2_000));
        Assert.That(torrent.Progress, Is.EqualTo(1.0));
        Assert.That(torrent.Downloaded, Is.LessThan(torrent.TotalSize));
    }

    [Test]
    public void Download_cap_of_zero_transfers_nothing_for_downloaders_and_residual_seeders()
    {
        var policy = CreatePolicy();
        var downloader = TorrentOf(1, 50_000_000);
        var seeder = IncompleteSeeder(2, 50_000_000, 10_000_000);
        var limits = Cap(0);

        policy.BeginSharedDownloadBudget(limits);
        policy.ProcessDownloading(new List<Torrent> { downloader }, limits, TimeSpan.FromSeconds(1));
        policy.ProcessSeeding(new List<Torrent> { seeder }, limits, TimeSpan.FromSeconds(1));
        policy.EndSharedDownloadBudget();

        Assert.That(downloader.Downloaded, Is.EqualTo(0));
        Assert.That(seeder.Downloaded, Is.EqualTo(10_000_000));
        Assert.That(downloader.DownloadSpeed, Is.EqualTo(0));
    }

    [Test]
    public void Global_download_cap_holds_across_hundreds_of_downloading_and_threshold_seeders()
    {
        const long cap = 1_000_000;
        const int downloaders = 100;
        const int seeders = 300;
        var policy = CreatePolicy();
        var downloading = new List<Torrent>();
        var seeding = new List<Torrent>();

        for (var i = 0; i < downloaders; i++)
        {
            downloading.Add(TorrentOf(i + 1, 5L * 1024 * 1024 * 1024));
        }

        for (var i = 0; i < seeders; i++)
        {
            seeding.Add(IncompleteSeeder(1_000 + i, 5L * 1024 * 1024 * 1024, 1L * 1024 * 1024 * 1024));
        }

        var limits = Cap(cap);
        var beforeSeeders = seeding.Sum(t => t.Downloaded);

        policy.BeginSharedDownloadBudget(limits);
        policy.ProcessDownloading(downloading, limits, TimeSpan.FromSeconds(1));
        policy.ProcessSeeding(seeding, limits, TimeSpan.FromSeconds(1));
        policy.EndSharedDownloadBudget();

        var downloaderDelta = downloading.Sum(t => t.Downloaded);
        var seederDelta = seeding.Sum(t => t.Downloaded) - beforeSeeders;

        Assert.That(downloaderDelta, Is.GreaterThan(cap / 2));
        Assert.That(seederDelta, Is.EqualTo(0));
        Assert.That(downloaderDelta + seederDelta, Is.LessThanOrEqualTo(cap));
        Assert.That(downloading.All(t => t.Downloaded <= t.TotalSize), Is.True);
        Assert.That(seeding.All(t => t.Downloaded <= t.TotalSize), Is.True);
    }

    [Test]
    public void Residual_download_obeys_per_torrent_tag_and_category_limits()
    {
        var tags = Substitute.For<ITagService>();
        tags.GetAll().Returns(new List<Tag>
        {
            new Tag { Id = 7, DownloadLimitKbps = 20 }
        });
        var categories = Substitute.For<ICategoryService>();
        categories.GetByName("linux").Returns(new Category
        {
            Name = "linux",
            DefaultDownloadLimit = 30
        });

        var policy = CreatePolicy(tags, categories);
        var limits = Cap(10_000_000);

        var perTorrent = IncompleteSeeder(1, 100_000_000, 1_000_000);
        perTorrent.DownloadLimit = 10;
        var tagged = IncompleteSeeder(2, 100_000_000, 1_000_000);
        tagged.TagIds = new List<int> { 7 };
        var categorized = IncompleteSeeder(3, 100_000_000, 1_000_000);
        categorized.Category = "linux";

        policy.BeginSharedDownloadBudget(limits);
        policy.ProcessSeeding(new List<Torrent> { perTorrent }, limits, TimeSpan.FromSeconds(1));
        policy.EndSharedDownloadBudget();

        policy.BeginSharedDownloadBudget(limits);
        policy.ProcessSeeding(new List<Torrent> { tagged }, limits, TimeSpan.FromSeconds(1));
        policy.EndSharedDownloadBudget();

        policy.BeginSharedDownloadBudget(limits);
        policy.ProcessSeeding(new List<Torrent> { categorized }, limits, TimeSpan.FromSeconds(1));
        policy.EndSharedDownloadBudget();

        Assert.That(perTorrent.Downloaded - 1_000_000, Is.EqualTo(10 * 1024));
        Assert.That(tagged.Downloaded - 1_000_000, Is.EqualTo(20 * 1024));
        Assert.That(categorized.Downloaded - 1_000_000, Is.EqualTo(30 * 1024));
    }

    [Test]
    public void Unlimited_residual_download_does_not_move_progress_backwards()
    {
        var policy = CreatePolicy();
        var torrent = IncompleteSeeder(1, 100_000_000, 40_000_000);
        var before = torrent.Downloaded;
        var beforeProgress = torrent.Progress;

        policy.ProcessSeeding(new List<Torrent> { torrent }, Unlimited(), TimeSpan.FromSeconds(1));

        Assert.That(torrent.Downloaded, Is.GreaterThan(before));
        Assert.That(torrent.Downloaded, Is.LessThanOrEqualTo(torrent.TotalSize));
        Assert.That(torrent.Downloaded - before, Is.LessThanOrEqualTo(SpeedPolicy.DefaultUnlimitedDownloadPerTorrentBps));
        Assert.That(torrent.Progress, Is.GreaterThanOrEqualTo(beforeProgress));
        Assert.That(torrent.Progress, Is.LessThanOrEqualTo(1.0));
    }

    [Test]
    public void Spare_global_budget_is_shared_with_residual_seeders_without_exceeding_the_cap()
    {
        const long cap = 1_000_000;
        var policy = CreatePolicy();
        var downloaders = new List<Torrent>();
        for (var i = 0; i < 10; i++)
        {
            var torrent = TorrentOf(i + 1, 5L * 1024 * 1024 * 1024);
            torrent.DownloadLimit = 10;
            downloaders.Add(torrent);
        }

        var seeders = new List<Torrent>();
        for (var i = 0; i < 10; i++)
        {
            seeders.Add(IncompleteSeeder(100 + i, 5L * 1024 * 1024 * 1024, 1L * 1024 * 1024 * 1024));
        }

        var before = seeders.Sum(t => t.Downloaded);
        var limits = Cap(cap);
        policy.BeginSharedDownloadBudget(limits);
        policy.ProcessDownloading(downloaders, limits, TimeSpan.FromSeconds(1));
        policy.ProcessSeeding(seeders, limits, TimeSpan.FromSeconds(1));
        policy.EndSharedDownloadBudget();

        var downloaderDelta = downloaders.Sum(t => t.Downloaded);
        var seederDelta = seeders.Sum(t => t.Downloaded) - before;

        Assert.That(downloaderDelta, Is.EqualTo(10 * 10 * 1024));
        Assert.That(seederDelta, Is.GreaterThan(0));
        Assert.That(downloaderDelta + seederDelta, Is.LessThanOrEqualTo(cap));
    }

    [Test]
    public void Downloaded_and_progress_stay_monotonic_and_within_selected_size()
    {
        var rng = new Random(1388);
        var caps = new[] { 0L, 50_000L, 1_000_000L, SpeedLimits.Unlimited };

        for (var sample = 0; sample < 24; sample++)
        {
            var total = rng.Next(50_000, 5_000_000);
            var selective = sample % 2 == 0;
            var wanted = selective ? rng.Next(10_000, total) : total;
            var start = rng.Next(0, Math.Max(1, wanted));
            var cap = caps[sample % caps.Length];
            var policy = CreatePolicy(variationMin: 0.5, variationMax: 1.5);
            var torrent = TorrentOf(sample + 1, total);
            torrent.Downloaded = start;
            torrent.Progress = wanted == 0 ? 0 : Math.Min(0.999999, (double)start / wanted);
            if (selective)
            {
                torrent.Files = new List<TorrentFile>
                {
                    new TorrentFile { ByteOffset = 0, Size = wanted, Wanted = true },
                    new TorrentFile { ByteOffset = wanted, Size = total - wanted, Wanted = false }
                };
            }

            var torrents = new List<Torrent> { torrent };
            var limits = new SpeedLimits { MaxUploadSpeed = cap, MaxDownloadSpeed = cap };
            var previousDownloaded = torrent.Downloaded;
            var previousProgress = torrent.Progress;

            for (var tick = 0; tick < 6; tick++)
            {
                policy.BeginSharedDownloadBudget(limits);
                policy.ProcessDownloading(torrents, limits, TimeSpan.FromMilliseconds(rng.Next(200, 2500)));
                policy.EndSharedDownloadBudget();

                Assert.That(torrent.Downloaded, Is.GreaterThanOrEqualTo(previousDownloaded), $"sample {sample} tick {tick} downloaded moved backwards");
                Assert.That(torrent.Downloaded, Is.LessThanOrEqualTo(wanted), $"sample {sample} tick {tick} exceeded selected size");
                Assert.That(torrent.Progress, Is.GreaterThanOrEqualTo(previousProgress), $"sample {sample} tick {tick} progress moved backwards");
                Assert.That(torrent.Progress, Is.LessThanOrEqualTo(1.0));
                if (cap == 0)
                {
                    Assert.That(torrent.Downloaded, Is.EqualTo(previousDownloaded));
                }

                previousDownloaded = torrent.Downloaded;
                previousProgress = torrent.Progress;
            }
        }
    }

    private static SpeedPolicy CreatePolicy(
        ITagService tags = null,
        ICategoryService categories = null,
        double variationMin = 1.0,
        double variationMax = 1.0)
    {
        var config = Substitute.For<IConfigService>();
        config.DownloadDistributionAlgorithm.Returns("Equal");
        config.UploadDistributionAlgorithm.Returns("Equal");
        config.DownloadDistributionSpreadPercentage.Returns(100);
        config.UploadDistributionSpreadPercentage.Returns(100);
        config.DownloadRedistributionMode.Returns("tick");
        config.UploadRedistributionMode.Returns("tick");
        config.SpeedVariationMin.Returns(variationMin);
        config.SpeedVariationMax.Returns(variationMax);
        config.DownloadThresholdPercent.Returns(100);
        config.SeederUploadActivityProbability.Returns(1.0);
        config.ProtocolOverheadFactor.Returns(0.0);
        config.SwarmIntelligenceEnabled.Returns(false);

        var manager = new SpeedDistributionManager(
            new ISpeedDistributor[] { new EqualDistributor(), new ParetoDistributor() },
            config);

        var stop = Substitute.For<IStopPolicy>();
        stop.SelectDownloadStoppedTorrents(Arg.Any<List<Torrent>>()).Returns(new HashSet<int>());
        stop.SelectStoppedTorrents(Arg.Any<List<Torrent>>()).Returns(new HashSet<int>());

        return new SpeedPolicy(
            manager,
            Substitute.For<ISpeedScheduler>(),
            config,
            Substitute.For<ITorrentEventLogService>(),
            Substitute.For<ITorrentStateMachine>(),
            stop,
            new RandomNumberGenerator(1384),
            categoryService: categories,
            tagService: tags);
    }

    private static Torrent TorrentOf(int id, long size)
    {
        return new Torrent
        {
            Id = id,
            Status = TorrentStatus.Downloading,
            TotalSize = size,
            Downloaded = 0,
            Progress = 0,
            Priority = 1,
            Leechers = 1,
            InfoHash = "dl" + id
        };
    }

    private static Torrent IncompleteSeeder(int id, long size, long downloaded)
    {
        return new Torrent
        {
            Id = id,
            Status = TorrentStatus.Seeding,
            TotalSize = size,
            Downloaded = downloaded,
            Progress = size > 0 ? (double)downloaded / size : 0,
            Priority = 1,
            Leechers = 1,
            SeedingTime = 300,
            InfoHash = "seed" + id
        };
    }

    private static SpeedLimits Cap(long bytesPerSecond)
    {
        return new SpeedLimits
        {
            MaxUploadSpeed = bytesPerSecond,
            MaxDownloadSpeed = bytesPerSecond
        };
    }

    private static SpeedLimits Unlimited()
    {
        return new SpeedLimits
        {
            MaxUploadSpeed = SpeedLimits.Unlimited,
            MaxDownloadSpeed = SpeedLimits.Unlimited
        };
    }
}
