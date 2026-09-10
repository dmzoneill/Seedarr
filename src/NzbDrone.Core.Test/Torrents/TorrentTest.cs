using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentTest
{
    [Test]
    public void Default_id_should_be_zero()
    {
        var torrent = new Torrent();

        Assert.That(torrent.Id, Is.EqualTo(0));
    }

    [Test]
    public void Default_status_should_be_stopped()
    {
        var torrent = new Torrent();

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
    }

    [Test]
    public void Should_set_and_get_name()
    {
        var torrent = new Torrent { Name = "My Torrent" };

        Assert.That(torrent.Name, Is.EqualTo("My Torrent"));
    }

    [Test]
    public void Should_set_and_get_info_hash()
    {
        var torrent = new Torrent { InfoHash = "abc123def456" };

        Assert.That(torrent.InfoHash, Is.EqualTo("abc123def456"));
    }

    [Test]
    public void Should_set_and_get_total_size()
    {
        var torrent = new Torrent { TotalSize = 1073741824L };

        Assert.That(torrent.TotalSize, Is.EqualTo(1073741824L));
    }

    [Test]
    public void Should_set_and_get_progress()
    {
        var torrent = new Torrent { Progress = 0.75 };

        Assert.That(torrent.Progress, Is.EqualTo(0.75));
    }

    [Test]
    public void TorrentStatus_enum_should_have_correct_values()
    {
        Assert.That((int)TorrentStatus.Stopped, Is.EqualTo(0));
        Assert.That((int)TorrentStatus.Seeding, Is.EqualTo(1));
        Assert.That((int)TorrentStatus.Paused, Is.EqualTo(2));
        Assert.That((int)TorrentStatus.Error, Is.EqualTo(3));
        Assert.That((int)TorrentStatus.Queued, Is.EqualTo(4));
        Assert.That((int)TorrentStatus.Downloading, Is.EqualTo(5));
    }

    [Test]
    public void Should_set_status_to_seeding()
    {
        var torrent = new Torrent { Status = TorrentStatus.Seeding };

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
    }

    [Test]
    public void Should_set_status_to_downloading()
    {
        var torrent = new Torrent { Status = TorrentStatus.Downloading };

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
    }

    [Test]
    public void Should_set_and_get_date_added()
    {
        var now = DateTime.UtcNow;
        var torrent = new Torrent { DateAdded = now };

        Assert.That(torrent.DateAdded, Is.EqualTo(now));
    }

    [Test]
    public void Should_set_and_get_sort_order()
    {
        var torrent = new Torrent { SortOrder = 42 };

        Assert.That(torrent.SortOrder, Is.EqualTo(42));
    }

    [Test]
    public void Default_progress_should_be_zero()
    {
        var torrent = new Torrent();

        Assert.That(torrent.Progress, Is.EqualTo(0.0));
    }

    [Test]
    public void ApplyUserFields_should_update_user_controllable_fields()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Original Name",
            Label = "Old Label",
            Priority = 1,
            UploadLimit = 100,
            DownloadLimit = 200,
            SuperSeeding = false,
            ForceStart = false,
            SequentialDownload = false,
            SmallTorrentLimit = 0,
            Threshold = 50,
            TagIds = new List<int> { 1, 2 },
            TrackerUrl = "http://tracker1.com/announce"
        };

        var updates = new Torrent
        {
            Name = "Updated Name",
            Label = "New Label",
            Priority = 5,
            UploadLimit = 500,
            DownloadLimit = 1000,
            SuperSeeding = true,
            ForceStart = true,
            SequentialDownload = true,
            SmallTorrentLimit = 1048576,
            Threshold = 80,
            TagIds = new List<int> { 3, 4 },
            TrackerUrl = "http://tracker2.com/announce"
        };

        torrent.ApplyUserFields(updates);

        Assert.That(torrent.Name, Is.EqualTo("Updated Name"));
        Assert.That(torrent.Label, Is.EqualTo("New Label"));
        Assert.That(torrent.Priority, Is.EqualTo(5));
        Assert.That(torrent.UploadLimit, Is.EqualTo(500));
        Assert.That(torrent.DownloadLimit, Is.EqualTo(1000));
        Assert.That(torrent.SuperSeeding, Is.True);
        Assert.That(torrent.ForceStart, Is.True);
        Assert.That(torrent.SequentialDownload, Is.True);
        Assert.That(torrent.SmallTorrentLimit, Is.EqualTo(1048576));
        Assert.That(torrent.Threshold, Is.EqualTo(80));
        Assert.That(torrent.TagIds, Is.EqualTo(new List<int> { 3, 4 }));
        Assert.That(torrent.TrackerUrl, Is.EqualTo("http://tracker2.com/announce"));
    }

    [Test]
    public void ApplyUserFields_should_preserve_engine_managed_stats()
    {
        var torrent = new Torrent
        {
            Id = 42,
            InfoHash = "abc123hash",
            TotalSize = 1000000000L,
            Uploaded = 5000000L,
            Downloaded = 10000000L,
            Ratio = 0.5,
            Seeders = 25,
            Leechers = 10,
            SessionUploaded = 100000L,
            SessionDownloaded = 200000L,
            UploadSpeed = 50000L,
            DownloadSpeed = 100000L,
            SeedingTime = 3600L,
            DateAdded = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var maliciousUpdates = new Torrent
        {
            Name = "New Name",
            Uploaded = 0,
            Downloaded = 0,
            Ratio = 0,
            Seeders = 0,
            Leechers = 0,
            SessionUploaded = 0,
            SessionDownloaded = 0,
            UploadSpeed = 0,
            DownloadSpeed = 0,
            SeedingTime = 0,
            TotalSize = 99999,
            DateAdded = DateTime.UtcNow
        };

        torrent.ApplyUserFields(maliciousUpdates);

        Assert.That(torrent.Name, Is.EqualTo("New Name"));
        Assert.That(torrent.Uploaded, Is.EqualTo(5000000L));
        Assert.That(torrent.Downloaded, Is.EqualTo(10000000L));
        Assert.That(torrent.Ratio, Is.EqualTo(0.5));
        Assert.That(torrent.Seeders, Is.EqualTo(25));
        Assert.That(torrent.Leechers, Is.EqualTo(10));
        Assert.That(torrent.SessionUploaded, Is.EqualTo(100000L));
        Assert.That(torrent.SessionDownloaded, Is.EqualTo(200000L));
        Assert.That(torrent.UploadSpeed, Is.EqualTo(50000L));
        Assert.That(torrent.DownloadSpeed, Is.EqualTo(100000L));
        Assert.That(torrent.SeedingTime, Is.EqualTo(3600L));
        Assert.That(torrent.TotalSize, Is.EqualTo(1000000000L));
        Assert.That(torrent.DateAdded, Is.EqualTo(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void ApplyTickStats_should_update_stats_and_calculate_ratio()
    {
        var torrent = new Torrent();

        torrent.ApplyTickStats(
            downloaded: 2000000,
            uploaded: 4000000,
            seeds: 15,
            peers: 30,
            downloadSpeed: 512000,
            uploadSpeed: 1024000,
            eta: 120,
            availability: 0.95,
            seedingTimeDelta: 10);

        Assert.That(torrent.Downloaded, Is.EqualTo(2000000));
        Assert.That(torrent.Uploaded, Is.EqualTo(4000000));
        Assert.That(torrent.Ratio, Is.EqualTo(2.0));
        Assert.That(torrent.Seeders, Is.EqualTo(15));
        Assert.That(torrent.Leechers, Is.EqualTo(30));
        Assert.That(torrent.DownloadSpeed, Is.EqualTo(512000));
        Assert.That(torrent.UploadSpeed, Is.EqualTo(1024000));
        Assert.That(torrent.Eta, Is.EqualTo(120));
        Assert.That(torrent.Availability, Is.EqualTo(0.95));
        Assert.That(torrent.SeedingTime, Is.EqualTo(10));
        Assert.That(torrent.LastActive, Is.Not.Null);
    }

    [Test]
    public void MarkForceCompleted_should_set_progress_and_transition_downloading_to_seeding()
    {
        var torrent = new Torrent
        {
            Status = TorrentStatus.Downloading,
            Progress = 0.45
        };

        torrent.MarkForceCompleted();

        Assert.That(torrent.ForceCompleted, Is.True);
        Assert.That(torrent.Progress, Is.EqualTo(1.0));
        Assert.That(torrent.Availability, Is.EqualTo(1.0));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
    }

    [Test]
    public void Lifecycle_methods_should_transition_status_correctly()
    {
        var torrent = new Torrent { Status = TorrentStatus.Downloading, UploadSpeed = 100, DownloadSpeed = 200, Active = true };

        torrent.Pause();
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Paused));

        torrent.Resume();
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));

        torrent.Stop();
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Stopped));
        Assert.That(torrent.UploadSpeed, Is.EqualTo(0));
        Assert.That(torrent.DownloadSpeed, Is.EqualTo(0));
        Assert.That(torrent.Active, Is.False);
    }
}
