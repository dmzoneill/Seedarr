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
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class WatchFolderServiceTest
{
    private ITorrentFileParser _parser;
    private ITorrentService _torrentService;
    private ITrackerEntryService _trackerEntryService;
    private ITorrentFileService _torrentFileService;
    private IAppFolderInfo _appFolderInfo;
    private IConfigService _configService;
    private ICategoryService _categoryService;
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
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _configService = Substitute.For<IConfigService>();
        _categoryService = Substitute.For<ICategoryService>();

        _appFolderInfo.AppDataFolder.Returns(_tempDir);
        _configService.WatchFolderScanIntervalSeconds.Returns(1);
        _configService.AnnounceIntervalSeconds.Returns(1800);
        _configService.MinAnnounceIntervalSeconds.Returns(300);
        _configService.WatchFolderAutoStartTorrents.Returns(true);
        _configService.WatchFolderDeleteAddedTorrents.Returns(false);

        _subject = new WatchFolderService(_parser, _torrentService, _trackerEntryService, _torrentFileService, _appFolderInfo, _configService, _categoryService);
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
    public async Task ExecuteAsync_should_set_status_to_downloading_when_auto_start_enabled_and_progress_zero()
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

        _torrentService.Received().Add(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Downloading));
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
        Thread.Sleep(700);

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

    [Test]
    public void ProcessTorrentFile_should_persist_torrent_files_when_parsed_files_present()
    {
        var torrentPath = Path.Combine(_tempDir, "multifile.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "MultiFileTorrent",
            InfoHash = "multifile123",
            TotalSize = 3000,
            PieceCount = 3,
            PieceLength = 1000,
            Files = new List<ParsedTorrentFile>
            {
                new() { Path = "folder/file1.mkv", Size = 2000 },
                new() { Path = "folder/file2.nfo", Size = 1000 }
            }
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 42, Name = "MultiFileTorrent" });

        var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { torrentPath });

        _torrentFileService.Received(1).Add(Arg.Is<TorrentFile>(f =>
            f.TorrentId == 42 && f.Path == "folder/file1.mkv" && f.Size == 2000));
        _torrentFileService.Received(1).Add(Arg.Is<TorrentFile>(f =>
            f.TorrentId == 42 && f.Path == "folder/file2.nfo" && f.Size == 1000));
    }

    [Test]
    public void ProcessTorrentFile_should_not_persist_files_when_files_list_is_empty()
    {
        var torrentPath = Path.Combine(_tempDir, "nofiles.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "NoFilesTorrent",
            InfoHash = "nofiles123",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 43, Name = "NoFilesTorrent" });

        var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { torrentPath });

        _torrentFileService.DidNotReceive().Add(Arg.Any<TorrentFile>());
    }

    [Test]
    public void ProcessTorrentFile_should_not_persist_files_when_files_list_is_null()
    {
        var torrentPath = Path.Combine(_tempDir, "nullfiles.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "NullFilesTorrent",
            InfoHash = "nullfiles123",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            Files = null
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 44, Name = "NullFilesTorrent" });

        var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { torrentPath });

        _torrentFileService.DidNotReceive().Add(Arg.Any<TorrentFile>());
    }

    [Test]
    public void ProcessTorrentFile_should_map_subfolder_to_category_and_resolve_category_save_path()
    {
        var watchDir = Path.Combine(_tempDir, "watch");
        var subDir = Path.Combine(watchDir, "movies");
        Directory.CreateDirectory(subDir);
        _configService.WatchFolderPath.Returns(watchDir);

        var torrentPath = Path.Combine(subDir, "test.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "MovieTorrent",
            InfoHash = "movie123",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);

        var category = new Category
        {
            Id = 1,
            Name = "movies",
            SavePath = "/media/movies"
        };
        _categoryService.GetByName("movies").Returns(category);
        _categoryService.GetSavePathForCategory("movies", Arg.Any<string>()).Returns("/media/movies");

        var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { torrentPath, watchDir });

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.Name == "MovieTorrent" &&
            t.Category == "movies" &&
            t.SavePath == "/media/movies"));
    }

    [Test]
    public void ProcessTorrentFile_in_root_watch_folder_should_have_save_path_set_to_default_directory()
    {
        var watchDir = Path.Combine(_tempDir, "watch");
        Directory.CreateDirectory(watchDir);
        _configService.WatchFolderPath.Returns(watchDir);
        var defaultDownloads = Path.Combine(_tempDir, "default_downloads");
        _configService.TorrentSaveDirectory.Returns(defaultDownloads);

        var torrentPath = Path.Combine(watchDir, "root.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "RootTorrent",
            InfoHash = "root123",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);

        var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { torrentPath, watchDir });

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.Name == "RootTorrent" &&
            string.IsNullOrEmpty(t.Category) &&
            t.SavePath == defaultDownloads));
    }

    [Test]
    public void ProcessTorrentFile_when_file_locked_should_retry_and_handle_gracefully_without_unhandled_exceptions()
    {
        var watchDir = Path.Combine(_tempDir, "watch");
        Directory.CreateDirectory(watchDir);
        _configService.WatchFolderPath.Returns(watchDir);

        var torrentPath = Path.Combine(watchDir, "locked.torrent");
        File.WriteAllBytes(torrentPath, new byte[] { 1, 2, 3, 4 });

        using (var lockStream = new FileStream(torrentPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.DoesNotThrow(() => method.Invoke(_subject, new object[] { torrentPath, watchDir }));
        }

        _parser.DidNotReceive().Parse(Arg.Any<string>());
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void WaitForFileReady_should_return_false_when_file_is_locked()
    {
        var torrentPath = Path.Combine(_tempDir, "exclusively_locked.torrent");
        File.WriteAllBytes(torrentPath, new byte[] { 1, 2, 3 });

        using var lockStream = new FileStream(torrentPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = _subject.WaitForFileReady(torrentPath, maxAttempts: 2, initialDelayMs: 10, stabilityDelayMs: 5);

        Assert.That(result, Is.False);
    }

    [Test]
    public void WaitForFileReady_should_return_true_when_file_is_unlocked_and_stable()
    {
        var torrentPath = Path.Combine(_tempDir, "ready.torrent");
        File.WriteAllBytes(torrentPath, new byte[] { 1, 2, 3 });

        var result = _subject.WaitForFileReady(torrentPath, maxAttempts: 2, initialDelayMs: 10, stabilityDelayMs: 5);

        Assert.That(result, Is.True);
    }

    [Test]
    public void WaitForFileReady_should_succeed_after_lock_is_released_during_retry()
    {
        var torrentPath = Path.Combine(_tempDir, "unlocking.torrent");
        File.WriteAllBytes(torrentPath, new byte[] { 1, 2, 3 });

        var lockStream = new FileStream(torrentPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Task.Delay(30).ContinueWith(_ => lockStream.Dispose());

        var result = _subject.WaitForFileReady(torrentPath, maxAttempts: 5, initialDelayMs: 25, stabilityDelayMs: 5);

        Assert.That(result, Is.True);
    }

    [Test]
    public void OnTorrentFileRenamed_should_process_renamed_torrent_file()
    {
        var torrentPath = Path.Combine(_tempDir, "onrenamed.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "OnRenamed",
            InfoHash = "renamed123",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 1 });

        var method = typeof(WatchFolderService).GetMethod("OnTorrentFileRenamed",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var args = new RenamedEventArgs(WatcherChangeTypes.Renamed, _tempDir, "onrenamed.torrent", "onrenamed.torrent.crdownload");

        method.Invoke(_subject, new object[] { null, args });
        Thread.Sleep(700);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.Name == "OnRenamed"));
    }

    [Test]
    public void ProcessMagnetFile_should_detect_parse_and_import_magnet_file()
    {
        var magnetPath = Path.Combine(_tempDir, "sample.magnet");
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu+Linux&tr=http%3A%2F%2Ftracker.example.com%2Fannounce";
        File.WriteAllText(magnetPath, magnetUri);

        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 101, Name = "Ubuntu Linux" });

        var method = typeof(WatchFolderService).GetMethod("ProcessMagnetFile",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { magnetPath, _tempDir });

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.Name == "Ubuntu Linux" &&
            t.InfoHash == "0123456789abcdef0123456789abcdef01234567" &&
            t.MagnetUrl == magnetUri));
        _trackerEntryService.Received(1).Add(Arg.Is<TrackerEntry>(te =>
            te.TorrentId == 101 && te.Url == "http://tracker.example.com/announce"));
    }

    [Test]
    public void ProcessTorrentFile_when_delete_after_add_is_false_should_rename_file_to_imported()
    {
        _configService.WatchFolderDeleteAddedTorrents.Returns(false);

        var torrentPath = Path.Combine(_tempDir, "preserve.torrent");
        CreateDummyTorrentFile(torrentPath);

        var parsed = new ParsedTorrent
        {
            Name = "PreserveTorrent",
            InfoHash = "preserve123",
            TotalSize = 1024,
            PieceCount = 1,
            PieceLength = 1024,
            Files = new List<ParsedTorrentFile>()
        };
        _parser.Parse(torrentPath).Returns(parsed);
        _torrentService.Add(Arg.Any<Torrent>()).Returns(new Torrent { Id = 2 });

        var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { torrentPath, _tempDir });

        Assert.That(File.Exists(torrentPath), Is.False);
        Assert.That(File.Exists(torrentPath + ".imported"), Is.True);
    }

    [Test]
    public void ProcessTorrentFile_when_unparseable_should_rename_file_to_failed()
    {
        var torrentPath = Path.Combine(_tempDir, "corrupted.torrent");
        File.WriteAllText(torrentPath, "not valid bencode data");

        _parser.Parse(torrentPath).Returns(_ => throw new Exception("Corrupted bencode"));

        var method = typeof(WatchFolderService).GetMethod("ProcessTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { torrentPath, _tempDir });

        Assert.That(File.Exists(torrentPath), Is.False);
        Assert.That(File.Exists(torrentPath + ".failed"), Is.True);
    }

    [Test]
    public void ProcessMagnetFile_when_unparseable_should_rename_file_to_failed()
    {
        var magnetPath = Path.Combine(_tempDir, "corrupted.magnet");
        File.WriteAllText(magnetPath, "not a magnet uri");

        var method = typeof(WatchFolderService).GetMethod("ProcessMagnetFile",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { magnetPath, _tempDir });

        Assert.That(File.Exists(magnetPath), Is.False);
        Assert.That(File.Exists(magnetPath + ".failed"), Is.True);
    }

    [Test]
    public void PeriodicScan_should_skip_imported_and_failed_files()
    {
        var watchDir = Path.Combine(_tempDir, "scan-watch");
        Directory.CreateDirectory(watchDir);

        var importedTorrent = Path.Combine(watchDir, "test.torrent.imported");
        var failedTorrent = Path.Combine(watchDir, "test2.torrent.failed");
        var importedMagnet = Path.Combine(watchDir, "test.magnet.imported");
        var failedMagnet = Path.Combine(watchDir, "test2.magnet.failed");

        File.WriteAllText(importedTorrent, "dummy");
        File.WriteAllText(failedTorrent, "dummy");
        File.WriteAllText(importedMagnet, "dummy");
        File.WriteAllText(failedMagnet, "dummy");

        var method = typeof(WatchFolderService).GetMethod("PeriodicScan",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { watchDir });

        _parser.DidNotReceive().Parse(Arg.Any<string>());
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    private static void CreateDummyTorrentFile(string path)
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);
        var info = new BDictionary
        {
            { "name", new BString("dummy") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "length", new BNumber(1024) }
        };
        var torrent = new BDictionary { { "info", info } };
        File.WriteAllBytes(path, torrent.EncodeAsBytes());
    }
}
