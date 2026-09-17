using System;
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

        private class TestableArrMetadataEnricherService : ArrMetadataEnricherService
        {
            public TestableArrMetadataEnricherService(
                IArrConnectionFactory connectionFactory,
                IDownloadHistoryRepository downloadHistoryRepository)
                : base(connectionFactory, downloadHistoryRepository)
            {
            }

            public IArrConnection ExposeCreateProvider(ArrConnectionDefinition definition) => CreateProvider(definition);
        }

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

        [TestCase("sonarr", typeof(SonarrConnection))]
        [TestCase("Sonarr", typeof(SonarrConnection))]
        [TestCase("SONARR", typeof(SonarrConnection))]
        [TestCase("sOnArR", typeof(SonarrConnection))]
        [TestCase("radarr", typeof(RadarrConnection))]
        [TestCase("Radarr", typeof(RadarrConnection))]
        [TestCase("RADARR", typeof(RadarrConnection))]
        [TestCase("lidarr", typeof(LidarrConnection))]
        [TestCase("Lidarr", typeof(LidarrConnection))]
        [TestCase("LIDARR", typeof(LidarrConnection))]
        [TestCase("whisparr", typeof(WhisparrConnection))]
        [TestCase("Whisparr", typeof(WhisparrConnection))]
        [TestCase("WHISPARR", typeof(WhisparrConnection))]
        public void CreateProvider_should_handle_case_insensitive_arr_type(string arrType, Type expectedType)
        {
            var service = new TestableArrMetadataEnricherService(_connectionFactory, _downloadHistoryRepository);
            var def = new ArrConnectionDefinition { ArrType = arrType, Url = "http://localhost:8989", ApiKey = "xyz" };

            var provider = service.ExposeCreateProvider(def);

            Assert.That(provider, Is.Not.Null);
            Assert.That(provider, Is.InstanceOf(expectedType));
            Assert.That(provider.Url, Is.EqualTo("http://localhost:8989"));
            Assert.That(provider.ApiKey, Is.EqualTo("xyz"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("unknown")]
        [TestCase("invalid")]
        public void CreateProvider_should_return_null_for_unrecognized_arr_type(string arrType)
        {
            var service = new TestableArrMetadataEnricherService(_connectionFactory, _downloadHistoryRepository);
            var def = new ArrConnectionDefinition { ArrType = arrType, Url = "http://localhost:8989", ApiKey = "xyz" };

            var provider = service.ExposeCreateProvider(def);

            Assert.That(provider, Is.Null);
        }

        [Test]
        public void EnrichAll_should_only_call_GetDownloadHistory_once_per_enabled_provider()
        {
            var entries = new List<DownloadHistory>
            {
                new() { Id = 1, InfoHash = "hash1", DataJson = null },
                new() { Id = 2, InfoHash = "hash2", DataJson = null },
                new() { Id = 3, InfoHash = "hash3", DataJson = null },
                new() { Id = 4, InfoHash = "hash4", DataJson = null },
                new() { Id = 5, InfoHash = "hash5", DataJson = null }
            };

            _downloadHistoryRepository.All().Returns(entries);
            foreach (var e in entries)
            {
                _downloadHistoryRepository.Get(e.Id).Returns(e);
            }

            var sonarrDef = new ArrConnectionDefinition { Id = 1, Name = "Sonarr", ArrType = "Sonarr", Enable = true };
            var radarrDef = new ArrConnectionDefinition { Id = 2, Name = "Radarr", ArrType = "Radarr", Enable = true };
            var disabledDef = new ArrConnectionDefinition { Id = 3, Name = "Lidarr", ArrType = "Lidarr", Enable = false };

            _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { sonarrDef, radarrDef, disabledDef });

            var sonarrProvider = Substitute.For<IArrConnection>();
            sonarrProvider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>());

            var radarrProvider = Substitute.For<IArrConnection>();
            radarrProvider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>());

            var lidarrProvider = Substitute.For<IArrConnection>();

            var service = new ArrMetadataEnricherService(
                _connectionFactory,
                _downloadHistoryRepository,
                null,
                def => def.Id == 1 ? sonarrProvider : def.Id == 2 ? radarrProvider : lidarrProvider);

            service.EnrichAll();

            sonarrProvider.Received(1).GetDownloadHistory();
            radarrProvider.Received(1).GetDownloadHistory();
            lidarrProvider.DidNotReceive().GetDownloadHistory();
        }

        [Test]
        public void EnrichAll_should_correlate_multiple_history_entries_efficiently()
        {
            var entries = new List<DownloadHistory>
            {
                new() { Id = 1, InfoHash = "hash1", DataJson = null },
                new() { Id = 2, InfoHash = "hash2", DataJson = null },
                new() { Id = 3, InfoHash = "hash3", DataJson = null }
            };

            _downloadHistoryRepository.All().Returns(entries);
            _downloadHistoryRepository.Get(1).Returns(entries[0]);
            _downloadHistoryRepository.Get(2).Returns(entries[1]);
            _downloadHistoryRepository.Get(3).Returns(entries[2]);

            var sonarrDef = new ArrConnectionDefinition { Id = 1, Name = "Sonarr", ArrType = "Sonarr", Enable = true };
            _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { sonarrDef });

            var sonarrProvider = Substitute.For<IArrConnection>();
            sonarrProvider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
            {
                new() { InfoHash = "HASH1", MediaId = 10, Title = "Show 1" },
                new() { InfoHash = "HASH2", MediaId = 20, Title = "Show 2" }
            });
            sonarrProvider.GetMediaDetails(10).Returns(new MediaMetadata { Title = "Show 1", MediaId = 10 });
            sonarrProvider.GetMediaDetails(20).Returns(new MediaMetadata { Title = "Show 2", MediaId = 20 });

            var service = new ArrMetadataEnricherService(
                _connectionFactory,
                _downloadHistoryRepository,
                null,
                _ => sonarrProvider);

            service.EnrichAll();

            sonarrProvider.Received(1).GetDownloadHistory();
            _downloadHistoryRepository.Received(1).Update(Arg.Is<DownloadHistory>(h => h.Id == 1 && h.DataJson.Contains("Show 1")));
            _downloadHistoryRepository.Received(1).Update(Arg.Is<DownloadHistory>(h => h.Id == 2 && h.DataJson.Contains("Show 2")));
        }
    }
}
