using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Prowlarr;

namespace NzbDrone.Core.Test.Indexers.Prowlarr
{
    [TestFixture]
    public class ProwlarrIndexerSyncServiceTest
    {
        private IIndexerRepository _indexerRepository;
        private ProwlarrTestHttpMessageHandler _httpHandler;
        private HttpClient _httpClient;
        private ProwlarrIndexerSyncService _subject;
        private IndexerDefinition _prowlarrDefinition;

        [SetUp]
        public void SetUp()
        {
            _indexerRepository = Substitute.For<IIndexerRepository>();
            _httpHandler = new ProwlarrTestHttpMessageHandler();
            _httpClient = new HttpClient(_httpHandler);
            _subject = new ProwlarrIndexerSyncService(_indexerRepository, _httpClient);

            _prowlarrDefinition = new IndexerDefinition
            {
                Id = 1,
                Name = "Prowlarr",
                IndexerType = "Prowlarr",
                Url = "http://prowlarr:9696",
                ApiKey = "prowlarr-api-key",
                Enable = true
            };

            _indexerRepository.All().Returns(new List<IndexerDefinition> { _prowlarrDefinition });
            _indexerRepository.Get(1).Returns(_prowlarrDefinition);
        }

        [Test]
        public void Sync_should_import_torrent_indexers_and_filter_usenet_and_disabled()
        {
            var prowlarrJson = @"[
                {
                    ""id"": 10,
                    ""name"": ""1337x"",
                    ""protocol"": ""torrent"",
                    ""enable"": true,
                    ""capabilities"": {
                        ""categories"": [
                            { ""id"": 2000, ""name"": ""Movies"" },
                            { ""id"": 5000, ""name"": ""TV"" }
                        ]
                    }
                },
                {
                    ""id"": 11,
                    ""name"": ""Disabled Torrent Indexer"",
                    ""protocol"": ""torrent"",
                    ""enable"": false
                },
                {
                    ""id"": 12,
                    ""name"": ""NZBGeek"",
                    ""protocol"": ""usenet"",
                    ""enable"": true
                }
            ]";

            _httpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(prowlarrJson)
            };

            var result = _subject.Sync();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Added, Is.EqualTo(1));
            Assert.That(result.Updated, Is.EqualTo(0));
            Assert.That(result.Removed, Is.EqualTo(0));

            _indexerRepository.Received(1).Insert(Arg.Is<IndexerDefinition>(i =>
                i.Name == "Prowlarr - 1337x" &&
                i.IndexerType == "Torznab" &&
                i.Implementation == "TorznabIndexer" &&
                i.ConfigContract == ProwlarrSyncMetadata.ConfigContractName &&
                i.Url == "http://prowlarr:9696/10" &&
                i.ApiKey == "prowlarr-api-key" &&
                i.ApiPath == "/api" &&
                i.Categories == "2000,5000" &&
                ProwlarrSyncMetadata.GetProwlarrIndexerId(i) == 10));
        }

        [Test]
        public void Sync_should_update_mutated_indexer()
        {
            var existingSynced = new IndexerDefinition
            {
                Id = 100,
                Name = "Prowlarr - OldName",
                IndexerType = "Torznab",
                Implementation = "TorznabIndexer",
                ConfigContract = ProwlarrSyncMetadata.ConfigContractName,
                Url = "http://prowlarr:9696/10",
                ApiKey = "old-key",
                ApiPath = "/api",
                Categories = "1000",
                Settings = ProwlarrSyncMetadata.BuildSettingsJson(10, 1)
            };

            _indexerRepository.All().Returns(new List<IndexerDefinition> { _prowlarrDefinition, existingSynced });

            var prowlarrJson = @"[
                {
                    ""id"": 10,
                    ""name"": ""NewName"",
                    ""protocol"": ""torrent"",
                    ""enable"": true,
                    ""categories"": [2000, 5000]
                }
            ]";

            _httpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(prowlarrJson)
            };

            var result = _subject.Sync();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Added, Is.EqualTo(0));
            Assert.That(result.Updated, Is.EqualTo(1));
            Assert.That(result.Removed, Is.EqualTo(0));

            _indexerRepository.Received(1).Update(Arg.Is<IndexerDefinition>(i =>
                i.Id == 100 &&
                i.Name == "Prowlarr - NewName" &&
                i.ApiKey == "prowlarr-api-key" &&
                i.Categories == "2000,5000"));
        }

        [Test]
        public void Sync_should_prune_removed_prowlarr_indexers()
        {
            var syncedIndexer1 = new IndexerDefinition
            {
                Id = 101,
                Name = "Prowlarr - ActiveTracker",
                IndexerType = "Torznab",
                ConfigContract = ProwlarrSyncMetadata.ConfigContractName,
                Url = "http://prowlarr:9696/10",
                Settings = ProwlarrSyncMetadata.BuildSettingsJson(10, 1)
            };

            var syncedIndexer2 = new IndexerDefinition
            {
                Id = 102,
                Name = "Prowlarr - RemovedTracker",
                IndexerType = "Torznab",
                ConfigContract = ProwlarrSyncMetadata.ConfigContractName,
                Url = "http://prowlarr:9696/20",
                Settings = ProwlarrSyncMetadata.BuildSettingsJson(20, 1)
            };

            _indexerRepository.All().Returns(new List<IndexerDefinition> { _prowlarrDefinition, syncedIndexer1, syncedIndexer2 });

            var prowlarrJson = @"[
                {
                    ""id"": 10,
                    ""name"": ""ActiveTracker"",
                    ""protocol"": ""torrent"",
                    ""enable"": true
                }
            ]";

            _httpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(prowlarrJson)
            };

            var result = _subject.Sync();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Removed, Is.EqualTo(1));
            _indexerRepository.Received(1).Delete(102);
            _indexerRepository.DidNotReceive().Delete(101);
            _indexerRepository.DidNotReceive().Delete(1);
        }

        [Test]
        public void Sync_should_not_prune_manual_torznab_indexers_or_root_prowlarr()
        {
            var manualTorznab = new IndexerDefinition
            {
                Id = 200,
                Name = "My Private Tracker",
                IndexerType = "Torznab",
                ConfigContract = "IndexerDefinition",
                Url = "https://private-tracker.org/api",
                Settings = null
            };

            _indexerRepository.All().Returns(new List<IndexerDefinition> { _prowlarrDefinition, manualTorznab });

            _httpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };

            var result = _subject.Sync();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Removed, Is.EqualTo(0));
            _indexerRepository.DidNotReceive().Delete(Arg.Any<int>());
        }

        [Test]
        public void Sync_should_flatten_capabilities_categories_and_subcategories()
        {
            var prowlarrJson = @"[
                {
                    ""id"": 50,
                    ""name"": ""CinemaZ"",
                    ""protocol"": ""torrent"",
                    ""enable"": true,
                    ""capabilities"": {
                        ""categories"": [
                            {
                                ""id"": 2000,
                                ""name"": ""Movies"",
                                ""subCategories"": [
                                    { ""id"": 2010, ""name"": ""HD"" },
                                    { ""id"": 2020, ""name"": ""UHD"" }
                                ]
                            }
                        ]
                    }
                }
            ]";

            _httpHandler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(prowlarrJson)
            };

            var result = _subject.Sync();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Added, Is.EqualTo(1));

            _indexerRepository.Received(1).Insert(Arg.Is<IndexerDefinition>(i =>
                i.Categories == "2000,2010,2020"));
        }

        [Test]
        public void ScheduledTask_should_delegate_to_sync_service()
        {
            var syncService = Substitute.For<IProwlarrIndexerSyncService>();
            syncService.Sync(Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(new ProwlarrSyncResult());
            var task = new ProwlarrIndexerSyncScheduledTask(syncService);

            Assert.That(task.DefaultInterval, Is.EqualTo(360));

            task.Execute();
            syncService.Received(1).Sync(null, null, null);

            var cmd = new SyncProwlarrIndexersCommand(42);
            task.Execute(cmd);
            syncService.Received(1).Sync(42, null, null);
        }

        private class ProwlarrTestHttpMessageHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; }

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Handler?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(Handler?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }
}
