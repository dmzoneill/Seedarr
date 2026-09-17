using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents
{
    [TestFixture]
    public class DownloadHistoryServiceTest
    {
        private IDownloadHistoryRepository _historyRepository;
        private ITorrentRepository _torrentRepository;
        private ITrackerEntryRepository _trackerEntryRepository;
        private ICategoryService _categoryService;
        private IDownloadClientFactory _downloadClientFactory;
        private DownloadHistoryService _subject;

        [SetUp]
        public void Setup()
        {
            _historyRepository = Substitute.For<IDownloadHistoryRepository>();
            _torrentRepository = Substitute.For<ITorrentRepository>();
            _trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
            _categoryService = Substitute.For<ICategoryService>();
            _downloadClientFactory = Substitute.For<IDownloadClientFactory>();
            _subject = new DownloadHistoryService(_historyRepository, _torrentRepository, _trackerEntryRepository, _categoryService, _downloadClientFactory);
        }

        [Test]
        public void RecordTorrentAdded_should_insert_new_history_entry()
        {
            var torrent = new Torrent
            {
                Id = 1,
                Name = "Ubuntu 24.04",
                InfoHash = "abc123hash",
                TotalSize = 1024000,
                TrackerUrl = "http://tracker.example.com",
                DateAdded = DateTime.UtcNow,
                SavePath = "/downloads/linux",
                Category = "OS",
                DownloadClientId = 2,
                SourcePath = "/downloads/linux/ubuntu.iso"
            };

            _historyRepository.FindByInfoHash("abc123hash").Returns((DownloadHistory)null);
            _historyRepository.Insert(Arg.Any<DownloadHistory>()).Returns(x => (DownloadHistory)x[0]);

            var result = _subject.RecordTorrentAdded(torrent, source: "Prowlarr", magnetUrl: "magnet:?xt=urn:btih:abc123hash", downloadUrl: "http://example.com/dl");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Title, Is.EqualTo("Ubuntu 24.04"));
            Assert.That(result.InfoHash, Is.EqualTo("abc123hash"));
            Assert.That(result.Source, Is.EqualTo("Prowlarr"));
            Assert.That(result.Status, Is.EqualTo("Active"));
            Assert.That(result.SavePath, Is.EqualTo("/downloads/linux"));
            Assert.That(result.Category, Is.EqualTo("OS"));
            Assert.That(result.DownloadClientId, Is.EqualTo(2));
            Assert.That(result.SourcePath, Is.EqualTo("/downloads/linux/ubuntu.iso"));
            Assert.That(result.MagnetUrl, Is.EqualTo("magnet:?xt=urn:btih:abc123hash"));
            Assert.That(result.DownloadUrl, Is.EqualTo("http://example.com/dl"));
            Assert.That(result.DataJson, Does.Contain("savePath"));
            _historyRepository.Received(1).Insert(Arg.Is<DownloadHistory>(h => h.InfoHash == "abc123hash" && h.Status == "Active"));
        }

        [Test]
        public void AddMany_should_enrich_and_call_repository_insert_many()
        {
            var entries = new List<DownloadHistory>
            {
                new() { InfoHash = "abc123hash", SavePath = "/downloads/linux" }
            };

            _subject.AddMany(entries);

            _historyRepository.Received(1).InsertMany(Arg.Is<IList<DownloadHistory>>(list =>
                list.Count == 1 && list[0].DataJson.Contains("savePath")));
        }

        [Test]
        public void RecordTorrentRemoved_should_mark_entry_removed()
        {
            var torrent = new Torrent
            {
                Id = 1,
                Name = "Ubuntu 24.04",
                InfoHash = "abc123hash",
                Uploaded = 5000,
                Downloaded = 1000,
                Ratio = 5.0,
                SeedingTime = 3600,
                SavePath = "/downloads/linux",
                Category = "OS",
                DownloadClientId = 2,
                SourcePath = "/downloads/linux/ubuntu.iso"
            };

            var existing = new DownloadHistory
            {
                Id = 10,
                TorrentId = 1,
                InfoHash = "abc123hash",
                Status = "Active"
            };

            _historyRepository.FindByTorrentId(1).Returns(existing);

            _subject.RecordTorrentRemoved(torrent, "Deleted by user");

            Assert.That(existing.TorrentId, Is.Null);
            Assert.That(existing.Status, Is.EqualTo("Removed"));
            Assert.That(existing.DateRemoved, Is.Not.Null);
            Assert.That(existing.Uploaded, Is.EqualTo(5000));
            Assert.That(existing.Ratio, Is.EqualTo(5.0));
            Assert.That(existing.SavePath, Is.EqualTo("/downloads/linux"));
            Assert.That(existing.Category, Is.EqualTo("OS"));
            Assert.That(existing.DownloadClientId, Is.EqualTo(2));
            Assert.That(existing.SourcePath, Is.EqualTo("/downloads/linux/ubuntu.iso"));
            _historyRepository.Received(1).Update(existing);
        }

        [Test]
        public void ReAdd_should_insert_torrent_into_repository_and_activate_history()
        {
            var history = new DownloadHistory
            {
                Id = 5,
                Title = "Ubuntu 24.04",
                InfoHash = "abc123hash",
                TotalSize = 1024000,
                PrimaryTracker = "http://tracker.example.com",
                Status = "Removed",
                SavePath = "/downloads/isos",
                Category = "Linux",
                DownloadClientId = 3,
                SourcePath = "/downloads/isos/ubuntu.iso",
                MagnetUrl = "magnet:?xt=urn:btih:0123456789012345678901234567890123456789&dn=Ubuntu",
                DownloadUrl = "http://example.com/ubuntu.torrent"
            };

            _historyRepository.Get(5).Returns(history);
            _torrentRepository.ExistsByInfoHash("abc123hash").Returns(false);
            _torrentRepository.All().Returns(new List<Torrent>().AsQueryable());
            _torrentRepository.Insert(Arg.Any<Torrent>()).Returns(callInfo =>
            {
                var t = callInfo.Arg<Torrent>();
                t.Id = 42;
                return t;
            });

            var readded = _subject.ReAdd(5);

            Assert.That(readded, Is.Not.Null);
            Assert.That(readded.Id, Is.EqualTo(42));
            Assert.That(readded.SavePath, Is.EqualTo("/downloads/isos"));
            Assert.That(readded.Category, Is.EqualTo("Linux"));
            Assert.That(readded.DownloadClientId, Is.EqualTo(3));
            Assert.That(readded.SourcePath, Is.EqualTo("/downloads/isos/ubuntu.iso"));
            Assert.That(readded.MagnetUrl, Is.EqualTo("magnet:?xt=urn:btih:0123456789012345678901234567890123456789&dn=Ubuntu"));
            Assert.That(readded.DownloadUrl, Is.EqualTo("http://example.com/ubuntu.torrent"));
            Assert.That(history.Status, Is.EqualTo("Active"));
            Assert.That(history.TorrentId, Is.EqualTo(42));

            _torrentRepository.Received(1).Insert(Arg.Is<Torrent>(t =>
                t.InfoHash == "abc123hash" &&
                t.SavePath == "/downloads/isos" &&
                t.Category == "Linux" &&
                t.DownloadClientId == 3 &&
                t.SourcePath == "/downloads/isos/ubuntu.iso" &&
                t.MagnetUrl == "magnet:?xt=urn:btih:0123456789012345678901234567890123456789&dn=Ubuntu" &&
                t.DownloadUrl == "http://example.com/ubuntu.torrent"));
            _historyRepository.Received(1).Update(history);
        }

        [Test]
        public void ReAdd_should_fallback_savepath_to_category_service()
        {
            var history = new DownloadHistory
            {
                Id = 6,
                Title = "Fedora 40",
                InfoHash = "def456hash",
                Category = "Linux",
                SavePath = null
            };

            _historyRepository.Get(6).Returns(history);
            _torrentRepository.ExistsByInfoHash("def456hash").Returns(false);
            _torrentRepository.All().Returns(new List<Torrent>().AsQueryable());
            _categoryService.GetSavePathForCategory("Linux").Returns("/data/categories/linux");
            _torrentRepository.Insert(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

            var readded = _subject.ReAdd(6);

            Assert.That(readded.SavePath, Is.EqualTo("/data/categories/linux"));
            Assert.That(readded.SourcePath, Is.EqualTo("/data/categories/linux"));
            _categoryService.Received(1).GetSavePathForCategory("Linux");
        }

        [Test]
        public void ReAdd_should_not_crash_with_null_savepath_when_category_absent()
        {
            var history = new DownloadHistory
            {
                Id = 7,
                Title = "Arch Linux",
                InfoHash = "archhash",
                Category = null,
                SavePath = null,
                SourcePath = null
            };

            _historyRepository.Get(7).Returns(history);
            _torrentRepository.ExistsByInfoHash("archhash").Returns(false);
            _torrentRepository.All().Returns(new List<Torrent>().AsQueryable());
            _torrentRepository.Insert(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

            var readded = _subject.ReAdd(7);

            Assert.That(readded.SavePath, Is.EqualTo(string.Empty));
            Assert.That(readded.SourcePath, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ReAdd_should_restore_fields_from_DataJson()
        {
            var history = new DownloadHistory
            {
                Id = 8,
                Title = "Debian 12",
                InfoHash = "debianhash",
                DataJson = "{\"savePath\":\"/mnt/storage\",\"category\":\"Debian\",\"downloadClientId\":5,\"sourcePath\":\"/mnt/storage/debian.iso\"}"
            };

            _historyRepository.Get(8).Returns(history);
            _torrentRepository.ExistsByInfoHash("debianhash").Returns(false);
            _torrentRepository.All().Returns(new List<Torrent>().AsQueryable());
            _torrentRepository.Insert(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

            var readded = _subject.ReAdd(8);

            Assert.That(readded.SavePath, Is.EqualTo("/mnt/storage"));
            Assert.That(readded.Category, Is.EqualTo("Debian"));
            Assert.That(readded.DownloadClientId, Is.EqualTo(5));
            Assert.That(readded.SourcePath, Is.EqualTo("/mnt/storage/debian.iso"));
        }

        [Test]
        public void ReAdd_should_throw_if_already_in_library_by_info_hash()
        {
            var history = new DownloadHistory
            {
                Id = 5,
                Title = "Ubuntu 24.04",
                InfoHash = "abc123hash"
            };

            _historyRepository.Get(5).Returns(history);
            _torrentRepository.ExistsByInfoHash("abc123hash").Returns(true);

            var ex = Assert.Throws<InvalidOperationException>(() => _subject.ReAdd(5));
            Assert.That(ex.Message, Does.Contain("abc123hash"));
            Assert.That(ex.Message, Does.Contain("Ubuntu 24.04"));
        }

        [Test]
        public void ReAdd_should_throw_if_already_in_library_by_torrent_id()
        {
            var history = new DownloadHistory
            {
                Id = 5,
                Title = "Ubuntu 24.04",
                InfoHash = "abc123hash",
                TorrentId = 99
            };

            _historyRepository.Get(5).Returns(history);
            _torrentRepository.ExistsByInfoHash("abc123hash").Returns(false);
            _torrentRepository.Get(99).Returns(new Torrent { Id = 99, Name = "Ubuntu 24.04" });

            var ex = Assert.Throws<InvalidOperationException>(() => _subject.ReAdd(5));
            Assert.That(ex.Message, Does.Contain("99"));
        }

        [Test]
        public void ReAdd_should_throw_if_tracked_in_active_download_client()
        {
            var history = new DownloadHistory
            {
                Id = 5,
                Title = "Ubuntu 24.04",
                InfoHash = "abc123hash"
            };

            _historyRepository.Get(5).Returns(history);
            _torrentRepository.ExistsByInfoHash("abc123hash").Returns(false);

            var clientDef = new DownloadClientDefinition
            {
                Id = 1,
                Name = "qBittorrent-Local",
                Enable = true,
                ClientType = "QBitTorrent"
            };
            _downloadClientFactory.All().Returns(new List<DownloadClientDefinition> { clientDef });

            var clientMock = Substitute.For<IDownloadClient>();
            clientMock.GetItems().Returns(new List<DownloadClientItem>
            {
                new DownloadClientItem { InfoHash = "abc123hash", Title = "Ubuntu 24.04" }
            });
            _downloadClientFactory.CreateClient(clientDef).Returns(clientMock);

            var ex = Assert.Throws<InvalidOperationException>(() => _subject.ReAdd(5));
            Assert.That(ex.Message, Does.Contain("already tracked in download client"));
            Assert.That(ex.Message, Does.Contain("qBittorrent-Local"));
        }

        [Test]
        public void ClearAll_should_call_repository_DeleteAll()
        {
            _subject.ClearAll();

            _historyRepository.Received(1).DeleteAll();
        }

        [Test]
        public void RecordTorrentAdded_when_called_concurrently_should_not_create_duplicate_records()
        {
            var storedEntries = new List<DownloadHistory>();
            var repoLock = new object();

            _historyRepository.FindByInfoHash(Arg.Any<string>()).Returns(x =>
            {
                lock (repoLock)
                {
                    var hash = (string)x[0];
                    return storedEntries.FirstOrDefault(e => e.InfoHash == hash);
                }
            });

            _historyRepository.Insert(Arg.Any<DownloadHistory>()).Returns(x =>
            {
                lock (repoLock)
                {
                    var entry = (DownloadHistory)x[0];
                    storedEntries.Add(entry);
                    return entry;
                }
            });

            _historyRepository.When(r => r.Update(Arg.Any<DownloadHistory>())).Do(x =>
            {
                lock (repoLock)
                {
                    var entry = (DownloadHistory)x[0];
                    var idx = storedEntries.FindIndex(e => e.InfoHash == entry.InfoHash);
                    if (idx >= 0)
                    {
                        storedEntries[idx] = entry;
                    }
                }
            });

            var torrent = new Torrent
            {
                Id = 1,
                Name = "Test Torrent",
                InfoHash = "concurrent_info_hash",
                TotalSize = 1000
            };

            var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
            {
                _subject.RecordTorrentAdded(torrent);
            })).ToArray();

            Task.WaitAll(tasks);

            _historyRepository.Received(1).Insert(Arg.Any<DownloadHistory>());
            Assert.That(storedEntries.Count(e => e.InfoHash == "concurrent_info_hash"), Is.EqualTo(1));
        }

        [TestCase(TorrentStatus.Paused, "Paused")]
        [TestCase(TorrentStatus.Downloading, "Downloading")]
        [TestCase(TorrentStatus.Checking, "Checking")]
        public void Handle_TorrentStatusChangedEvent_should_update_status(TorrentStatus status, string expectedStatus)
        {
            var torrent = new Torrent
            {
                Id = 10,
                InfoHash = "hash123",
                Name = "Test Torrent"
            };

            var entry = new DownloadHistory
            {
                TorrentId = 10,
                InfoHash = "hash123",
                Status = "Active"
            };

            _historyRepository.FindByTorrentId(10).Returns(entry);

            var evt = new TorrentStatusChangedEvent(torrent, TorrentStatus.Downloading, status);
            _subject.Handle(evt);

            Assert.That(entry.Status, Is.EqualTo(expectedStatus));
            _historyRepository.Received(1).Update(entry);
        }

        [Test]
        public void Handle_TorrentStatusChangedEvent_should_record_error_message_in_removal_reason_and_data_json()
        {
            var torrent = new Torrent
            {
                Id = 20,
                InfoHash = "errorhash",
                Name = "Error Torrent"
            };

            var entry = new DownloadHistory
            {
                TorrentId = 20,
                InfoHash = "errorhash",
                Status = "Downloading"
            };

            _historyRepository.FindByTorrentId(20).Returns(entry);

            var evt = new TorrentStatusChangedEvent(torrent, TorrentStatus.Downloading, TorrentStatus.Error, "Tracker unreachable");
            _subject.Handle(evt);

            Assert.That(entry.Status, Is.EqualTo("Error"));
            Assert.That(entry.RemovalReason, Is.EqualTo("Tracker unreachable"));
            Assert.That(entry.DataJson, Does.Contain("Tracker unreachable"));
            Assert.That(entry.DataJson, Does.Contain("errorMessage"));
            _historyRepository.Received(1).Update(entry);
        }

        [Test]
        public void Handle_TorrentStatusChangedEvent_should_record_torrent_error_message_if_event_error_message_is_empty()
        {
            var torrent = new Torrent
            {
                Id = 21,
                InfoHash = "errorhash2",
                Name = "Error Torrent 2",
                ErrorMessage = "Disk write error"
            };

            var entry = new DownloadHistory
            {
                TorrentId = 21,
                InfoHash = "errorhash2",
                Status = "Downloading"
            };

            _historyRepository.FindByTorrentId(21).Returns(entry);

            var evt = new TorrentStatusChangedEvent(torrent, TorrentStatus.Downloading, TorrentStatus.Error);
            _subject.Handle(evt);

            Assert.That(entry.Status, Is.EqualTo("Error"));
            Assert.That(entry.RemovalReason, Is.EqualTo("Disk write error"));
            Assert.That(entry.DataJson, Does.Contain("Disk write error"));
            Assert.That(entry.DataJson, Does.Contain("errorMessage"));
            _historyRepository.Received(1).Update(entry);
        }
    }
}
