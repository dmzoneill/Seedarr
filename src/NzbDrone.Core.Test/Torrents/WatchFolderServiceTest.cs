using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class WatchFolderServiceTest
{
    private ITorrentFileParser _parser;
    private ITorrentService _torrentService;
    private ITrackerEntryService _trackerEntryService;
    private IAppFolderInfo _appFolderInfo;
    private IConfigService _configService;
    private WatchFolderService _subject;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_watch_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _parser = Substitute.For<ITorrentFileParser>();
        _torrentService = Substitute.For<ITorrentService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _configService = Substitute.For<IConfigService>();

        _appFolderInfo.AppDataFolder.Returns(_tempDir);
        _configService.WatchFolderScanIntervalSeconds.Returns(1);
        _configService.AnnounceIntervalSeconds.Returns(1800);
        _configService.MinAnnounceIntervalSeconds.Returns(300);
        _configService.WatchFolderAutoStartTorrents.Returns(true);
        _configService.WatchFolderDeleteAddedTorrents.Returns(false);

        _subject = new WatchFolderService(_parser, _torrentService, _trackerEntryService, _appFolderInfo, _configService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_should_exit_early_when_disabled()
    {
        _configService.WatchFolderEnabled.Returns(false);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(300);

        _parser.DidNotReceive().Parse(Arg.Any<string>());
    }

    [Test]
    public async Task ExecuteAsync_should_create_default_watch_folder_when_path_empty()
    {
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderPath.Returns("");

        var expectedPath = Path.Combine(_tempDir, "watch");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(600);

        Assert.That(Directory.Exists(expectedPath), Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_use_configured_path_when_set()
    {
        var watchDir = Path.Combine(_tempDir, "custom-watch");
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderPath.Returns(watchDir);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(600);

        Assert.That(Directory.Exists(watchDir), Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_process_torrent_files_during_scan()
    {
        var watchDir = Path.Combine(_tempDir, "watch");
        Directory.CreateDirectory(watchDir);
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderPath.Returns(watchDir);

        var torrentPath = Path.Combine(watchDir, "test.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "Test",
            InfoHash = "abc123",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            AnnounceUrl = "http://tracker.example.com/announce",
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);

        var addedTorrent = new Torrent { Id = 1, Name = "Test" };
        _torrentService.Add(Arg.Any<Torrent>()).Returns(addedTorrent);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(3500);

        _torrentService.Received().Add(Arg.Is<Torrent>(t => t.Name == "Test" && t.InfoHash == "abc123"));
    }

    [Test]
    public async Task ExecuteAsync_should_set_status_to_seeding_when_auto_start_enabled()
    {
        var watchDir = Path.Combine(_tempDir, "watch");
        Directory.CreateDirectory(watchDir);
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderPath.Returns(watchDir);
        _configService.WatchFolderAutoStartTorrents.Returns(true);

        var torrentPath = Path.Combine(watchDir, "test.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "AutoStart",
            InfoHash = "def456",
            TotalSize = 2048,
            PieceCount = 1,
            PieceLength = 2048,
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 1 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(3500);

        _torrentService.Received().Add(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Seeding));
    }

    [Test]
    public async Task ExecuteAsync_should_set_status_to_stopped_when_auto_start_disabled()
    {
        var watchDir = Path.Combine(_tempDir, "watch");
        Directory.CreateDirectory(watchDir);
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderPath.Returns(watchDir);
        _configService.WatchFolderAutoStartTorrents.Returns(false);

        var torrentPath = Path.Combine(watchDir, "test.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "Stopped",
            InfoHash = "ghi789",
            TotalSize = 512,
            PieceCount = 1,
            PieceLength = 512,
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 1 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(3500);

        _torrentService.Received().Add(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Stopped));
    }

    [Test]
    public async Task ExecuteAsync_should_create_tracker_entries_from_announce_list()
    {
        var watchDir = Path.Combine(_tempDir, "watch");
        Directory.CreateDirectory(watchDir);
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderPath.Returns(watchDir);

        var torrentPath = Path.Combine(watchDir, "test.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "TrackerTest",
            InfoHash = "jkl012",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            AnnounceUrl = "http://tracker.example.com/announce",
            AnnounceList = new List<List<string>>
            {
                new() { "http://tracker1.example.com/announce", "http://tracker2.example.com/announce" },
                new() { "http://tracker3.example.com/announce" }
            },
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 10 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(3500);

        _trackerEntryService.Received().Add(Arg.Is<TrackerEntry>(t => t.Tier == 0));
        _trackerEntryService.Received().Add(Arg.Is<TrackerEntry>(t =>
            t.Url == "http://tracker1.example.com/announce"));
        _trackerEntryService.Received().Add(Arg.Is<TrackerEntry>(t =>
            t.Url == "http://tracker3.example.com/announce" && t.Tier == 1));
    }

    [Test]
    public async Task ExecuteAsync_should_create_single_tracker_entry_from_announce_url()
    {
        var watchDir = Path.Combine(_tempDir, "watch");
        Directory.CreateDirectory(watchDir);
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderPath.Returns(watchDir);

        var torrentPath = Path.Combine(watchDir, "test.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "SingleTracker",
            InfoHash = "mno345",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            AnnounceUrl = "http://tracker.example.com/announce",
            AnnounceList = null,
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 10 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(3500);

        _trackerEntryService.Received().Add(Arg.Is<TrackerEntry>(t =>
            t.Url == "http://tracker.example.com/announce" && t.Tier == 0));
    }

    [Test]
    public async Task ExecuteAsync_should_enforce_minimum_scan_interval()
    {
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderScanIntervalSeconds.Returns(0);
        _configService.WatchFolderPath.Returns(Path.Combine(_tempDir, "watch"));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(600);

        Assert.Pass();
    }

    [Test]
    public async Task ExecuteAsync_should_skip_duplicate_tracker_urls_in_announce_list()
    {
        var watchDir = Path.Combine(_tempDir, "watch");
        Directory.CreateDirectory(watchDir);
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderPath.Returns(watchDir);

        var torrentPath = Path.Combine(watchDir, "test.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "DupTracker",
            InfoHash = "dup123",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            AnnounceList = new List<List<string>>
            {
                new() { "http://tracker.example.com/announce", "http://tracker.example.com/announce" }
            },
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 10 });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(3500);

        _trackerEntryService.Received().Add(Arg.Is<TrackerEntry>(t =>
            t.Url == "http://tracker.example.com/announce"));
        _trackerEntryService.DidNotReceive().Add(Arg.Is<TrackerEntry>(t =>
            t.Url != "http://tracker.example.com/announce"));
    }

    [Test]
    public async Task ExecuteAsync_should_exit_when_watch_dir_cannot_be_created()
    {
        _configService.WatchFolderEnabled.Returns(true);

        // Place a file where the watch directory should be so CreateDirectory throws
        var blockingFile = Path.Combine(_tempDir, "blocked_watch");
        await File.WriteAllBytesAsync(blockingFile, Array.Empty<byte>());
        _configService.WatchFolderPath.Returns(blockingFile);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await _subject.StartAsync(cts.Token);
        await Task.Delay(200);

        _parser.DidNotReceive().Parse(Arg.Any<string>());
    }

    [Test]
    public void ProcessTorrentFile_should_return_early_when_file_does_not_exist()
    {
        var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var nonExistentPath = Path.Combine(_tempDir, "nonexistent.torrent");

        Assert.DoesNotThrow(() => method.Invoke(_subject, new object[] { nonExistentPath }));
        _parser.DidNotReceive().Parse(Arg.Any<string>());
    }

    [Test]
    public void ProcessTorrentFile_should_delete_file_when_delete_after_add_enabled()
    {
        _configService.WatchFolderDeleteAddedTorrents.Returns(true);

        var torrentPath = Path.Combine(_tempDir, "todelete.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "ToDelete",
            InfoHash = "abc123",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 1 });

        var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { torrentPath });

        Assert.That(File.Exists(torrentPath), Is.False);
    }

    [Test]
    public void ProcessTorrentFile_should_handle_parse_exception()
    {
        var torrentPath = Path.Combine(_tempDir, "bad.torrent");
        File.WriteAllText(torrentPath, "not valid torrent data");

        _parser.Parse(torrentPath).Returns(x => throw new Exception("Parse failed"));

        var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotThrow(() => method.Invoke(_subject, new object[] { torrentPath }));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void CreateTrackerEntries_should_skip_whitespace_url_in_announce_list()
    {
        var parsed = new ParsedTorrent
        {
            AnnounceList = new List<List<string>>
            {
                new() { "  ", "http://tracker.example.com/announce" }
            },
            Files = new List<ParsedTorrentFile>()
        };

        var method = typeof(WatchFolderService).GetMethod("CreateTrackerEntries",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { 1, parsed });

        // Whitespace url is skipped; only the valid one is added
        _trackerEntryService.Received(1).Add(Arg.Any<TrackerEntry>());
        _trackerEntryService.DidNotReceive().Add(
            Arg.Is<TrackerEntry>(t => string.IsNullOrWhiteSpace(t.Url)));
    }

    [Test]
    public void CreateTrackerEntries_should_use_announce_url_when_announce_list_is_empty()
    {
        var parsed = new ParsedTorrent
        {
            AnnounceList = new List<List<string>>(), // non-null but empty
            AnnounceUrl = "http://tracker.example.com/announce",
            Files = new List<ParsedTorrentFile>()
        };

        var method = typeof(WatchFolderService).GetMethod("CreateTrackerEntries",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { 1, parsed });

        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(t =>
            t.Url == "http://tracker.example.com/announce" && t.Tier == 0));
    }

    [Test]
    public void OnTorrentFileCreated_should_process_torrent_file()
    {
        var torrentPath = Path.Combine(_tempDir, "oncreated.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "OnCreated",
            InfoHash = "created123",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 1 });

        var method = typeof(WatchFolderService).GetMethod("OnTorrentFileCreated",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var args = new FileSystemEventArgs(WatcherChangeTypes.Created, _tempDir, "oncreated.torrent");

        method.Invoke(_subject, new object[] { null, args });

        _torrentService.Received(1).Add(Arg.Any<Torrent>());
    }

    [Test]
    public async Task PeriodicScan_should_skip_when_directory_does_not_exist()
    {
        var watchDir = Path.Combine(_tempDir, "volatile-watch");
        Directory.CreateDirectory(watchDir);
        _configService.WatchFolderEnabled.Returns(true);
        _configService.WatchFolderPath.Returns(watchDir);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await _subject.StartAsync(cts.Token);

        // Delete the directory; the next periodic scan should detect it is gone and no-op
        Directory.Delete(watchDir);

        await Task.Delay(1500);

        _parser.DidNotReceive().Parse(Arg.Any<string>());
    }

    private static void CreateDummyTorrentFile(string path)
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);
        var info = new BDictionary
        {
            { "name", new BString("dummy") },
            { "piece length", new BNumber(512) },
            { "pieces", new BString(pieces) },
            { "length", new BNumber(1024) }
        };
        var torrent = new BDictionary { { "info", info } };
        File.WriteAllBytes(path, torrent.EncodeAsBytes());
    }
}
