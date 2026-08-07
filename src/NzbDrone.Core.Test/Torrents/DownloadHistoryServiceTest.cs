using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents
{
    [TestFixture]
    public class DownloadHistoryServiceTest
    {
        private IDownloadHistoryRepository _historyRepository;
        private ITorrentRepository _torrentRepository;
        private ITrackerEntryRepository _trackerEntryRepository;
        private DownloadHistoryService _subject;

        [SetUp]
        public void Setup()
        {
            _historyRepository = Substitute.For<IDownloadHistoryRepository>();
            _torrentRepository = Substitute.For<ITorrentRepository>();
            _trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
            _subject = new DownloadHistoryService(_historyRepository, _torrentRepository, _trackerEntryRepository);
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
                DateAdded = DateTime.UtcNow
            };

            _historyRepository.FindByInfoHash("abc123hash").Returns((DownloadHistory)null);
            _historyRepository.Insert(Arg.Any<DownloadHistory>()).Returns(x => (DownloadHistory)x[0]);

            var result = _subject.RecordTorrentAdded(torrent, source: "Prowlarr", magnetUrl: "magnet:?xt=urn:btih:abc123hash");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Title, Is.EqualTo("Ubuntu 24.04"));
            Assert.That(result.InfoHash, Is.EqualTo("abc123hash"));
            Assert.That(result.Source, Is.EqualTo("Prowlarr"));
            Assert.That(result.Status, Is.EqualTo("Active"));
            _historyRepository.Received(1).Insert(Arg.Is<DownloadHistory>(h => h.InfoHash == "abc123hash" && h.Status == "Active"));
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
                SeedingTime = 3600
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
                Status = "Removed"
            };

            _historyRepository.Get(5).Returns(history);
            _torrentRepository.ExistsByInfoHash("abc123hash").Returns(false);
            _torrentRepository.All().Returns(new List<Torrent>().AsQueryable());
            _torrentRepository.Insert(Arg.Any<Torrent>()).Returns(new Torrent { Id = 42, Name = "Ubuntu 24.04", InfoHash = "abc123hash" });

            var readded = _subject.ReAdd(5);

            Assert.That(readded, Is.Not.Null);
            Assert.That(readded.Id, Is.EqualTo(42));
            Assert.That(history.Status, Is.EqualTo("Active"));
            Assert.That(history.TorrentId, Is.EqualTo(42));
            _torrentRepository.Received(1).Insert(Arg.Is<Torrent>(t => t.InfoHash == "abc123hash"));
            _historyRepository.Received(1).Update(history);
        }

        [Test]
        public void ReAdd_should_throw_if_already_in_library()
        {
            var history = new DownloadHistory
            {
                Id = 5,
                InfoHash = "abc123hash"
            };

            _historyRepository.Get(5).Returns(history);
            _torrentRepository.ExistsByInfoHash("abc123hash").Returns(true);

            Assert.Throws<InvalidOperationException>(() => _subject.ReAdd(5));
        }

        [Test]
        public void ClearAll_should_call_repository_DeleteAll()
        {
            _subject.ClearAll();

            _historyRepository.Received(1).DeleteAll();
        }
    }
}
