using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentImportServiceTests
{
    private ITorrentFileParser _parser;
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITrackerEntryService _trackerEntryService;
    private ITorrentEventLogService _eventLogService;
    private TorrentImportService _subject;

    [SetUp]
    public void SetUp()
    {
        _parser = Substitute.For<ITorrentFileParser>();
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _eventLogService = Substitute.For<ITorrentEventLogService>();

        _subject = new TorrentImportService(
            _parser,
            _torrentService,
            _torrentFileService,
            _trackerEntryService,
            _eventLogService);
    }

    [Test]
    public void ImportFromFile_should_throw_when_stream_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => _subject.ImportFromFile(null, "test.torrent"));
    }

    [Test]
    public void ImportFromFile_should_throw_when_file_parser_returns_null()
    {
        using var stream = new MemoryStream();
        _parser.Parse(stream).Returns((ParsedTorrent)null);

        Assert.Throws<InvalidOperationException>(() => _subject.ImportFromFile(stream, "test.torrent"));
    }

    [Test]
    public void ImportFromFile_should_merge_trackers_when_torrent_already_exists()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var parsed = new ParsedTorrent
        {
            Name = "Existing Torrent",
            InfoHash = "abc123def456abc123def456abc123def456abcd",
            AnnounceList = new List<List<string>>
            {
                new() { "http://tracker1.example.com/announce", "http://tracker2.example.com/announce" }
            }
        };

        var existing = new Torrent
        {
            Id = 55,
            Name = "Existing Torrent",
            InfoHash = parsed.InfoHash
        };

        _parser.Parse(stream).Returns(parsed);
        _torrentService.GetByInfoHash(parsed.InfoHash).Returns(existing);
        _trackerEntryService.GetByTorrentId(55).Returns(new List<TrackerEntry>
        {
            new() { Id = 1, TorrentId = 55, Url = "http://tracker1.example.com/announce", Tier = 0 }
        });

        var result = _subject.ImportFromFile(stream, "test.torrent");

        Assert.That(result, Is.SameAs(existing));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
        _trackerEntryService.Received(1).AddMany(Arg.Is<IList<TrackerEntry>>(list =>
            list.Any(t => t.TorrentId == 55 && t.Url == "http://tracker2.example.com/announce" && t.Tier == 0 && t.Enabled) &&
            !list.Any(t => t.Url == "http://tracker1.example.com/announce")));
        _eventLogService.Received(1).Info(55, "Update", Arg.Is<string>(msg => msg.Contains("Existing Torrent") && msg.Contains("updated with new trackers")));
    }

    [Test]
    public void ImportFromFile_should_preserve_tier_indices_when_merging_trackers()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var parsed = new ParsedTorrent
        {
            Name = "Tiered Torrent",
            InfoHash = "abc123def456abc123def456abc123def456abcd",
            AnnounceList = new List<List<string>>
            {
                new() { "http://tier0.example.com/announce" },
                new() { "http://tier1.example.com/announce" },
                new() { "http://tier2.example.com/announce" }
            }
        };

        var existing = new Torrent
        {
            Id = 60,
            Name = "Tiered Torrent",
            InfoHash = parsed.InfoHash
        };

        _parser.Parse(stream).Returns(parsed);
        _torrentService.GetByInfoHash(parsed.InfoHash).Returns(existing);
        _trackerEntryService.GetByTorrentId(60).Returns(new List<TrackerEntry>
        {
            new() { Id = 1, TorrentId = 60, Url = "http://tier0.example.com/announce", Tier = 0 }
        });

        var result = _subject.ImportFromFile(stream, "test.torrent");

        Assert.That(result, Is.SameAs(existing));
        _trackerEntryService.Received(1).AddMany(Arg.Is<IList<TrackerEntry>>(list =>
            list.Any(t => t.TorrentId == 60 && t.Url == "http://tier1.example.com/announce" && t.Tier == 1) &&
            list.Any(t => t.TorrentId == 60 && t.Url == "http://tier2.example.com/announce" && t.Tier == 2)));
    }

    [Test]
    public void ImportFromFile_should_merge_single_announce_url_into_tier_zero()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var parsed = new ParsedTorrent
        {
            Name = "Single Announce Torrent",
            InfoHash = "abc123def456abc123def456abc123def456abcd",
            AnnounceUrl = "http://newtracker.example.com/announce",
            AnnounceList = null
        };

        var existing = new Torrent
        {
            Id = 65,
            Name = "Single Announce Torrent",
            InfoHash = parsed.InfoHash
        };

        _parser.Parse(stream).Returns(parsed);
        _torrentService.GetByInfoHash(parsed.InfoHash).Returns(existing);
        _trackerEntryService.GetByTorrentId(65).Returns(new List<TrackerEntry>());

        var result = _subject.ImportFromFile(stream, "test.torrent");

        Assert.That(result, Is.SameAs(existing));
        _trackerEntryService.Received(1).AddMany(Arg.Is<IList<TrackerEntry>>(list =>
            list.Any(t => t.TorrentId == 65 && t.Url == "http://newtracker.example.com/announce" && t.Tier == 0 && t.Enabled)));
    }

    [Test]
    public void ImportFromFile_should_add_torrent_and_files_and_trackers()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var parsed = new ParsedTorrent
        {
            Name = "Ubuntu Linux",
            InfoHash = "4a5c6d7e8f901234567890abcdef1234567890ab",
            TotalSize = 1000000,
            PieceCount = 100,
            PieceLength = 10000,
            Comment = "Official release",
            CreatedBy = "Ubuntu",
            CreationDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrivate = false,
            AnnounceUrl = "http://tracker1.example.com/announce",
            AnnounceList = new List<List<string>>
            {
                new() { "http://tracker1.example.com/announce", "http://tracker1-backup.example.com/announce" },
                new() { "http://tracker2.example.com/announce" }
            },
            Files = new List<ParsedTorrentFile>
            {
                new() { Path = "ubuntu.iso", Size = 900000 },
                new() { Path = "ubuntu.md5", Size = 100000 }
            }
        };

        _parser.Parse(stream).Returns(parsed);
        _torrentService.ExistsByInfoHash(parsed.InfoHash).Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            t.Id = 42;
            return t;
        });

        var result = _subject.ImportFromFile(stream, "ubuntu.torrent");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(42));
        Assert.That(result.Name, Is.EqualTo("Ubuntu Linux"));
        Assert.That(result.InfoHash, Is.EqualTo(parsed.InfoHash));
        Assert.That(result.Status, Is.EqualTo(TorrentStatus.Queued));

        _torrentFileService.Received(1).AddMany(Arg.Is<IList<TorrentFile>>(files =>
            files.Any(f => f.TorrentId == 42 && f.Path == "ubuntu.iso" && f.Size == 900000) &&
            files.Any(f => f.TorrentId == 42 && f.Path == "ubuntu.md5" && f.Size == 100000)));

        _trackerEntryService.Received(1).AddMany(Arg.Is<IList<TrackerEntry>>(trackers =>
            trackers.Any(t => t.TorrentId == 42 && t.Url == "http://tracker1.example.com/announce" && t.Tier == 0 && t.Enabled) &&
            trackers.Any(t => t.TorrentId == 42 && t.Url == "http://tracker1-backup.example.com/announce" && t.Tier == 0 && t.Enabled) &&
            trackers.Any(t => t.TorrentId == 42 && t.Url == "http://tracker2.example.com/announce" && t.Tier == 1 && t.Enabled)));

        _eventLogService.Received(1).Info(42, "Add", Arg.Is<string>(msg => msg.Contains("Ubuntu Linux") && msg.Contains("ubuntu.torrent")));
    }

    [Test]
    public void ImportFromFile_should_add_single_tracker_when_announce_list_is_null()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var parsed = new ParsedTorrent
        {
            Name = "Single Tracker Torrent",
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            AnnounceUrl = "http://single-tracker.example.com/announce",
            AnnounceList = null
        };

        _parser.Parse(stream).Returns(parsed);
        _torrentService.ExistsByInfoHash(parsed.InfoHash).Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            t.Id = 10;
            return t;
        });

        var result = _subject.ImportFromFile(stream, "single.torrent");

        Assert.That(result, Is.Not.Null);
        _trackerEntryService.Received(1).AddMany(Arg.Is<IList<TrackerEntry>>(trackers =>
            trackers.Any(t => t.TorrentId == 10 && t.Url == "http://single-tracker.example.com/announce" && t.Tier == 0 && t.Enabled)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ImportFromMagnet_should_throw_when_magnet_link_is_null_or_whitespace(string magnetLink)
    {
        Assert.Throws<ArgumentException>(() => _subject.ImportFromMagnet(magnetLink));
    }

    [Test]
    public void ImportFromMagnet_should_throw_when_magnet_link_is_invalid()
    {
        Assert.Throws<ArgumentException>(() => _subject.ImportFromMagnet("invalid-magnet-uri"));
    }

    [Test]
    public void ImportFromMagnet_should_merge_trackers_when_torrent_already_exists()
    {
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Duplicate&tr=http%3A%2F%2Ftracker1.org%2Fannounce&tr=http%3A%2F%2Ftracker2.org%2Fannounce";
        var existing = new Torrent
        {
            Id = 88,
            Name = "Duplicate",
            InfoHash = "0123456789abcdef0123456789abcdef01234567"
        };

        _torrentService.GetByInfoHash("0123456789abcdef0123456789abcdef01234567").Returns(existing);
        _trackerEntryService.GetByTorrentId(88).Returns(new List<TrackerEntry>
        {
            new() { Id = 1, TorrentId = 88, Url = "http://tracker1.org/announce", Tier = 0 }
        });

        var result = _subject.ImportFromMagnet(magnetUri);

        Assert.That(result, Is.SameAs(existing));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
        _trackerEntryService.Received(1).AddMany(Arg.Is<IList<TrackerEntry>>(trackers =>
            trackers.Any(t => t.TorrentId == 88 && t.Url == "http://tracker2.org/announce" && t.Tier == 1 && t.Enabled) &&
            !trackers.Any(t => t.Url == "http://tracker1.org/announce")));
        _eventLogService.Received(1).Info(88, "Update", Arg.Is<string>(msg => msg.Contains("Duplicate") && msg.Contains("updated with new trackers")));
    }

    [Test]
    public void ImportFromMagnet_should_add_torrent_and_trackers_and_log_event()
    {
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=My+Linux+Distro&tr=http%3A%2F%2Ftracker1.org%2Fannounce&tr=http%3A%2F%2Ftracker2.org%2Fannounce";
        _torrentService.ExistsByInfoHash("0123456789abcdef0123456789abcdef01234567").Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            t.Id = 77;
            return t;
        });

        var result = _subject.ImportFromMagnet(magnetUri);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(77));
        Assert.That(result.Name, Is.EqualTo("My Linux Distro"));
        Assert.That(result.InfoHash, Is.EqualTo("0123456789abcdef0123456789abcdef01234567"));
        Assert.That(result.TrackerUrl, Is.EqualTo("http://tracker1.org/announce"));
        Assert.That(result.Status, Is.EqualTo(TorrentStatus.Queued));

        _trackerEntryService.Received(1).AddMany(Arg.Is<IList<TrackerEntry>>(trackers =>
            trackers.Any(t => t.TorrentId == 77 && t.Url == "http://tracker1.org/announce" && t.Tier == 0 && t.Enabled) &&
            trackers.Any(t => t.TorrentId == 77 && t.Url == "http://tracker2.org/announce" && t.Tier == 1 && t.Enabled)));

        _eventLogService.Received(1).Info(77, "Add", Arg.Is<string>(msg => msg.Contains("My Linux Distro") && msg.Contains("magnet link")));
    }

    [Test]
    public void ImportFromFile_should_cleanup_torrent_and_throw_when_file_insertion_fails()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var parsed = new ParsedTorrent
        {
            Name = "Failing File Torrent",
            InfoHash = "4a5c6d7e8f901234567890abcdef1234567890ab",
            Files = new List<ParsedTorrentFile>
            {
                new() { Path = "broken.bin", Size = 100 }
            }
        };

        _parser.Parse(stream).Returns(parsed);
        _torrentService.ExistsByInfoHash(parsed.InfoHash).Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            t.Id = 42;
            return t;
        });
        _torrentFileService.When(x => x.AddMany(Arg.Any<IList<TorrentFile>>())).Do(_ => throw new InvalidOperationException("Disk error"));

        Assert.Throws<InvalidOperationException>(() => _subject.ImportFromFile(stream, "failing.torrent"));
        _torrentService.Received(1).Delete(42, false);
    }

    [Test]
    public void ImportFromFile_should_cleanup_torrent_and_throw_when_tracker_insertion_fails()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var parsed = new ParsedTorrent
        {
            Name = "Failing Tracker Torrent",
            InfoHash = "4a5c6d7e8f901234567890abcdef1234567890ab",
            AnnounceUrl = "http://tracker.example.com/announce"
        };

        _parser.Parse(stream).Returns(parsed);
        _torrentService.ExistsByInfoHash(parsed.InfoHash).Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            t.Id = 42;
            return t;
        });
        _trackerEntryService.When(x => x.AddMany(Arg.Any<IList<TrackerEntry>>())).Do(_ => throw new InvalidOperationException("DB error"));

        Assert.Throws<InvalidOperationException>(() => _subject.ImportFromFile(stream, "failing.torrent"));
        _torrentService.Received(1).Delete(42, false);
    }

    [Test]
    public void ImportFromMagnet_should_cleanup_torrent_and_throw_when_tracker_insertion_fails()
    {
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=FailingMagnet&tr=http%3A%2F%2Ftracker1.org%2Fannounce";
        _torrentService.ExistsByInfoHash("0123456789abcdef0123456789abcdef01234567").Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            t.Id = 77;
            return t;
        });
        _trackerEntryService.When(x => x.AddMany(Arg.Any<IList<TrackerEntry>>())).Do(_ => throw new InvalidOperationException("DB error"));

        Assert.Throws<InvalidOperationException>(() => _subject.ImportFromMagnet(magnetUri));
        _torrentService.Received(1).Delete(77, false);
    }
}
