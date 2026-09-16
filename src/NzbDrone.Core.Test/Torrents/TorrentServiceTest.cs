using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
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
        public void Update_should_return_updated_torrent()
        {
            var torrent = new Torrent { Id = 1, Name = "Updated" };
            _repository.Update(torrent).Returns(torrent);

            var result = _subject.Update(torrent);

            Assert.That(result, Is.EqualTo(torrent));
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
            _repository.Received().Update(Arg.Is<Torrent>(t => t.Id == 3 && t.SortOrder == 0));
            _repository.Received().Update(Arg.Is<Torrent>(t => t.Id == 1 && t.SortOrder == 1));
            _repository.Received().Update(Arg.Is<Torrent>(t => t.Id == 2 && t.SortOrder == 2));
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
            _repository.Received().Update(Arg.Is<Torrent>(t => t.Id == 2 && t.SortOrder == 0));
            _repository.Received().Update(Arg.Is<Torrent>(t => t.Id == 1 && t.SortOrder == 1));
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
            _repository.Received().Update(Arg.Is<Torrent>(t => t.Id == 2 && t.SortOrder == 2));
            _repository.Received().Update(Arg.Is<Torrent>(t => t.Id == 3 && t.SortOrder == 1));
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
            _repository.Received().Update(Arg.Is<Torrent>(t => t.Id == 1 && t.SortOrder == 2));
            _repository.Received().Update(Arg.Is<Torrent>(t => t.Id == 2 && t.SortOrder == 0));
            _repository.Received().Update(Arg.Is<Torrent>(t => t.Id == 3 && t.SortOrder == 1));
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
    }
}
