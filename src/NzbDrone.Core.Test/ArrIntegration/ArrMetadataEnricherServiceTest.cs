using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.ArrIntegration
{
    [TestFixture]
    public class ArrMetadataEnricherServiceTest
    {
        private IArrConnectionFactory _connectionFactory;
        private IDownloadHistoryRepository _downloadHistoryRepository;
        private ArrMetadataEnricherService _subject;

        [SetUp]
        public void Setup()
        {
            _connectionFactory = Substitute.For<IArrConnectionFactory>();
            _downloadHistoryRepository = Substitute.For<IDownloadHistoryRepository>();
            _subject = new ArrMetadataEnricherService(_connectionFactory, _downloadHistoryRepository);
        }

        [Test]
        public void EnrichHistoryEntry_should_return_null_when_history_not_found()
        {
            _downloadHistoryRepository.Get(99).Returns((DownloadHistory)null);

            var result = _subject.EnrichHistoryEntry(99);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void FetchMetadataForRecord_should_return_null_when_no_media_id()
        {
            var record = new ArrDownloadRecord { Title = "Test Movie" };
            var def = new ArrConnectionDefinition { ArrType = "Radarr", Url = "http://localhost:7878" };

            var result = _subject.FetchMetadataForRecord(record, def);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void EnrichAll_should_iterate_all_entries_without_data_json()
        {
            var entries = new List<DownloadHistory>
            {
                new() { Id = 1, Title = "Movie 1", DataJson = null },
                new() { Id = 2, Title = "Movie 2", DataJson = "{\"title\":\"Existing\"}" }
            };

            _downloadHistoryRepository.All().Returns(entries);
            _downloadHistoryRepository.Get(1).Returns(entries[0]);
            _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());

            _subject.EnrichAll();

            _downloadHistoryRepository.Received(1).Get(1);
            _downloadHistoryRepository.DidNotReceive().Get(2);
        }

        [TestCase("Severance.S01E01.1080p.WEB-DL.x265-FLUX.mkv", "Severance")]
        [TestCase("Dune.Part.Two.2024.2160p.UHD.Remux.mkv", "Dune Part Two 2024")]
        [TestCase("The.Penguin.S01.720p.HDTV.x264-SPARKS", "The Penguin")]
        public void CleanReleaseTitle_should_clean_scene_tags(string raw, string expected)
        {
            var cleaned = ArrMetadataEnricherService.CleanReleaseTitle(raw);
            Assert.That(cleaned, Is.EqualTo(expected));
        }

        [Test]
        public void ReconcileAndEnrichAll_should_backfill_missing_torrents()
        {
            var torrentRepo = Substitute.For<ITorrentRepository>();
            var torrents = new List<Torrent>
            {
                new() { Id = 10, Name = "Test Torrent", InfoHash = "abc12345", TotalSize = 1024 }
            };
            torrentRepo.All().Returns(torrents);
            _downloadHistoryRepository.FindByInfoHash("abc12345").Returns((DownloadHistory)null);

            var subjectWithRepo = new ArrMetadataEnricherService(_connectionFactory, _downloadHistoryRepository, torrentRepo);
            var result = subjectWithRepo.ReconcileAndEnrichAll();

            Assert.That(result, Is.GreaterThanOrEqualTo(1));
            _downloadHistoryRepository.Received(1).Insert(Arg.Is<DownloadHistory>(h => h.InfoHash == "abc12345"));
        }
    }
}
