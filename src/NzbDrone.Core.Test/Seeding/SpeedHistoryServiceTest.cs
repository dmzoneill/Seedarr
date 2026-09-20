using System;
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

    [Test]
    public void RecordSnapshot_should_not_spike_aggregate_speed_when_adding_torrent_with_large_uploaded_bytes()
    {
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 100_000_000, Downloaded = 0 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot(baseTime);

        // Tick 1: Torrent 1 uploads 5 MB over 5 seconds (1 MB/s)
        baseTime = baseTime.AddSeconds(5);
        torrents[0].Uploaded = 105_000_000;
        CallRecordSnapshot(baseTime);

        var history = _service.GetHistory();
        Assert.That(history[1].UploadSpeed, Is.EqualTo(1_000_000));

        // Now user adds/imports Torrent 2 with 50 GB pre-existing uploaded bytes
        // Torrent 1 uploads another 5 MB
        baseTime = baseTime.AddSeconds(5);
        torrents[0].Uploaded = 110_000_000;
        torrents.Add(new Torrent { Id = 2, Status = TorrentStatus.Seeding, Uploaded = 50_000_000_000L, Downloaded = 0 });
        CallRecordSnapshot(baseTime);

        history = _service.GetHistory();
        Assert.That(history, Has.Count.EqualTo(3));
        // Aggregate speed should only be the 5 MB from Torrent 1 (1 MB/s), NOT 10+ GB/s
        Assert.That(history[2].UploadSpeed, Is.EqualTo(1_000_000));

        // And Torrent 2's own per-torrent speed should be 0 on its first tick
        var torrent2History = _service.GetTorrentHistory(2);
        Assert.That(torrent2History, Has.Count.EqualTo(1));
        Assert.That(torrent2History[0].UploadSpeed, Is.EqualTo(0));
    }

    [Test]
    public void RecordSnapshot_should_retain_snapshot_history_when_torrent_is_paused()
    {
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 1000, Downloaded = 500 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot(baseTime);

        baseTime = baseTime.AddSeconds(5);
        torrents[0].Uploaded = 6000;
        CallRecordSnapshot(baseTime);

        var historyBeforePause = _service.GetTorrentHistory(1);
        Assert.That(historyBeforePause, Has.Count.EqualTo(2));

        // Pause the torrent
        baseTime = baseTime.AddSeconds(5);
        torrents[0].Status = TorrentStatus.Paused;
        CallRecordSnapshot(baseTime);

        // History must NOT be wiped
        var historyAfterPause = _service.GetTorrentHistory(1);
        Assert.That(historyAfterPause, Has.Count.EqualTo(3));
        Assert.That(historyAfterPause[2].UploadSpeed, Is.EqualTo(0));
        Assert.That(historyAfterPause[2].DownloadSpeed, Is.EqualTo(0));

        // Resume the torrent and upload 5000 bytes over 5s
        baseTime = baseTime.AddSeconds(5);
        torrents[0].Status = TorrentStatus.Seeding;
        torrents[0].Uploaded = 11000;
        CallRecordSnapshot(baseTime);

        var historyAfterResume = _service.GetTorrentHistory(1);
        Assert.That(historyAfterResume, Has.Count.EqualTo(4));
        Assert.That(historyAfterResume[3].UploadSpeed, Is.EqualTo(1000));
    }

    [Test]
    public void RecordSnapshot_should_normalize_speed_accurately_with_variable_time_deltas()
    {
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 0, Downloaded = 0 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot(baseTime);

        // 10 second delay, 100,000 bytes uploaded -> 10,000 B/s
        baseTime = baseTime.AddSeconds(10);
        torrents[0].Uploaded = 100_000;
        CallRecordSnapshot(baseTime);

        var history = _service.GetHistory();
        Assert.That(history[1].UploadSpeed, Is.EqualTo(10_000));
        var torrentHistory = _service.GetTorrentHistory(1);
        Assert.That(torrentHistory[1].UploadSpeed, Is.EqualTo(10_000));

        // 2 second delay, 50,000 bytes uploaded -> 25,000 B/s
        baseTime = baseTime.AddSeconds(2);
        torrents[0].Uploaded = 150_000;
        CallRecordSnapshot(baseTime);

        history = _service.GetHistory();
        Assert.That(history[2].UploadSpeed, Is.EqualTo(25_000));
        torrentHistory = _service.GetTorrentHistory(1);
        Assert.That(torrentHistory[2].UploadSpeed, Is.EqualTo(25_000));
    }

    [Test]
    public void RecordSnapshot_should_not_spike_when_resuming_torrent_after_delay()
    {
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 1000, Downloaded = 500 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot(baseTime);

        // Torrent uploads 5000 bytes over 5 seconds
        baseTime = baseTime.AddSeconds(5);
        torrents[0].Uploaded = 6000;
        CallRecordSnapshot(baseTime);

        // Torrent is paused
        baseTime = baseTime.AddSeconds(5);
        torrents[0].Status = TorrentStatus.Paused;
        CallRecordSnapshot(baseTime);

        // 1 hour delay while paused, 50 MB accumulated
        baseTime = baseTime.AddHours(1);
        torrents[0].Status = TorrentStatus.Seeding;
        torrents[0].Uploaded = 50_000_000;
        CallRecordSnapshot(baseTime);

        var history = _service.GetHistory();
        var torrentHistory = _service.GetTorrentHistory(1);

        // Neither aggregate nor per-torrent speed should spike to multi-MB/s
        Assert.That(torrentHistory[3].UploadSpeed, Is.EqualTo(0));
        Assert.That(history[3].UploadSpeed, Is.EqualTo(0));

        // On the next normal snapshot interval (5s later), regular speed is calculated
        baseTime = baseTime.AddSeconds(5);
        torrents[0].Uploaded = 50_010_000; // 10,000 bytes over 5s = 2000 B/s
        CallRecordSnapshot(baseTime);

        history = _service.GetHistory();
        torrentHistory = _service.GetTorrentHistory(1);
        Assert.That(torrentHistory[4].UploadSpeed, Is.EqualTo(2000));
        Assert.That(history[4].UploadSpeed, Is.EqualTo(2000));
    }

    [Test]
    public void RecordSnapshot_should_not_spike_when_torrent_is_paused_for_multiple_snapshots_and_resumed()
    {
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Status = TorrentStatus.Seeding, Uploaded = 1000, Downloaded = 500 }
        };
        _torrentService.GetAll().Returns(torrents);

        CallRecordSnapshot(baseTime);

        // Pause for several snapshot intervals
        baseTime = baseTime.AddSeconds(5);
        torrents[0].Status = TorrentStatus.Paused;
        torrents[0].Uploaded = 6000;
        CallRecordSnapshot(baseTime);

        baseTime = baseTime.AddSeconds(5);
        CallRecordSnapshot(baseTime);

        baseTime = baseTime.AddSeconds(5);
        CallRecordSnapshot(baseTime);

        // Resume with accumulated bytes
        baseTime = baseTime.AddSeconds(5);
        torrents[0].Status = TorrentStatus.Seeding;
        torrents[0].Uploaded = 100_000_000;
        CallRecordSnapshot(baseTime);

        var history = _service.GetHistory();
        var torrentHistory = _service.GetTorrentHistory(1);

        Assert.That(torrentHistory[4].UploadSpeed, Is.EqualTo(0));
        Assert.That(history[4].UploadSpeed, Is.EqualTo(0));
    }

    private void CallRecordSnapshot(DateTime? timestamp = null)
    {
        if (timestamp.HasValue)
        {
            var method = typeof(SpeedHistoryService).GetMethod("RecordSnapshotAt",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method.Invoke(_service, new object[] { timestamp.Value });
        }
        else
        {
            var method = typeof(SpeedHistoryService).GetMethod("RecordSnapshot",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method.Invoke(_service, null);
        }
    }
}
