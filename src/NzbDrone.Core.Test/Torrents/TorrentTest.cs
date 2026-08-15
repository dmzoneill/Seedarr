using System;
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
}
