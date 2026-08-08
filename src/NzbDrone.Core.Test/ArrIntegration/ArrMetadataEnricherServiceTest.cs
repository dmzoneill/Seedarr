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
    }
}
