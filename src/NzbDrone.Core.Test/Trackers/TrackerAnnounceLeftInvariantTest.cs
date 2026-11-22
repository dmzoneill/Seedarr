using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Metrics;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Test.Trackers;

[TestFixture]
public class TrackerAnnounceLeftInvariantTest
{
    private ITrackerEntryService _trackerEntryService;
    private IMultiTrackerManager _multiTracker;
    private ITorrentFileService _torrentFileService;
    private TrackerAnnounceService _service;
    private List<TrackerAnnounceRequest> _requests;

    [SetUp]
    public void Setup()
    {
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _multiTracker = Substitute.For<IMultiTrackerManager>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        var config = Substitute.For<IConfigService>();
        config.ListeningPort.Returns(51413);
        config.AnnounceIntervalSeconds.Returns(1800);

        _service = new TrackerAnnounceService(
            _trackerEntryService,
            _multiTracker,
            Substitute.For<IPeerDiscoveryService>(),
            Substitute.For<ITorrentEventLogService>(),
            config,
            Substitute.For<ITrackerMetricService>(),
            eventAggregator: Substitute.For<IEventAggregator>(),
            torrentService: Substitute.For<ITorrentService>(),
            vpnKillSwitchService: Substitute.For<IVpnKillSwitchService>(),
            torrentFileService: _torrentFileService);

        _requests = new List<TrackerAnnounceRequest>();
        _multiTracker.Announce(Arg.Any<TrackerAnnounceRequest>(), Arg.Any<List<List<string>>>())
            .Returns(ci =>
            {
                _requests.Add(ci.Arg<TrackerAnnounceRequest>());
                return new TrackerAnnounceResponse { Success = true, Interval = 1800 };
            });
        _multiTracker.AnnounceAsync(
                Arg.Any<TrackerAnnounceRequest>(),
                Arg.Any<List<List<string>>>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(_multiTracker.Announce(
                ci.ArgAt<TrackerAnnounceRequest>(0),
                ci.ArgAt<List<List<string>>>(1))));
    }

    [Test]
    public void Selective_download_keeps_left_at_zero_after_completed()
    {
        var files = SelectiveFiles(wanted: 400_000, unwanted: 600_000);
        var torrent = SeedingTorrent(files, downloaded: 400_000, progress: 1.0);
        StubTracker(torrent.Id);

        _service.Handle(new TorrentFinishedEvent(torrent));
        _service.AnnounceTorrent(torrent, force: true);

        Assert.That(_requests, Has.Count.EqualTo(2));
        Assert.That(_requests[0].Event, Is.EqualTo(AnnounceEvent.Completed));
        Assert.That(_requests[0].Left, Is.EqualTo(0));
        Assert.That(_requests[1].Event, Is.Not.EqualTo(AnnounceEvent.Completed));
        Assert.That(_requests[1].Left, Is.EqualTo(0));
        Assert.That(_requests[1].Left, Is.EqualTo(_requests[0].Left));
    }

    [Test]
    public void Partial_selection_reports_left_against_wanted_bytes_on_every_announce()
    {
        var files = SelectiveFiles(wanted: 400_000, unwanted: 600_000);
        var torrent = SeedingTorrent(files, downloaded: 100_000, progress: 0.25);
        torrent.Files = null;
        _torrentFileService.GetByTorrentId(torrent.Id).Returns(files);
        StubTracker(torrent.Id);

        _service.AnnounceTorrent(torrent, force: true);
        _service.AnnounceTracker(torrent, TrackerFor(torrent.Id), force: true);
        _service.AnnounceTorrent(torrent, force: true);

        Assert.That(_requests, Has.Count.EqualTo(3));
        Assert.That(_requests.Select(r => r.Left).Distinct().ToArray(), Is.EqualTo(new[] { 300_000L }));
        Assert.That(_requests.All(r => r.Left != torrent.TotalSize - torrent.Downloaded), Is.True);
    }

    [Test]
    public void Left_matches_wanted_minus_downloaded_for_varied_selections()
    {
        var cases = new[]
        {
            (wanted: 1_000L, unwanted: 0L, downloaded: 0L, progress: 0.0, expected: 1_000L),
            (wanted: 1_000L, unwanted: 9_000L, downloaded: 250L, progress: 0.25, expected: 750L),
            (wanted: 5_000L, unwanted: 5_000L, downloaded: 5_000L, progress: 1.0, expected: 0L),
            (wanted: 8_000L, unwanted: 2_000L, downloaded: 7_999L, progress: 0.999875, expected: 1L),
            (wanted: 100L, unwanted: 900L, downloaded: 100L, progress: 0.5, expected: 0L)
        };

        foreach (var sample in cases)
        {
            _requests.Clear();
            var files = SelectiveFiles(sample.wanted, sample.unwanted);
            var torrent = SeedingTorrent(files, sample.downloaded, sample.progress);
            torrent.Id = 80 + _requests.Count;
            StubTracker(torrent.Id);

            _service.AnnounceTorrent(torrent, force: true);
            _service.AnnounceTorrent(torrent, force: true);

            Assert.That(_requests, Has.Count.EqualTo(2), $"wanted {sample.wanted} downloaded {sample.downloaded}");
            Assert.That(_requests[0].Left, Is.EqualTo(sample.expected));
            Assert.That(_requests[1].Left, Is.EqualTo(_requests[0].Left));
        }
    }

    private void StubTracker(int torrentId)
    {
        _trackerEntryService.GetByTorrentId(torrentId).Returns(new List<TrackerEntry> { TrackerFor(torrentId) });
    }

    private static TrackerEntry TrackerFor(int torrentId)
    {
        return new TrackerEntry
        {
            Id = torrentId,
            TorrentId = torrentId,
            Url = "http://127.0.0.1:6969/announce",
            Enabled = true,
            Tier = 0
        };
    }

    private static Torrent SeedingTorrent(List<TorrentFile> files, long downloaded, double progress)
    {
        var total = files.Sum(f => f.Size);
        return new Torrent
        {
            Id = 70,
            Name = "Selective",
            InfoHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Status = TorrentStatus.Seeding,
            TotalSize = total,
            Downloaded = downloaded,
            Progress = progress,
            Files = files
        };
    }

    private static List<TorrentFile> SelectiveFiles(long wanted, long unwanted)
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile { ByteOffset = 0, Size = wanted, Wanted = true }
        };
        if (unwanted > 0)
        {
            files.Add(new TorrentFile { ByteOffset = wanted, Size = unwanted, Wanted = false });
        }

        return files;
    }
}
