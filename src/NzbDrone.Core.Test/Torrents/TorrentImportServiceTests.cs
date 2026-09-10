using System;
using System.Collections.Generic;
using System.IO;
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
    public void ImportFromFile_should_throw_when_info_hash_already_exists()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var parsed = new ParsedTorrent
        {
            Name = "Existing Torrent",
            InfoHash = "abc123def456abc123def456abc123def456abcd"
        };
        _parser.Parse(stream).Returns(parsed);
        _torrentService.ExistsByInfoHash(parsed.InfoHash).Returns(true);

        var ex = Assert.Throws<InvalidOperationException>(() => _subject.ImportFromFile(stream, "test.torrent"));
        Assert.That(ex.Message, Does.Contain("already exists"));
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

        _torrentFileService.Received(1).Add(Arg.Is<TorrentFile>(f => f.TorrentId == 42 && f.Path == "ubuntu.iso" && f.Size == 900000));
        _torrentFileService.Received(1).Add(Arg.Is<TorrentFile>(f => f.TorrentId == 42 && f.Path == "ubuntu.md5" && f.Size == 100000));

        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t => t.TorrentId == 42 && t.Url == "http://tracker1.example.com/announce" && t.Tier == 0 && t.Enabled));
        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t => t.TorrentId == 42 && t.Url == "http://tracker1-backup.example.com/announce" && t.Tier == 0 && t.Enabled));
        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t => t.TorrentId == 42 && t.Url == "http://tracker2.example.com/announce" && t.Tier == 1 && t.Enabled));

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
        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t => t.TorrentId == 10 && t.Url == "http://single-tracker.example.com/announce" && t.Tier == 0 && t.Enabled));
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
    public void ImportFromMagnet_should_throw_when_info_hash_already_exists()
    {
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Duplicate";
        _torrentService.ExistsByInfoHash("0123456789abcdef0123456789abcdef01234567").Returns(true);

        var ex = Assert.Throws<InvalidOperationException>(() => _subject.ImportFromMagnet(magnetUri));
        Assert.That(ex.Message, Does.Contain("already exists"));
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

        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t => t.TorrentId == 77 && t.Url == "http://tracker1.org/announce" && t.Tier == 0 && t.Enabled));
        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t => t.TorrentId == 77 && t.Url == "http://tracker2.org/announce" && t.Tier == 1 && t.Enabled));

        _eventLogService.Received(1).Info(77, "Add", Arg.Is<string>(msg => msg.Contains("My Linux Distro") && msg.Contains("magnet link")));
    }
}
