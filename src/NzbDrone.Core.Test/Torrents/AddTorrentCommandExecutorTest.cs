using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class AddTorrentCommandExecutorTest
{
    private ITorrentFileParser _parser;
    private ITorrentService _torrentService;
    private ITrackerEntryService _trackerEntryService;
    private ITorrentFileService _torrentFileService;
    private IConfigService _configService;
    private AddTorrentCommandExecutor _subject;

    [SetUp]
    public void SetUp()
    {
        _parser = Substitute.For<ITorrentFileParser>();
        _torrentService = Substitute.For<ITorrentService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _configService = Substitute.For<IConfigService>();

        _configService.AnnounceIntervalSeconds.Returns(1800);
        _configService.MinAnnounceIntervalSeconds.Returns(300);
        _configService.AutoStart.Returns(true);

        _subject = new AddTorrentCommandExecutor(
            _parser,
            _torrentService,
            _trackerEntryService,
            _torrentFileService,
            _configService);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Execute_should_throw_when_filepath_is_null_or_whitespace(string filePath)
    {
        var command = new AddTorrentCommand { FilePath = filePath };

        Assert.Throws<ArgumentException>(() => _subject.Execute(command));
    }

    [Test]
    public void Execute_should_skip_when_torrent_already_exists_by_info_hash()
    {
        var command = new AddTorrentCommand { FilePath = "/path/to/existing.torrent" };
        var parsed = new ParsedTorrent
        {
            Name = "Existing",
            InfoHash = "abc123hash",
            TotalSize = 1000,
            Files = new List<ParsedTorrentFile> { new() { Path = "file.iso", Size = 1000 } }
        };

        _parser.Parse("/path/to/existing.torrent").Returns(parsed);
        _torrentService.ExistsByInfoHash("abc123hash").Returns(true);

        _subject.Execute(command);

        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
        _torrentFileService.DidNotReceive().Add(Arg.Any<TorrentFile>());
        _trackerEntryService.DidNotReceive().Add(Arg.Any<TrackerEntry>());
    }

    [Test]
    public void Execute_should_persist_torrent_files_when_parsed_files_present()
    {
        var command = new AddTorrentCommand { FilePath = "/path/to/multi.torrent" };
        var parsed = new ParsedTorrent
        {
            Name = "MultiFileTorrent",
            InfoHash = "multifilehash",
            TotalSize = 5000,
            PieceCount = 5,
            PieceLength = 1000,
            Comment = "Test Comment",
            CreatedBy = "Tester",
            Files = new List<ParsedTorrentFile>
            {
                new() { Path = "folder/video.mkv", Size = 4000 },
                new() { Path = "folder/subs.srt", Size = 1000 }
            }
        };

        _parser.Parse("/path/to/multi.torrent").Returns(parsed);
        _torrentService.ExistsByInfoHash("multifilehash").Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 100, Name = "MultiFileTorrent" });

        _subject.Execute(command);

        _torrentFileService.Received(1).Add(Arg.Is<TorrentFile>(f =>
            f.TorrentId == 100 && f.Path == "folder/video.mkv" && f.Size == 4000));
        _torrentFileService.Received(1).Add(Arg.Is<TorrentFile>(f =>
            f.TorrentId == 100 && f.Path == "folder/subs.srt" && f.Size == 1000));
    }

    [Test]
    public void Execute_should_not_persist_files_when_files_list_is_empty()
    {
        var command = new AddTorrentCommand { FilePath = "/path/to/emptyfiles.torrent" };
        var parsed = new ParsedTorrent
        {
            Name = "EmptyFiles",
            InfoHash = "emptyfileshash",
            TotalSize = 1000,
            Files = new List<ParsedTorrentFile>()
        };

        _parser.Parse("/path/to/emptyfiles.torrent").Returns(parsed);
        _torrentService.ExistsByInfoHash("emptyfileshash").Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 101 });

        _subject.Execute(command);

        _torrentFileService.DidNotReceive().Add(Arg.Any<TorrentFile>());
    }

    [Test]
    public void Execute_should_not_persist_files_when_files_list_is_null()
    {
        var command = new AddTorrentCommand { FilePath = "/path/to/nullfiles.torrent" };
        var parsed = new ParsedTorrent
        {
            Name = "NullFiles",
            InfoHash = "nullfileshash",
            TotalSize = 1000,
            Files = null
        };

        _parser.Parse("/path/to/nullfiles.torrent").Returns(parsed);
        _torrentService.ExistsByInfoHash("nullfileshash").Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 102 });

        _subject.Execute(command);

        _torrentFileService.DidNotReceive().Add(Arg.Any<TorrentFile>());
    }

    [Test]
    public void Execute_should_set_status_to_seeding_when_autostart_is_true()
    {
        _configService.AutoStart.Returns(true);
        var command = new AddTorrentCommand { FilePath = "/path/to/test.torrent" };
        var parsed = new ParsedTorrent
        {
            Name = "AutoStartTorrent",
            InfoHash = "autostarthash",
            TotalSize = 1000
        };

        _parser.Parse("/path/to/test.torrent").Returns(parsed);
        _torrentService.ExistsByInfoHash("autostarthash").Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 103 });

        _subject.Execute(command);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Seeding));
    }

    [Test]
    public void Execute_should_set_status_to_stopped_when_autostart_is_false()
    {
        _configService.AutoStart.Returns(false);
        var command = new AddTorrentCommand { FilePath = "/path/to/test.torrent" };
        var parsed = new ParsedTorrent
        {
            Name = "StoppedTorrent",
            InfoHash = "stoppedhash",
            TotalSize = 1000
        };

        _parser.Parse("/path/to/test.torrent").Returns(parsed);
        _torrentService.ExistsByInfoHash("stoppedhash").Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 104 });

        _subject.Execute(command);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Stopped));
    }

    [Test]
    public void Execute_should_create_tracker_entries_from_announce_list()
    {
        var command = new AddTorrentCommand { FilePath = "/path/to/test.torrent" };
        var parsed = new ParsedTorrent
        {
            Name = "TrackerListTorrent",
            InfoHash = "trackerhash",
            TotalSize = 1000,
            AnnounceList = new List<List<string>>
            {
                new() { "http://t1.example.com/announce", "http://t2.example.com/announce" },
                new() { "http://t3.example.com/announce" }
            }
        };

        _parser.Parse("/path/to/test.torrent").Returns(parsed);
        _torrentService.ExistsByInfoHash("trackerhash").Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 105 });

        _subject.Execute(command);

        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t =>
            t.TorrentId == 105 && t.Url == "http://t1.example.com/announce" && t.Tier == 0));
        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t =>
            t.TorrentId == 105 && t.Url == "http://t2.example.com/announce" && t.Tier == 0));
        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t =>
            t.TorrentId == 105 && t.Url == "http://t3.example.com/announce" && t.Tier == 1));
    }

    [Test]
    public void Execute_should_create_single_tracker_entry_from_announce_url()
    {
        var command = new AddTorrentCommand { FilePath = "/path/to/test.torrent" };
        var parsed = new ParsedTorrent
        {
            Name = "SingleTrackerTorrent",
            InfoHash = "singletrackerhash",
            TotalSize = 1000,
            AnnounceUrl = "http://single.example.com/announce"
        };

        _parser.Parse("/path/to/test.torrent").Returns(parsed);
        _torrentService.ExistsByInfoHash("singletrackerhash").Returns(false);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 106 });

        _subject.Execute(command);

        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t =>
            t.TorrentId == 106 && t.Url == "http://single.example.com/announce" && t.Tier == 0));
    }
}
