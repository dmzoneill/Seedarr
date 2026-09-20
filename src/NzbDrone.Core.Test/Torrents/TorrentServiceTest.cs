using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents
{
    [TestFixture]
    public class TorrentServiceTest
    {
        private ITorrentRepository _repository;
        private ITorrentFileService _torrentFileService;
        private ITrackerEntryService _trackerEntryService;
        private IEventAggregator _eventAggregator;
        private TorrentService _subject;

        [SetUp]
        public void Setup()
        {
            _repository = Substitute.For<ITorrentRepository>();
            _torrentFileService = Substitute.For<ITorrentFileService>();
            _trackerEntryService = Substitute.For<ITrackerEntryService>();
            _eventAggregator = Substitute.For<IEventAggregator>();
            _subject = new TorrentService(_repository, _torrentFileService, _trackerEntryService, _eventAggregator);
        }

        [Test]
        public void GetAll_should_return_all_torrents_from_repository()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, Name = "Torrent1" },
                new Torrent { Id = 2, Name = "Torrent2" }
            };
            _repository.All().Returns(torrents.AsQueryable());

            var result = _subject.GetAll();

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].Id, Is.EqualTo(1));
            Assert.That(result[1].Id, Is.EqualTo(2));
        }

        [Test]
        public void GetAll_should_return_empty_list_when_no_torrents()
        {
            _repository.All().Returns(new List<Torrent>().AsQueryable());

            var result = _subject.GetAll();

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Get_should_return_torrent_by_id()
        {
            var torrent = new Torrent { Id = 1, Name = "Torrent1" };
            _repository.Get(1).Returns(torrent);

            var result = _subject.Get(1);

            Assert.That(result, Is.EqualTo(torrent));
        }

        [Test]
        public void ExistsByInfoHash_should_return_true_when_exists()
        {
            _repository.ExistsByInfoHash("hash123").Returns(true);

            var result = _subject.ExistsByInfoHash("hash123");

            Assert.That(result, Is.True);
        }

        [Test]
        public void ExistsByInfoHash_should_return_false_when_not_exists()
        {
            _repository.ExistsByInfoHash("hash123").Returns(false);

            var result = _subject.ExistsByInfoHash("hash123");

            Assert.That(result, Is.False);
        }

        [Test]
        public void ExistsByInfoHash_matches_uppercase_query_against_lowercase_record()
        {
            _repository.ExistsByInfoHash("hash123").Returns(true);

            var result = _subject.ExistsByInfoHash("HASH123");

            Assert.That(result, Is.True);
            _repository.Received(1).ExistsByInfoHash("hash123");
        }

        [Test]
        public void Add_should_set_SortOrder_to_0_when_no_existing_torrents()
        {
            var torrent = new Torrent { Name = "New Torrent" };
            _repository.GetNextSortOrder().Returns(0);
            _repository.Insert(Arg.Any<Torrent>()).Returns(torrent);

            _subject.Add(torrent);

            Assert.That(torrent.SortOrder, Is.EqualTo(0));
            _repository.DidNotReceive().All();
        }

        [Test]
        public void Add_should_set_SortOrder_to_max_plus_one_when_existing_torrents()
        {
            var torrent = new Torrent { Name = "New Torrent" };
            _repository.GetNextSortOrder().Returns(3);
            _repository.Insert(Arg.Any<Torrent>()).Returns(torrent);

            _subject.Add(torrent);

            Assert.That(torrent.SortOrder, Is.EqualTo(3));
            _repository.Received(1).GetNextSortOrder();
            _repository.DidNotReceive().All();
        }

        [Test]
        public void Add_should_calculate_next_SortOrder_using_repository_query_without_calling_All()
        {
            var torrent = new Torrent { Name = "New Torrent", InfoHash = "abcd1234efgh" };
            _repository.ExistsByInfoHash("abcd1234efgh").Returns(false);
            _repository.GetNextSortOrder().Returns(42);
            _repository.Insert(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

            var result = _subject.Add(torrent);

            Assert.That(result.SortOrder, Is.EqualTo(42));
            _repository.Received(1).GetNextSortOrder();
            _repository.DidNotReceive().All();
        }

        [Test]
        public void Add_should_throw_DuplicateTorrentException_when_InfoHash_already_exists()
        {
            var infoHash = "0123456789abcdef0123456789abcdef01234567";
            var torrent = new Torrent { Name = "Duplicate Torrent", InfoHash = infoHash };
            _repository.ExistsByInfoHash(infoHash).Returns(true);

            var ex = Assert.Throws<DuplicateTorrentException>(() => _subject.Add(torrent));

            Assert.That(ex.InfoHash, Is.EqualTo(infoHash));
            _repository.DidNotReceive().Insert(Arg.Any<Torrent>());
            _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<TorrentAddedEvent>());
        }

        [Test]
        public void Add_throws_DuplicateTorrentException_when_uppercase_info_hash_already_exists()
        {
            const string existingLower = "a1b2c3d4e5f60123456789abcdef0123456789ab";
            const string incomingUpper = "A1B2C3D4E5F60123456789ABCDEF0123456789AB";

            _repository.ExistsByInfoHash(existingLower).Returns(true);

            var torrent = new Torrent { Name = "Uppercase Hash Torrent", InfoHash = incomingUpper };

            var ex = Assert.Throws<DuplicateTorrentException>(() => _subject.Add(torrent));

            Assert.That(ex.InfoHash, Is.EqualTo(existingLower));
            _repository.DidNotReceive().Insert(Arg.Any<Torrent>());
            _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<TorrentAddedEvent>());
        }

        [Test]
        public void Add_persists_normalized_lowercase_InfoHash()
        {
            const string incomingMixed = "  A1B2C3D4E5F60123456789ABCDEF0123456789AB  ";
            const string expectedNormalized = "a1b2c3d4e5f60123456789abcdef0123456789ab";

            _repository.ExistsByInfoHash(expectedNormalized).Returns(false);
            _repository.GetNextSortOrder().Returns(1);
            _repository.Insert(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

            var torrent = new Torrent { Name = "Mixed Hash Torrent", InfoHash = incomingMixed };
            var result = _subject.Add(torrent);

            Assert.That(result.InfoHash, Is.EqualTo(expectedNormalized));
            _repository.Received(1).Insert(Arg.Is<Torrent>(t => t.InfoHash == expectedNormalized));
        }

        [Test]
        public async Task Add_concurrent_additions_should_assign_monotonically_increasing_SortOrder_without_collisions()
        {
            var currentSortOrder = 0;
            var assignedSortOrders = new System.Collections.Concurrent.ConcurrentBag<int>();

            _repository.GetNextSortOrder().Returns(_ =>
            {
                Thread.Sleep(5);
                return currentSortOrder++;
            });

            _repository.Insert(Arg.Any<Torrent>()).Returns(callInfo =>
            {
                var t = callInfo.Arg<Torrent>();
                assignedSortOrders.Add(t.SortOrder);
                return t;
            });

            const int count = 20;
            var tasks = Enumerable.Range(0, count).Select(i => Task.Run(() =>
            {
                var torrent = new Torrent { Name = $"Torrent {i}", InfoHash = $"hash_{i}" };
                _subject.Add(torrent);
            })).ToArray();

            await Task.WhenAll(tasks);

            Assert.That(assignedSortOrders.Count, Is.EqualTo(count));
            var sorted = assignedSortOrders.OrderBy(x => x).ToList();
            Assert.That(sorted, Is.EqualTo(Enumerable.Range(0, count).ToList()), "SortOrders should be unique and monotonically increasing without collisions");
        }

        [Test]
        public async Task Add_concurrent_duplicate_additions_should_throw_DuplicateTorrentException_for_second_caller()
        {
            const string infoHash = "shared_hash_123";
            var exists = false;

            _repository.ExistsByInfoHash(infoHash).Returns(_ => exists);
            _repository.Insert(Arg.Any<Torrent>()).Returns(callInfo =>
            {
                exists = true;
                return callInfo.Arg<Torrent>();
            });

            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            var successCount = 0;

            var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(() =>
            {
                try
                {
                    var torrent = new Torrent { Name = "Concurrent Dup", InfoHash = infoHash };
                    _subject.Add(torrent);
                    Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            Assert.That(successCount, Is.EqualTo(1));
            Assert.That(exceptions.Count, Is.EqualTo(4));
            Assert.That(exceptions.All(e => e is DuplicateTorrentException), Is.True);
        }

        [Test]
        public void Add_should_call_repository_Insert()
        {
            var torrent = new Torrent { Name = "New Torrent" };
            _repository.Insert(torrent).Returns(torrent);

            _subject.Add(torrent);

            _repository.Received(1).Insert(torrent);
        }

        [Test]
        public void Add_should_publish_TorrentAddedEvent()
        {
            var torrent = new Torrent { Name = "New Torrent" };
            _repository.Insert(torrent).Returns(torrent);

            _subject.Add(torrent);

            _eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentAddedEvent>());
        }

        [Test]
        public void Update_should_return_updated_torrent_and_publish_events()
        {
            var torrent = new Torrent { Id = 1, Name = "Updated" };
            _repository.Update(torrent).Returns(torrent);

            var result = _subject.Update(torrent);

            Assert.That(result, Is.EqualTo(torrent));
            _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == torrent));
            _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Torrent>>(e => e.Model == torrent && e.Action == ModelAction.Updated));
        }

        [Test]
        public void Delete_should_call_DeleteByTorrentId_on_file_service()
        {
            _subject.Delete(1);

            _torrentFileService.Received(1).DeleteByTorrentId(1);
        }

        [Test]
        public void Delete_should_call_DeleteByTorrentId_on_tracker_service()
        {
            _subject.Delete(1);

            _trackerEntryService.Received(1).DeleteByTorrentId(1);
        }

        [Test]
        public void Delete_should_call_repository_Delete()
        {
            _subject.Delete(1);

            _repository.Received(1).Delete(1);
        }

        [Test]
        public void Delete_should_publish_TorrentDeletedEvent()
        {
            _subject.Delete(1);

            _eventAggregator.Received(1).PublishEvent(Arg.Any<TorrentDeletedEvent>());
        }

        [Test]
        public void Delete_should_look_up_torrent_to_publish_model_event()
        {
            _subject.Delete(1, false);

            _repository.Received(1).Get(1);
        }

        [Test]
        public void Delete_should_proceed_when_deleteFiles_true_and_torrent_not_found()
        {
            _repository.Get(1).Returns((Torrent)null);

            Assert.DoesNotThrow(() => _subject.Delete(1, true));
            _repository.Received(1).Delete(1);
        }

        [Test]
        public void Delete_should_proceed_when_deleteFiles_true_and_null_SourcePath()
        {
            var torrent = new Torrent { Id = 1, SourcePath = null };
            _repository.Get(1).Returns(torrent);

            Assert.DoesNotThrow(() => _subject.Delete(1, true));
            _repository.Received(1).Delete(1);
        }

        [Test]
        public void Delete_should_delete_payload_files_and_source_file_when_deleteFiles_is_true()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var sourceFile = Path.Combine(tempDir, "test.torrent");
                var payloadFile = Path.Combine(tempDir, "payload.mkv");
                File.WriteAllText(sourceFile, "dummy torrent file");
                File.WriteAllText(payloadFile, "dummy video file");

                var torrent = new Torrent
                {
                    Id = 1,
                    Name = "TestTorrent",
                    SavePath = tempDir,
                    SourcePath = sourceFile
                };
                var file = new TorrentFile
                {
                    TorrentId = 1,
                    Path = "payload.mkv"
                };

                _repository.Get(1).Returns(torrent);
                _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

                _subject.Delete(1, deleteFiles: true);

                Assert.That(File.Exists(sourceFile), Is.False);
                Assert.That(File.Exists(payloadFile), Is.False);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Test]
        public void Delete_should_preserve_payload_files_when_deleteFiles_is_false()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var sourceFile = Path.Combine(tempDir, "test.torrent");
                var payloadFile = Path.Combine(tempDir, "payload.mkv");
                File.WriteAllText(sourceFile, "dummy torrent file");
                File.WriteAllText(payloadFile, "dummy video file");

                var torrent = new Torrent
                {
                    Id = 1,
                    Name = "TestTorrent",
                    SavePath = tempDir,
                    SourcePath = sourceFile
                };
                var file = new TorrentFile
                {
                    TorrentId = 1,
                    Path = "payload.mkv"
                };

                _repository.Get(1).Returns(torrent);
                _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

                _subject.Delete(1, deleteFiles: false);

                Assert.That(File.Exists(sourceFile), Is.True);
                Assert.That(File.Exists(payloadFile), Is.True);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Test]
        public void Delete_should_reject_and_guard_against_path_traversal_in_files()
        {
            var tempBase = Path.Combine(Path.GetTempPath(), "seedarr_test_" + Guid.NewGuid().ToString("N"));
            var saveDir = Path.Combine(tempBase, "save_dir");
            var outsideDir = Path.Combine(tempBase, "outside");
            Directory.CreateDirectory(saveDir);
            Directory.CreateDirectory(outsideDir);

            try
            {
                var sensitiveFile = Path.Combine(outsideDir, "sensitive.txt");
                File.WriteAllText(sensitiveFile, "secret data");

                var torrent = new Torrent
                {
                    Id = 1,
                    Name = "TestTorrent",
                    SavePath = saveDir
                };
                var traversalFile = new TorrentFile
                {
                    TorrentId = 1,
                    Path = "../outside/sensitive.txt"
                };

                _repository.Get(1).Returns(torrent);
                _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { traversalFile });

                _subject.Delete(1, deleteFiles: true);

                Assert.That(File.Exists(sensitiveFile), Is.True);
            }
            finally
            {
                if (Directory.Exists(tempBase))
                {
                    Directory.Delete(tempBase, true);
                }
            }
        }

        [Test]
        public void Delete_should_not_delete_root_save_path()
        {
            var torrent = new Torrent
            {
                Id = 1,
                Name = "RootTorrent",
                SavePath = "/"
            };
            _repository.Get(1).Returns(torrent);

            Assert.DoesNotThrow(() => _subject.Delete(1, deleteFiles: true));
            _repository.Received(1).Delete(1);
        }

        [Test]
        public void Delete_should_preserve_shared_download_root_when_deleting_single_file_torrent()
        {
            var rootDir = Path.Combine(Path.GetTempPath(), "seedarr_downloads_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootDir);
            try
            {
                var payloadFile = Path.Combine(rootDir, "single_movie.mkv");
                var otherFile = Path.Combine(rootDir, "other_movie.mkv");
                File.WriteAllText(payloadFile, "movie data");
                File.WriteAllText(otherFile, "other movie data");

                var torrent = new Torrent
                {
                    Id = 1,
                    Name = "single_movie.mkv",
                    SavePath = rootDir
                };
                var file = new TorrentFile
                {
                    TorrentId = 1,
                    Path = "single_movie.mkv"
                };

                _repository.Get(1).Returns(torrent);
                _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile> { file });

                _subject.Delete(1, deleteFiles: true);

                Assert.That(File.Exists(payloadFile), Is.False);
                Assert.That(File.Exists(otherFile), Is.True);
                Assert.That(Directory.Exists(rootDir), Is.True);
            }
            finally
            {
                if (Directory.Exists(rootDir))
                {
                    Directory.Delete(rootDir, true);
                }
            }
        }

        [Test]
        public void Delete_should_cleanup_empty_subdirectory_for_multi_file_torrent_without_deleting_root()
        {
            var rootDir = Path.Combine(Path.GetTempPath(), "seedarr_downloads_" + Guid.NewGuid().ToString("N"));
            var subDir = Path.Combine(rootDir, "MyShow");
            Directory.CreateDirectory(subDir);
            try
            {
                var file1 = Path.Combine(subDir, "ep1.mkv");
                var file2 = Path.Combine(subDir, "ep2.mkv");
                File.WriteAllText(file1, "ep1 data");
                File.WriteAllText(file2, "ep2 data");

                var torrent = new Torrent
                {
                    Id = 1,
                    Name = "MyShow",
                    SavePath = rootDir
                };
                var files = new List<TorrentFile>
                {
                    new TorrentFile { TorrentId = 1, Path = Path.Combine("MyShow", "ep1.mkv") },
                    new TorrentFile { TorrentId = 1, Path = Path.Combine("MyShow", "ep2.mkv") }
                };

                _repository.Get(1).Returns(torrent);
                _torrentFileService.GetByTorrentId(1).Returns(files);

                _subject.Delete(1, deleteFiles: true);

                Assert.That(File.Exists(file1), Is.False);
                Assert.That(File.Exists(file2), Is.False);
                Assert.That(Directory.Exists(subDir), Is.False);
                Assert.That(Directory.Exists(rootDir), Is.True);
            }
            finally
            {
                if (Directory.Exists(rootDir))
                {
                    Directory.Delete(rootDir, true);
                }
            }
        }

        [Test]
        public void Recheck_should_return_null_when_torrent_not_found()
        {
            _repository.Get(1).Returns((Torrent)null);

            var result = _subject.Recheck(1);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void Recheck_should_set_Progress_to_1_when_already_at_1()
        {
            var torrent = new Torrent { Id = 1, Progress = 1.0 };
            _repository.Get(1).Returns(torrent);
            _repository.Update(Arg.Any<Torrent>()).Returns(torrent);

            _subject.Recheck(1);

            Assert.That(torrent.Progress, Is.EqualTo(1.0));
        }

        [Test]
        public void Recheck_should_set_Progress_to_1_when_greater_than_1()
        {
            var torrent = new Torrent { Id = 1, Progress = 1.5 };
            _repository.Get(1).Returns(torrent);
            _repository.Update(Arg.Any<Torrent>()).Returns(torrent);

            _subject.Recheck(1);

            Assert.That(torrent.Progress, Is.EqualTo(1.0));
        }

        [Test]
        public void Recheck_should_set_Progress_to_0_when_less_than_1()
        {
            var torrent = new Torrent { Id = 1, Progress = 0.5 };
            _repository.Get(1).Returns(torrent);
            _repository.Update(Arg.Any<Torrent>()).Returns(torrent);

            _subject.Recheck(1);

            Assert.That(torrent.Progress, Is.EqualTo(0.0));
        }

        [Test]
        public void Recheck_should_set_LastActive_to_UtcNow()
        {
            var before = DateTime.UtcNow;
            var torrent = new Torrent { Id = 1, Progress = 0.5 };
            _repository.Get(1).Returns(torrent);
            _repository.Update(Arg.Any<Torrent>()).Returns(torrent);

            _subject.Recheck(1);

            var after = DateTime.UtcNow;
            Assert.That(torrent.LastActive, Is.GreaterThanOrEqualTo(before));
            Assert.That(torrent.LastActive, Is.LessThanOrEqualTo(after));
        }

        [Test]
        public void Recheck_should_call_repository_Update()
        {
            var torrent = new Torrent { Id = 1, Progress = 0.5 };
            _repository.Get(1).Returns(torrent);
            _repository.Update(torrent).Returns(torrent);

            _subject.Recheck(1);

            _repository.Received(1).Update(torrent);
        }

        [Test]
        public void MoveQueue_should_do_nothing_when_torrent_not_found()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.MoveQueue(999, "top");

            _repository.DidNotReceive().Update(Arg.Any<Torrent>());
            _repository.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<Torrent>>());
        }

        [Test]
        public void MoveQueue_should_move_to_top()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 },
                new Torrent { Id = 3, SortOrder = 2 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.MoveQueue(3, "top");

            Assert.That(torrents[2].SortOrder, Is.EqualTo(0));
            Assert.That(torrents[0].SortOrder, Is.EqualTo(1));
            Assert.That(torrents[1].SortOrder, Is.EqualTo(2));
            _repository.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
                items.Count() == 3 &&
                items.Any(t => t.Id == 3 && t.SortOrder == 0) &&
                items.Any(t => t.Id == 1 && t.SortOrder == 1) &&
                items.Any(t => t.Id == 2 && t.SortOrder == 2)));
            _repository.DidNotReceive().Update(Arg.Any<Torrent>());
        }

        [Test]
        public void MoveQueue_should_move_up_by_one()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 },
                new Torrent { Id = 3, SortOrder = 2 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.MoveQueue(2, "up");

            Assert.That(torrents[1].SortOrder, Is.EqualTo(0));
            Assert.That(torrents[0].SortOrder, Is.EqualTo(1));
            Assert.That(torrents[2].SortOrder, Is.EqualTo(2));
            _repository.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
                items.Count() == 2 &&
                items.Any(t => t.Id == 2 && t.SortOrder == 0) &&
                items.Any(t => t.Id == 1 && t.SortOrder == 1) &&
                !items.Any(t => t.Id == 3)));
            _repository.DidNotReceive().Update(Arg.Any<Torrent>());
        }

        [Test]
        public void MoveQueue_should_move_down_by_one()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 },
                new Torrent { Id = 3, SortOrder = 2 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.MoveQueue(2, "down");

            Assert.That(torrents[1].SortOrder, Is.EqualTo(2));
            Assert.That(torrents[0].SortOrder, Is.EqualTo(0));
            Assert.That(torrents[2].SortOrder, Is.EqualTo(1));
            _repository.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
                items.Count() == 2 &&
                items.Any(t => t.Id == 2 && t.SortOrder == 2) &&
                items.Any(t => t.Id == 3 && t.SortOrder == 1) &&
                !items.Any(t => t.Id == 1)));
            _repository.DidNotReceive().Update(Arg.Any<Torrent>());
        }

        [Test]
        public void MoveQueue_should_move_to_bottom()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 },
                new Torrent { Id = 3, SortOrder = 2 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.MoveQueue(1, "bottom");

            Assert.That(torrents[0].SortOrder, Is.EqualTo(2));
            Assert.That(torrents[1].SortOrder, Is.EqualTo(0));
            Assert.That(torrents[2].SortOrder, Is.EqualTo(1));
            _repository.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
                items.Count() == 3 &&
                items.Any(t => t.Id == 1 && t.SortOrder == 2) &&
                items.Any(t => t.Id == 2 && t.SortOrder == 0) &&
                items.Any(t => t.Id == 3 && t.SortOrder == 1)));
            _repository.DidNotReceive().Update(Arg.Any<Torrent>());
        }

        [Test]
        public void MoveQueue_should_keep_current_order_for_unknown_position()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.MoveQueue(1, "invalid");

            _repository.DidNotReceive().Update(Arg.Any<Torrent>());
            _repository.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<Torrent>>());
        }

        [Test]
        public void MoveQueue_with_colliding_sort_orders_should_stabilize_order_by_id_and_batch_update()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 30, SortOrder = 5 },
                new Torrent { Id = 10, SortOrder = 5 },
                new Torrent { Id = 20, SortOrder = 5 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.MoveQueue(30, "top");

            Assert.That(torrents.First(t => t.Id == 30).SortOrder, Is.EqualTo(0));
            Assert.That(torrents.First(t => t.Id == 10).SortOrder, Is.EqualTo(1));
            Assert.That(torrents.First(t => t.Id == 20).SortOrder, Is.EqualTo(2));

            _repository.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
                items.Count() == 3 &&
                items.Any(t => t.Id == 30 && t.SortOrder == 0) &&
                items.Any(t => t.Id == 10 && t.SortOrder == 1) &&
                items.Any(t => t.Id == 20 && t.SortOrder == 2)));
            _repository.DidNotReceive().Update(Arg.Any<Torrent>());
        }

        [Test]
        public void BatchMoveQueue_with_colliding_sort_orders_should_stabilize_order_by_id_and_batch_update()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 30, SortOrder = 5 },
                new Torrent { Id = 10, SortOrder = 5 },
                new Torrent { Id = 20, SortOrder = 5 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.BatchMoveQueue(new[] { 20, 30 }, "top");

            Assert.That(torrents.First(t => t.Id == 20).SortOrder, Is.EqualTo(0));
            Assert.That(torrents.First(t => t.Id == 30).SortOrder, Is.EqualTo(1));
            Assert.That(torrents.First(t => t.Id == 10).SortOrder, Is.EqualTo(2));

            _repository.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
                items.Count() == 3 &&
                items.Any(t => t.Id == 20 && t.SortOrder == 0) &&
                items.Any(t => t.Id == 30 && t.SortOrder == 1) &&
                items.Any(t => t.Id == 10 && t.SortOrder == 2)));
        }

        [Test]
        public void BatchMoveQueue_should_move_multiple_items_to_top_and_update_only_changed()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 },
                new Torrent { Id = 3, SortOrder = 2 },
                new Torrent { Id = 4, SortOrder = 3 },
                new Torrent { Id = 5, SortOrder = 4 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.BatchMoveQueue(new[] { 2, 3 }, "top");

            Assert.That(torrents[1].SortOrder, Is.EqualTo(0));
            Assert.That(torrents[2].SortOrder, Is.EqualTo(1));
            Assert.That(torrents[0].SortOrder, Is.EqualTo(2));
            Assert.That(torrents[3].SortOrder, Is.EqualTo(3));
            Assert.That(torrents[4].SortOrder, Is.EqualTo(4));

            _repository.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
                items.Count() == 3 &&
                items.Any(t => t.Id == 2 && t.SortOrder == 0) &&
                items.Any(t => t.Id == 3 && t.SortOrder == 1) &&
                items.Any(t => t.Id == 1 && t.SortOrder == 2) &&
                !items.Any(t => t.Id == 4) &&
                !items.Any(t => t.Id == 5)));
        }

        [Test]
        public void BatchMoveQueue_should_move_multiple_items_to_bottom_and_update_only_changed()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 },
                new Torrent { Id = 3, SortOrder = 2 },
                new Torrent { Id = 4, SortOrder = 3 },
                new Torrent { Id = 5, SortOrder = 4 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.BatchMoveQueue(new[] { 3, 4 }, "bottom");

            Assert.That(torrents[0].SortOrder, Is.EqualTo(0));
            Assert.That(torrents[1].SortOrder, Is.EqualTo(1));
            Assert.That(torrents[4].SortOrder, Is.EqualTo(2));
            Assert.That(torrents[2].SortOrder, Is.EqualTo(3));
            Assert.That(torrents[3].SortOrder, Is.EqualTo(4));

            _repository.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
                items.Count() == 3 &&
                items.Any(t => t.Id == 5 && t.SortOrder == 2) &&
                items.Any(t => t.Id == 3 && t.SortOrder == 3) &&
                items.Any(t => t.Id == 4 && t.SortOrder == 4) &&
                !items.Any(t => t.Id == 1) &&
                !items.Any(t => t.Id == 2)));
        }

        [Test]
        public void BatchMoveQueue_should_move_multiple_items_up_and_update_only_changed()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 },
                new Torrent { Id = 3, SortOrder = 2 },
                new Torrent { Id = 4, SortOrder = 3 },
                new Torrent { Id = 5, SortOrder = 4 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.BatchMoveQueue(new[] { 3, 4 }, "up");

            Assert.That(torrents[0].SortOrder, Is.EqualTo(0));
            Assert.That(torrents[2].SortOrder, Is.EqualTo(1));
            Assert.That(torrents[3].SortOrder, Is.EqualTo(2));
            Assert.That(torrents[1].SortOrder, Is.EqualTo(3));
            Assert.That(torrents[4].SortOrder, Is.EqualTo(4));

            _repository.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
                items.Count() == 3 &&
                items.Any(t => t.Id == 3 && t.SortOrder == 1) &&
                items.Any(t => t.Id == 4 && t.SortOrder == 2) &&
                items.Any(t => t.Id == 2 && t.SortOrder == 3) &&
                !items.Any(t => t.Id == 1) &&
                !items.Any(t => t.Id == 5)));
        }

        [Test]
        public void BatchMoveQueue_should_move_multiple_items_down_and_update_only_changed()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 },
                new Torrent { Id = 3, SortOrder = 2 },
                new Torrent { Id = 4, SortOrder = 3 },
                new Torrent { Id = 5, SortOrder = 4 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.BatchMoveQueue(new[] { 2, 3 }, "down");

            Assert.That(torrents[0].SortOrder, Is.EqualTo(0));
            Assert.That(torrents[3].SortOrder, Is.EqualTo(1));
            Assert.That(torrents[1].SortOrder, Is.EqualTo(2));
            Assert.That(torrents[2].SortOrder, Is.EqualTo(3));
            Assert.That(torrents[4].SortOrder, Is.EqualTo(4));

            _repository.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
                items.Count() == 3 &&
                items.Any(t => t.Id == 4 && t.SortOrder == 1) &&
                items.Any(t => t.Id == 2 && t.SortOrder == 2) &&
                items.Any(t => t.Id == 3 && t.SortOrder == 3) &&
                !items.Any(t => t.Id == 1) &&
                !items.Any(t => t.Id == 5)));
        }

        [Test]
        public void BatchMoveQueue_should_not_update_when_already_at_boundary_or_invalid()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, SortOrder = 0 },
                new Torrent { Id = 2, SortOrder = 1 }
            };
            _repository.All().Returns(torrents.AsQueryable());

            _subject.BatchMoveQueue(new[] { 1 }, "up");
            _subject.BatchMoveQueue(new[] { 2 }, "down");
            _subject.BatchMoveQueue(new[] { 1 }, "invalid");
            _subject.BatchMoveQueue(new List<int>(), "top");

            _repository.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<Torrent>>());
        }

        [Test]
        public void UpdateUserFields_should_apply_user_fields_and_update_repository()
        {
            var existing = new Torrent
            {
                Id = 1,
                Name = "Original",
                Uploaded = 5000,
                Downloaded = 10000,
                Priority = 1
            };
            _repository.Get(1).Returns(existing);
            _repository.Update(existing).Returns(existing);

            var updates = new Torrent
            {
                Name = "Updated Name",
                Priority = 5,
                Uploaded = 0,
                Downloaded = 0
            };

            var result = _subject.UpdateUserFields(1, updates);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("Updated Name"));
            Assert.That(result.Priority, Is.EqualTo(5));
            Assert.That(result.Uploaded, Is.EqualTo(5000));
            Assert.That(result.Downloaded, Is.EqualTo(10000));
            _repository.Received(1).Update(existing);
        }

        [Test]
        public void UpdateUserFields_should_return_null_when_not_found()
        {
            _repository.Get(99).Returns((Torrent)null);

            var result = _subject.UpdateUserFields(99, new Torrent { Name = "Updated" });

            Assert.That(result, Is.Null);
            _repository.DidNotReceive().Update(Arg.Any<Torrent>());
        }

        [Test]
        public void UpdateMany_should_call_repository_UpdateMany()
        {
            var torrents = new List<Torrent>
            {
                new Torrent { Id = 1, Name = "Torrent1" },
                new Torrent { Id = 2, Name = "Torrent2" }
            };

            _subject.UpdateMany(torrents);

            _repository.Received(1).UpdateMany(torrents);
        }

        [Test]
        public void UpdateMany_should_not_call_repository_when_empty()
        {
            _subject.UpdateMany(new List<Torrent>());

            _repository.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<Torrent>>());
        }

        [Test]
        public void GetByInfoHashes_should_call_repository_and_return_results()
        {
            var hashes = new[] { "hash1", "hash2" };
            var expected = new List<Torrent> { new Torrent { Id = 1, InfoHash = "hash1" } };
            _repository.GetByInfoHashes(hashes).Returns(expected);

            var result = _subject.GetByInfoHashes(hashes);

            Assert.That(result, Is.SameAs(expected));
            _repository.Received(1).GetByInfoHashes(hashes);
        }

        [Test]
        public void GetByInfoHashes_should_return_empty_when_hashes_null()
        {
            var result = _subject.GetByInfoHashes(null);

            Assert.That(result, Is.Empty);
            _repository.DidNotReceive().GetByInfoHashes(Arg.Any<IEnumerable<string>>());
        }

        [Test]
        public void GetByInfoHash_should_call_repository_and_return_torrent()
        {
            var torrent = new Torrent { Id = 1, InfoHash = "hash1" };
            _repository.GetByInfoHash("hash1").Returns(torrent);

            var result = _subject.GetByInfoHash("hash1");

            Assert.That(result, Is.SameAs(torrent));
            _repository.Received(1).GetByInfoHash("hash1");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void GetByInfoHash_should_return_null_when_hash_is_empty(string hash)
        {
            var result = _subject.GetByInfoHash(hash);

            Assert.That(result, Is.Null);
            _repository.DidNotReceive().GetByInfoHash(Arg.Any<string>());
        }

        [Test]
        public void Handle_VpnKillSwitchTriggeredEvent_marks_active_torrents_IsVpnPaused_true()
        {
            var downloading = new Torrent { Id = 1, Name = "DownloadingTorrent", Status = TorrentStatus.Downloading, IsVpnPaused = false };
            var seeding = new Torrent { Id = 2, Name = "SeedingTorrent", Status = TorrentStatus.Seeding, IsVpnPaused = false };
            var stopped = new Torrent { Id = 3, Name = "StoppedTorrent", Status = TorrentStatus.Stopped, IsVpnPaused = false };
            var alreadyVpnPaused = new Torrent { Id = 4, Name = "PausedTorrent", Status = TorrentStatus.Downloading, IsVpnPaused = true };

            _repository.All().Returns(new List<Torrent> { downloading, seeding, stopped, alreadyVpnPaused }.AsQueryable());

            _subject.Handle(new VpnKillSwitchTriggeredEvent("tun0"));

            Assert.That(downloading.IsVpnPaused, Is.True);
            Assert.That(seeding.IsVpnPaused, Is.True);
            Assert.That(stopped.IsVpnPaused, Is.False);
            Assert.That(alreadyVpnPaused.IsVpnPaused, Is.True);

            _repository.Received(1).UpdateMany(Arg.Is<IList<Torrent>>(list => list.Count == 2 && list.Contains(downloading) && list.Contains(seeding)));
            _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == downloading));
            _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == seeding));
        }

        [Test]
        public void Handle_VpnInterfaceRestoredEvent_unpauses_torrents()
        {
            var vpnPaused1 = new Torrent { Id = 1, Name = "Torrent1", Status = TorrentStatus.Downloading, IsVpnPaused = true };
            var vpnPaused2 = new Torrent { Id = 2, Name = "Torrent2", Status = TorrentStatus.Seeding, IsVpnPaused = true };
            var normalTorrent = new Torrent { Id = 3, Name = "Torrent3", Status = TorrentStatus.Stopped, IsVpnPaused = false };

            _repository.All().Returns(new List<Torrent> { vpnPaused1, vpnPaused2, normalTorrent }.AsQueryable());

            _subject.Handle(new VpnInterfaceRestoredEvent("tun0"));

            Assert.That(vpnPaused1.IsVpnPaused, Is.False);
            Assert.That(vpnPaused2.IsVpnPaused, Is.False);
            Assert.That(normalTorrent.IsVpnPaused, Is.False);

            _repository.Received(1).UpdateMany(Arg.Is<IList<Torrent>>(list => list.Count == 2 && list.Contains(vpnPaused1) && list.Contains(vpnPaused2)));
            _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == vpnPaused1));
            _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == vpnPaused2));
        }

        [Test]
        public void Handle_DiskSpaceCriticalEvent_should_autopause_active_downloading_torrents_on_volume()
        {
            var downloadingOnVolume = new Torrent
            {
                Id = 1,
                Name = "DownloadingOnVolume",
                Status = TorrentStatus.Downloading,
                SavePath = "/mnt/downloads/movie1",
                Active = true,
            };

            var seedingOnVolume = new Torrent
            {
                Id = 2,
                Name = "SeedingOnVolume",
                Status = TorrentStatus.Seeding,
                SavePath = "/mnt/downloads/movie2",
                Active = true,
            };

            var downloadingOnOther = new Torrent
            {
                Id = 3,
                Name = "DownloadingOnOther",
                Status = TorrentStatus.Downloading,
                SavePath = "/mnt/other/movie3",
                Active = true,
            };

            _repository.All().Returns(new List<Torrent> { downloadingOnVolume, seedingOnVolume, downloadingOnOther }.AsQueryable());

            _subject.Handle(new DiskSpaceCriticalEvent("/mnt/downloads", 100L * 1024 * 1024));

            Assert.That(downloadingOnVolume.Status, Is.EqualTo(TorrentStatus.Paused));
            Assert.That(downloadingOnVolume.ErrorMessage, Is.EqualTo("Paused: Emergency low disk space on volume /mnt/downloads"));
            Assert.That(downloadingOnVolume.Active, Is.False);

            Assert.That(seedingOnVolume.Status, Is.EqualTo(TorrentStatus.Seeding));
            Assert.That(downloadingOnOther.Status, Is.EqualTo(TorrentStatus.Downloading));

            _repository.Received(1).UpdateMany(Arg.Is<IList<Torrent>>(list => list.Count == 1 && list.Contains(downloadingOnVolume)));
            _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == downloadingOnVolume));
        }

        [Test]
        public void Handle_DiskSpaceRestoredEvent_should_resume_autopaused_torrents_on_volume()
        {
            var autoPaused = new Torrent
            {
                Id = 1,
                Name = "AutoPausedTorrent",
                Status = TorrentStatus.Paused,
                SavePath = "/mnt/downloads/movie1",
                ErrorMessage = "Paused: Emergency low disk space on volume /mnt/downloads",
                Progress = 0.5,
            };

            var manuallyPaused = new Torrent
            {
                Id = 2,
                Name = "ManuallyPausedTorrent",
                Status = TorrentStatus.Paused,
                SavePath = "/mnt/downloads/movie2",
                ErrorMessage = "Manual pause",
                Progress = 0.5,
            };

            var autoPausedOtherVolume = new Torrent
            {
                Id = 3,
                Name = "OtherVolumePausedTorrent",
                Status = TorrentStatus.Paused,
                SavePath = "/mnt/other/movie3",
                ErrorMessage = "Paused: Emergency low disk space on volume /mnt/other",
                Progress = 0.5,
            };

            _repository.All().Returns(new List<Torrent> { autoPaused, manuallyPaused, autoPausedOtherVolume }.AsQueryable());

            _subject.Handle(new DiskSpaceRestoredEvent("/mnt/downloads", 50L * 1024 * 1024 * 1024, 100L * 1024 * 1024 * 1024));

            Assert.That(autoPaused.Status, Is.EqualTo(TorrentStatus.Downloading));
            Assert.That(autoPaused.ErrorMessage, Is.Null);

            Assert.That(manuallyPaused.Status, Is.EqualTo(TorrentStatus.Paused));
            Assert.That(manuallyPaused.ErrorMessage, Is.EqualTo("Manual pause"));

            Assert.That(autoPausedOtherVolume.Status, Is.EqualTo(TorrentStatus.Paused));

            _repository.Received(1).UpdateMany(Arg.Is<IList<Torrent>>(list => list.Count == 1 && list.Contains(autoPaused)));
            _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == autoPaused));
        }

        [Test]
        public void Start_should_reject_and_throw_InsufficientDiskSpaceException_when_disk_space_is_insufficient()
        {
            var torrent = new Torrent
            {
                Id = 10,
                Name = "HugeTorrent",
                TotalSize = 10L * 1024 * 1024 * 1024, // 10 GB
                Downloaded = 0,
                SavePath = "/downloads",
                Status = TorrentStatus.Paused,
            };

            _repository.Get(10).Returns(torrent);

            var diskSpaceService = Substitute.For<IDiskSpaceService>();
            diskSpaceService.GetDiskSpaceForPath("/downloads").Returns(new DiskSpaceInfo
            {
                Path = "/downloads",
                FreeSpace = 1024L * 1024 * 1024, // 1 GB (requires 10 GB + 500 MB)
                TotalSpace = 50L * 1024 * 1024 * 1024,
            });
            _subject.DiskSpaceService = diskSpaceService;

            var ex = Assert.Throws<InsufficientDiskSpaceException>(() => _subject.Start(10));
            Assert.That(ex.Message, Does.Contain("Insufficient disk space on destination volume to complete torrent download"));
        }

        [Test]
        public void Start_should_succeed_and_resume_when_disk_space_is_sufficient()
        {
            var torrent = new Torrent
            {
                Id = 11,
                Name = "ManageableTorrent",
                TotalSize = 2L * 1024 * 1024 * 1024, // 2 GB
                Downloaded = 1L * 1024 * 1024 * 1024, // 1 GB remaining
                SavePath = "/downloads",
                Status = TorrentStatus.Paused,
                ErrorMessage = "Paused: Emergency low disk space on volume /downloads",
                Progress = 0.5,
            };

            _repository.Get(11).Returns(torrent);
            _repository.Update(torrent).Returns(torrent);

            var diskSpaceService = Substitute.For<IDiskSpaceService>();
            diskSpaceService.GetDiskSpaceForPath("/downloads").Returns(new DiskSpaceInfo
            {
                Path = "/downloads",
                FreeSpace = 50L * 1024 * 1024 * 1024, // 50 GB free
                TotalSpace = 100L * 1024 * 1024 * 1024,
            });
            _subject.DiskSpaceService = diskSpaceService;

            var result = _subject.Start(11);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Status, Is.EqualTo(TorrentStatus.Downloading));
            Assert.That(result.ErrorMessage, Is.Null);
        }

        [Test]
        public void Add_should_throw_InsufficientDiskSpaceException_when_adding_downloading_torrent_with_insufficient_space()
        {
            var torrent = new Torrent
            {
                Id = 12,
                Name = "NewDownloadingTorrent",
                Status = TorrentStatus.Downloading,
                TotalSize = 5L * 1024 * 1024 * 1024, // 5 GB
                Downloaded = 0,
                SavePath = "/downloads",
            };

            var diskSpaceService = Substitute.For<IDiskSpaceService>();
            diskSpaceService.GetDiskSpaceForPath("/downloads").Returns(new DiskSpaceInfo
            {
                Path = "/downloads",
                FreeSpace = 200L * 1024 * 1024, // 200 MB
                TotalSpace = 50L * 1024 * 1024 * 1024,
            });
            _subject.DiskSpaceService = diskSpaceService;

            var ex = Assert.Throws<InsufficientDiskSpaceException>(() => _subject.Add(torrent));
            Assert.That(ex.Message, Does.Contain("Insufficient disk space on destination volume to complete torrent download"));
        }

        [Test]
        public void Start_should_call_DiskAllocationService_PreallocateFiles_when_starting_torrent()
        {
            var torrent = new Torrent
            {
                Id = 15,
                Name = "TorrentToStart",
                Status = TorrentStatus.Stopped,
                TotalSize = 1000,
                SavePath = "/downloads"
            };

            var files = new List<TorrentFile>
            {
                new TorrentFile { TorrentId = 15, Path = "file1.mkv", Size = 1000 }
            };

            _repository.Get(15).Returns(torrent);
            _repository.Update(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());
            _torrentFileService.GetFilesByTorrentId(15).Returns(files);

            var diskAllocationService = Substitute.For<IDiskAllocationService>();
            _subject.DiskAllocationService = diskAllocationService;

            _subject.Start(15);

            diskAllocationService.Received(1).PreallocateFiles(torrent, files);
        }

        [Test]
        public void Add_should_call_DiskAllocationService_PreallocateFiles_when_adding_downloading_torrent()
        {
            var files = new List<TorrentFile>
            {
                new TorrentFile { Path = "download.bin", Size = 2000 }
            };

            var torrent = new Torrent
            {
                Name = "NewDownloadingTorrent",
                Status = TorrentStatus.Downloading,
                TotalSize = 2000,
                SavePath = "/downloads",
                Files = files
            };

            _repository.Insert(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

            var diskAllocationService = Substitute.For<IDiskAllocationService>();
            _subject.DiskAllocationService = diskAllocationService;

            _subject.Add(torrent);

            diskAllocationService.Received(1).PreallocateFiles(torrent, files);
        }
    }
}
