using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
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
            _repository.All().Returns(new List<Torrent>().AsQueryable());
            _repository.Insert(Arg.Any<Torrent>()).Returns(torrent);

            _subject.Add(torrent);

            Assert.That(torrent.SortOrder, Is.EqualTo(0));
        }

        [Test]
        public void Add_should_set_SortOrder_to_max_plus_one_when_existing_torrents()
        {
            var torrent = new Torrent { Name = "New Torrent" };
            var existing = new List<Torrent>
            {
                new Torrent { SortOrder = 0 },
                new Torrent { SortOrder = 1 },
                new Torrent { SortOrder = 2 }
            };
            _repository.All().Returns(existing.AsQueryable());
            _repository.Insert(Arg.Any<Torrent>()).Returns(torrent);

            _subject.Add(torrent);

            Assert.That(torrent.SortOrder, Is.EqualTo(3));
        }

        [Test]
        public void Add_should_call_repository_Insert()
        {
            var torrent = new Torrent { Name = "New Torrent" };
            _repository.All().Returns(new List<Torrent>().AsQueryable());
            _repository.Insert(torrent).Returns(torrent);

            _subject.Add(torrent);

            _repository.Received(1).Insert(torrent);
        }

        [Test]
        public void Add_should_publish_TorrentAddedEvent()
        {
            var torrent = new Torrent { Name = "New Torrent" };
            _repository.All().Returns(new List<Torrent>().AsQueryable());
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
        public void Delete_should_not_look_up_torrent_when_deleteFiles_is_false()
        {
            _subject.Delete(1, false);

            _repository.DidNotReceive().Get(Arg.Any<int>());
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
    }
}
