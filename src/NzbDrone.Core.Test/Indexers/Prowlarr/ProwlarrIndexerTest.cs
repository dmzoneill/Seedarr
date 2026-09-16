using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Prowlarr;

namespace NzbDrone.Core.Test.Indexers.Prowlarr
{
    [TestFixture]
    public class ProwlarrIndexerTest
    {
        private ProwlarrIndexer _subject;

        [SetUp]
        public void Setup()
        {
            _subject = new ProwlarrIndexer();
        }

        [Test]
        public void Name_should_return_prowlarr()
        {
            Assert.That(_subject.Name, Is.EqualTo("Prowlarr"));
        }

        [Test]
        public void IndexerType_should_return_prowlarr()
        {
            Assert.That(_subject.IndexerType, Is.EqualTo("Prowlarr"));
        }

        [Test]
        public void TestConnection_should_return_false_when_url_is_invalid()
        {
            var definition = new IndexerDefinition
            {
                Url = "not-a-url",
                ApiKey = "test-key",
                ApiPath = "/api"
            };

            var result = _subject.TestConnection(definition);

            Assert.That(result, Is.False);
        }

        [Test]
        public void TestConnectionDetailed_should_return_error_when_url_is_null_or_empty()
        {
            var definition = new IndexerDefinition
            {
                Url = "",
                ApiKey = "test-key",
                ApiPath = "/api"
            };

            var result = _subject.TestConnectionDetailed(definition);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("URL is required"));
        }

        [Test]
        public void TestConnectionDetailed_should_return_error_when_connection_fails()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://127.0.0.1:59999",
                ApiKey = "test-key",
                ApiPath = "/api"
            };

            var result = _subject.TestConnectionDetailed(definition);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Unable to connect to Prowlarr"));
        }

        [Test]
        public void FetchTorrentByHash_should_reject_unsafe_download_url()
        {
            var handler = new ProwlarrTestHttpMessageHandler();
            var indexer = new ProwlarrIndexer(new HttpClient(handler));

            var definition = new IndexerDefinition
            {
                Id = 1,
                Name = "Prowlarr Test",
                Url = "http://8.8.8.8:9696",
                ApiKey = "secret-key"
            };

            var searchJson = @"[
                {
                    ""title"": ""Unsafe Torrent"",
                    ""downloadUrl"": ""http://169.254.169.254/latest/meta-data""
                }
            ]";

            handler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(searchJson)
            };

            var result = indexer.FetchTorrentByHash(definition, "0123456789abcdef0123456789abcdef01234567");

            Assert.That(result, Is.Null);
            Assert.That(handler.SentRequests.Count, Is.EqualTo(1));
        }

        [Test]
        public void FetchTorrentByHash_should_not_send_api_key_to_cross_origin_download_host()
        {
            var handler = new ProwlarrTestHttpMessageHandler();
            var indexer = new ProwlarrIndexer(new HttpClient(handler));

            var definition = new IndexerDefinition
            {
                Id = 1,
                Name = "Prowlarr Test",
                Url = "http://8.8.8.8:9696",
                ApiKey = "secret-key"
            };

            var searchJson = @"[
                {
                    ""title"": ""Cross Origin Torrent"",
                    ""downloadUrl"": ""http://8.8.4.4:8080/download/1.torrent""
                }
            ]";

            var torrentBytes = new byte[] { 10, 20, 30 };

            handler.Handler = req =>
            {
                if (req.RequestUri.ToString().Contains("/api/v1/search"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(searchJson)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(torrentBytes)
                };
            };

            var result = indexer.FetchTorrentByHash(definition, "0123456789abcdef0123456789abcdef01234567");

            Assert.That(result, Is.EqualTo(torrentBytes));
            Assert.That(handler.SentRequests.Count, Is.EqualTo(2));
            var dlRequest = handler.SentRequests[1];
            Assert.That(dlRequest.Headers.Contains("X-Api-Key"), Is.False);
        }

        [Test]
        public void FetchTorrentByHash_should_send_api_key_to_same_origin_download_host()
        {
            var handler = new ProwlarrTestHttpMessageHandler();
            var indexer = new ProwlarrIndexer(new HttpClient(handler));

            var definition = new IndexerDefinition
            {
                Id = 1,
                Name = "Prowlarr Test",
                Url = "http://8.8.8.8:9696",
                ApiKey = "secret-key"
            };

            var searchJson = @"[
                {
                    ""title"": ""Same Origin Torrent"",
                    ""downloadUrl"": ""http://8.8.8.8:9696/download/1.torrent""
                }
            ]";

            var torrentBytes = new byte[] { 40, 50, 60 };

            handler.Handler = req =>
            {
                if (req.RequestUri.ToString().Contains("/api/v1/search"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(searchJson)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(torrentBytes)
                };
            };

            var result = indexer.FetchTorrentByHash(definition, "0123456789abcdef0123456789abcdef01234567");

            Assert.That(result, Is.EqualTo(torrentBytes));
            Assert.That(handler.SentRequests.Count, Is.EqualTo(2));
            var dlRequest = handler.SentRequests[1];
            Assert.That(dlRequest.Headers.Contains("X-Api-Key"), Is.True);
            Assert.That(dlRequest.Headers.GetValues("X-Api-Key").First(), Is.EqualTo("secret-key"));
        }

        private class ProwlarrTestHttpMessageHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> SentRequests { get; } = new();
            public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; }

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                SentRequests.Add(request);
                return Handler?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                SentRequests.Add(request);
                return Task.FromResult(Handler?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }
}
