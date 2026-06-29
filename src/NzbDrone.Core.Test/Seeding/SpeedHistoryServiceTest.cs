using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class SpeedHistoryServiceTest
{
    private ITorrentService _torrentService;
    private SpeedHistoryService _service;

    [SetUp]
    public void Setup()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _service = new SpeedHistoryService(_torrentService);
    }

    [Test]
    public void GetHistory_should_return_empty_list_initially()
    {
        var history = _service.GetHistory();

        Assert.That(history, Is.Empty);
    }

    [Test]
    public void GetTorrentHistory_should_return_empty_list_for_unknown_torrent()
    {
        var history = _service.GetTorrentHistory(999);

        Assert.That(history, Is.Empty);
    }

    [Test]
    public void RecordSnapshot_should_add_snapshot_to_history()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 1000, Downloaded = 500, Seeders = 5, Leechers = 3, Ratio = 2.0 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot();

        var history = _service.GetHistory();
        Assert.That(history, Has.Count.EqualTo(1));
        Assert.That(history[0].ActiveTorrents, Is.EqualTo(1));
    }

    [Test]
    public void RecordSnapshot_should_calculate_speed_on_second_call()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 1000, Downloaded = 500 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot();

        torrents[0].Uploaded = 2000;
        torrents[0].Downloaded = 600;

        CallRecordSnapshot();

        var history = _service.GetHistory();
        Assert.That(history, Has.Count.EqualTo(2));
        Assert.That(history[1].UploadSpeed, Is.GreaterThanOrEqualTo(0));
        Assert.That(history[1].DownloadSpeed, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void RecordSnapshot_should_record_total_peers()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Seeders = 5, Leechers = 3, Uploaded = 0, Downloaded = 0 },
            new Torrent { Id = 2, Status = TorrentStatus.Downloading, Seeders = 10, Leechers = 7, Uploaded = 0, Downloaded = 0 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot();

        var history = _service.GetHistory();
        Assert.That(history[0].TotalPeers, Is.EqualTo(25));
    }

    [Test]
    public void RecordSnapshot_should_record_average_ratio_of_active_only()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Ratio = 2.0, Uploaded = 0, Downloaded = 0 },
            new Torrent { Id = 2, Status = TorrentStatus.Seeding, Ratio = 4.0, Uploaded = 0, Downloaded = 0 },
            new Torrent { Id = 3, Status = TorrentStatus.Stopped, Ratio = 100.0, Uploaded = 0, Downloaded = 0 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot();

        var history = _service.GetHistory();
        Assert.That(history[0].AverageRatio, Is.EqualTo(3.0));
    }

    [Test]
    public void RecordSnapshot_should_return_zero_ratio_when_no_active()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Stopped, Ratio = 5.0, Uploaded = 0, Downloaded = 0 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot();

        var history = _service.GetHistory();
        Assert.That(history[0].AverageRatio, Is.EqualTo(0));
    }

    [Test]
    public void RecordSnapshot_should_track_per_torrent_snapshots()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 42, Status = TorrentStatus.Seeding, Uploaded = 1000, Downloaded = 500 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot();

        var torrentHistory = _service.GetTorrentHistory(42);
        Assert.That(torrentHistory, Has.Count.EqualTo(1));
    }

    [Test]
    public void RecordSnapshot_should_clean_stale_torrent_snapshots()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 1000, Downloaded = 500 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot();

        Assert.That(_service.GetTorrentHistory(1), Has.Count.EqualTo(1));

        _torrentService.GetAll().Returns(new List<Torrent>());
        CallRecordSnapshot();

        Assert.That(_service.GetTorrentHistory(1), Is.Empty);
    }

    [Test]
    public void RecordSnapshot_should_limit_to_max_snapshots()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 0, Downloaded = 0 }
        };
        _torrentService.GetAll().Returns(torrents);

        for (var i = 0; i < 310; i++)
        {
            torrents[0].Uploaded = i * 100;
            CallRecordSnapshot();
        }

        var history = _service.GetHistory();
        Assert.That(history, Has.Count.EqualTo(300));
    }

    [Test]
    public void RecordSnapshot_should_limit_per_torrent_snapshots()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 0, Downloaded = 0 }
        };
        _torrentService.GetAll().Returns(torrents);

        for (var i = 0; i < 310; i++)
        {
            torrents[0].Uploaded = i * 100;
            CallRecordSnapshot();
        }

        var torrentHistory = _service.GetTorrentHistory(1);
        Assert.That(torrentHistory, Has.Count.EqualTo(300));
    }

    [Test]
    public void RecordSnapshot_should_record_total_uploaded_and_downloaded()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 5000, Downloaded = 2000 },
            new Torrent { Id = 2, Status = TorrentStatus.Stopped, Uploaded = 3000, Downloaded = 1000 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot();

        var history = _service.GetHistory();
        Assert.That(history[0].TotalUploaded, Is.EqualTo(8000));
        Assert.That(history[0].TotalDownloaded, Is.EqualTo(3000));
    }

    [Test]
    public void RecordSnapshot_first_call_should_have_zero_speed()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 5000, Downloaded = 2000 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot();

        var history = _service.GetHistory();
        Assert.That(history[0].UploadSpeed, Is.EqualTo(0));
        Assert.That(history[0].DownloadSpeed, Is.EqualTo(0));
    }

    [Test]
    public void RecordSnapshot_per_torrent_first_call_should_have_zero_speed()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 5000, Downloaded = 2000 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot();

        var torrentHistory = _service.GetTorrentHistory(1);
        Assert.That(torrentHistory[0].UploadSpeed, Is.EqualTo(0));
        Assert.That(torrentHistory[0].DownloadSpeed, Is.EqualTo(0));
    }

    private void CallRecordSnapshot()
    {
        var method = typeof(SpeedHistoryService).GetMethod("RecordSnapshot",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Invoke(_service, null);
    }
}
