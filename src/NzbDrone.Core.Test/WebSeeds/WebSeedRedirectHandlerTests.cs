using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.WebSeeds;

namespace NzbDrone.Core.Test.WebSeeds;

[TestFixture]
public class WebSeedRedirectHandlerTests
{
    private WebSeedRedirectHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _handler = new WebSeedRedirectHandler();
    }

    [Test]
    [TestCase(HttpStatusCode.MovedPermanently)]
    [TestCase(HttpStatusCode.Found)]
    [TestCase(HttpStatusCode.TemporaryRedirect)]
    [TestCase(HttpStatusCode.PermanentRedirect)]
    public async Task SendWithRedirectsAsync_follows_redirect_and_preserves_range_header(HttpStatusCode statusCode)
    {
        var requestsReceived = new List<HttpRequestMessage>();
        var finalContent = new byte[] { 1, 2, 3, 4 };

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requestsReceived.Add(CloneRequest(request));

            if (request.RequestUri.ToString() == "https://source.example.com/file.iso")
            {
                var redirectResponse = new HttpResponseMessage(statusCode);
                redirectResponse.Headers.Location = new Uri("https://target.example.com/file.iso");
                return Task.FromResult(redirectResponse);
            }

            var finalResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(finalContent)
            };
            return Task.FromResult(finalResponse);
        });

        using var client = new HttpClient(mockHandler);
        using var initialRequest = new HttpRequestMessage(HttpMethod.Get, "https://source.example.com/file.iso");
        initialRequest.Headers.Range = new RangeHeaderValue(0, 16383);

        using var response = await _handler.SendWithRedirectsAsync(client, initialRequest);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
        Assert.That(requestsReceived.Count, Is.EqualTo(2));

        Assert.That(requestsReceived[0].RequestUri.ToString(), Is.EqualTo("https://source.example.com/file.iso"));
        Assert.That(requestsReceived[0].Headers.Range, Is.Not.Null);
        Assert.That(requestsReceived[0].Headers.Range.ToString(), Is.EqualTo("bytes=0-16383"));

        Assert.That(requestsReceived[1].RequestUri.ToString(), Is.EqualTo("https://target.example.com/file.iso"));
        Assert.That(requestsReceived[1].Headers.Range, Is.Not.Null);
        Assert.That(requestsReceived[1].Headers.Range.ToString(), Is.EqualTo("bytes=0-16383"));
    }

    [Test]
    public async Task SendWithRedirectsAsync_preserves_range_on_cross_domain_redirect()
    {
        var requestsReceived = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requestsReceived.Add(CloneRequest(request));

            if (request.RequestUri.Host == "archive.org")
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirectResponse.Headers.Location = new Uri("https://ia6000.us.archive.org/items/sample.iso");
                return Task.FromResult(redirectResponse);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(new byte[100])
            });
        });

        using var client = new HttpClient(mockHandler);
        using var initialRequest = new HttpRequestMessage(HttpMethod.Get, "https://archive.org/download/sample.iso");
        initialRequest.Headers.Range = new RangeHeaderValue(100, 199);

        using var response = await _handler.SendWithRedirectsAsync(client, initialRequest);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
        Assert.That(requestsReceived.Count, Is.EqualTo(2));
        Assert.That(requestsReceived[1].RequestUri.Host, Is.EqualTo("ia6000.us.archive.org"));
        Assert.That(requestsReceived[1].Headers.Range, Is.Not.Null);
        Assert.That(requestsReceived[1].Headers.Range.ToString(), Is.EqualTo("bytes=100-199"));
    }

    [Test]
    public async Task SendWithRedirectsAsync_resolves_relative_root_location_header()
    {
        var requestsReceived = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requestsReceived.Add(CloneRequest(request));

            if (request.RequestUri.AbsolutePath == "/downloads/file.iso")
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
                redirectResponse.Headers.Location = new Uri("/mirrors/file.iso", UriKind.Relative);
                return Task.FromResult(redirectResponse);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(mockHandler);
        using var initialRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/downloads/file.iso");

        using var response = await _handler.SendWithRedirectsAsync(client, initialRequest);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(requestsReceived.Count, Is.EqualTo(2));
        Assert.That(requestsReceived[1].RequestUri.ToString(), Is.EqualTo("https://example.com/mirrors/file.iso"));
    }

    [Test]
    public async Task SendWithRedirectsAsync_resolves_relative_path_location_header()
    {
        var requestsReceived = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requestsReceived.Add(CloneRequest(request));

            if (request.RequestUri.ToString() == "https://example.com/downloads/file.iso")
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                redirectResponse.Headers.Location = new Uri("alternate.iso", UriKind.Relative);
                return Task.FromResult(redirectResponse);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(mockHandler);
        using var initialRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/downloads/file.iso");

        using var response = await _handler.SendWithRedirectsAsync(client, initialRequest);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(requestsReceived.Count, Is.EqualTo(2));
        Assert.That(requestsReceived[1].RequestUri.ToString(), Is.EqualTo("https://example.com/downloads/alternate.iso"));
    }

    [Test]
    public async Task SendWithRedirectsAsync_resolves_absolute_location_header()
    {
        var requestsReceived = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requestsReceived.Add(CloneRequest(request));

            if (request.RequestUri.ToString() == "https://original.com/file.iso")
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.PermanentRedirect);
                redirectResponse.Headers.Location = new Uri("https://newsite.org/file.iso");
                return Task.FromResult(redirectResponse);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(mockHandler);
        using var initialRequest = new HttpRequestMessage(HttpMethod.Get, "https://original.com/file.iso");

        using var response = await _handler.SendWithRedirectsAsync(client, initialRequest);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(requestsReceived.Count, Is.EqualTo(2));
        Assert.That(requestsReceived[1].RequestUri.ToString(), Is.EqualTo("https://newsite.org/file.iso"));
    }

    [Test]
    public void SendWithRedirectsAsync_detects_circular_redirect_and_throws_invalid_operation_exception()
    {
        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            var uri = request.RequestUri.ToString();
            var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);

            if (uri == "https://example.com/a")
            {
                redirectResponse.Headers.Location = new Uri("https://example.com/b");
            }
            else if (uri == "https://example.com/b")
            {
                redirectResponse.Headers.Location = new Uri("https://example.com/c");
            }
            else
            {
                // Circular back to /a
                redirectResponse.Headers.Location = new Uri("https://example.com/a");
            }

            return Task.FromResult(redirectResponse);
        });

        using var client = new HttpClient(mockHandler);
        using var initialRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/a");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _handler.SendWithRedirectsAsync(client, initialRequest);
        });

        Assert.That(ex.Message, Does.Contain("Circular redirect detected"));
    }

    [Test]
    public void SendWithRedirectsAsync_detects_self_redirect_and_throws_invalid_operation_exception()
    {
        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
            redirectResponse.Headers.Location = new Uri("https://example.com/self");
            return Task.FromResult(redirectResponse);
        });

        using var client = new HttpClient(mockHandler);
        using var initialRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/self");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _handler.SendWithRedirectsAsync(client, initialRequest);
        });

        Assert.That(ex.Message, Does.Contain("Circular redirect detected"));
    }

    [Test]
    public void SendWithRedirectsAsync_throws_when_redirect_count_exceeds_max_redirects()
    {
        // 6 distinct redirect hops (exceeding MaxRedirects = 5)
        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            var uri = request.RequestUri.ToString();
            var step = int.Parse(uri[^1..]);
            var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
            redirectResponse.Headers.Location = new Uri($"https://example.com/hop{step + 1}");
            return Task.FromResult(redirectResponse);
        });

        using var client = new HttpClient(mockHandler);
        using var initialRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/hop1");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _handler.SendWithRedirectsAsync(client, initialRequest);
        });

        Assert.That(ex.Message, Does.Contain("Maximum redirect limit of 5 exceeded"));
    }

    [Test]
    public async Task SendWithRedirectsAsync_allows_up_to_max_redirects()
    {
        // Exactly 5 redirect hops ending on hop 6 with 200 OK
        var requestsReceived = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requestsReceived.Add(CloneRequest(request));
            var uri = request.RequestUri.ToString();
            var step = int.Parse(uri[^1..]);

            if (step < 6)
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
                redirectResponse.Headers.Location = new Uri($"https://example.com/hop{step + 1}");
                return Task.FromResult(redirectResponse);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(mockHandler);
        using var initialRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/hop1");

        using var response = await _handler.SendWithRedirectsAsync(client, initialRequest);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(requestsReceived.Count, Is.EqualTo(6));
        Assert.That(requestsReceived[^1].RequestUri.ToString(), Is.EqualTo("https://example.com/hop6"));
    }

    [Test]
    public async Task SendWithRedirectsAsync_301_and_308_updates_cache_and_subsequent_requests_target_destination_directly()
    {
        var requestsReceived = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requestsReceived.Add(CloneRequest(request));
            var uri = request.RequestUri.ToString();

            if (uri == "https://source.example.com/item301")
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirectResponse.Headers.Location = new Uri("https://cdn.example.com/item301");
                return Task.FromResult(redirectResponse);
            }

            if (uri == "https://source.example.com/item308")
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.PermanentRedirect);
                redirectResponse.Headers.Location = new Uri("https://cdn.example.com/item308");
                return Task.FromResult(redirectResponse);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(mockHandler);

        // First request for 301
        using (var req1 = new HttpRequestMessage(HttpMethod.Get, "https://source.example.com/item301"))
        {
            using var resp1 = await _handler.SendWithRedirectsAsync(client, req1);
            Assert.That(resp1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        Assert.That(requestsReceived.Count, Is.EqualTo(2));
        Assert.That(_handler.PermanentRedirects.ContainsKey("https://source.example.com/item301"), Is.True);
        Assert.That(_handler.PermanentRedirects["https://source.example.com/item301"], Is.EqualTo("https://cdn.example.com/item301"));

        // Second request for 301 - should target cached destination directly
        requestsReceived.Clear();
        using (var req2 = new HttpRequestMessage(HttpMethod.Get, "https://source.example.com/item301"))
        {
            using var resp2 = await _handler.SendWithRedirectsAsync(client, req2);
            Assert.That(resp2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        Assert.That(requestsReceived.Count, Is.EqualTo(1));
        Assert.That(requestsReceived[0].RequestUri.ToString(), Is.EqualTo("https://cdn.example.com/item301"));

        // First request for 308
        requestsReceived.Clear();
        using (var req3 = new HttpRequestMessage(HttpMethod.Get, "https://source.example.com/item308"))
        {
            using var resp3 = await _handler.SendWithRedirectsAsync(client, req3);
            Assert.That(resp3.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        Assert.That(requestsReceived.Count, Is.EqualTo(2));
        Assert.That(_handler.PermanentRedirects.ContainsKey("https://source.example.com/item308"), Is.True);
        Assert.That(_handler.PermanentRedirects["https://source.example.com/item308"], Is.EqualTo("https://cdn.example.com/item308"));

        // Second request for 308 - should target cached destination directly
        requestsReceived.Clear();
        using (var req4 = new HttpRequestMessage(HttpMethod.Get, "https://source.example.com/item308"))
        {
            using var resp4 = await _handler.SendWithRedirectsAsync(client, req4);
            Assert.That(resp4.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        Assert.That(requestsReceived.Count, Is.EqualTo(1));
        Assert.That(requestsReceived[0].RequestUri.ToString(), Is.EqualTo("https://cdn.example.com/item308"));
    }

    [Test]
    public async Task SendWithRedirectsAsync_302_and_307_temporary_redirects_are_not_cached_permanently()
    {
        var requestsReceived = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requestsReceived.Add(CloneRequest(request));
            var uri = request.RequestUri.ToString();

            if (uri == "https://source.example.com/temp302")
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.Found);
                redirectResponse.Headers.Location = new Uri("https://cdn.example.com/temp302");
                return Task.FromResult(redirectResponse);
            }

            if (uri == "https://source.example.com/temp307")
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                redirectResponse.Headers.Location = new Uri("https://cdn.example.com/temp307");
                return Task.FromResult(redirectResponse);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(mockHandler);

        using (var req1 = new HttpRequestMessage(HttpMethod.Get, "https://source.example.com/temp302"))
        {
            using var resp1 = await _handler.SendWithRedirectsAsync(client, req1);
            Assert.That(resp1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        using (var req2 = new HttpRequestMessage(HttpMethod.Get, "https://source.example.com/temp307"))
        {
            using var resp2 = await _handler.SendWithRedirectsAsync(client, req2);
            Assert.That(resp2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        Assert.That(_handler.PermanentRedirects.ContainsKey("https://source.example.com/temp302"), Is.False);
        Assert.That(_handler.PermanentRedirects.ContainsKey("https://source.example.com/temp307"), Is.False);

        // Subsequent requests should still go through the redirect
        requestsReceived.Clear();
        using (var req3 = new HttpRequestMessage(HttpMethod.Get, "https://source.example.com/temp302"))
        {
            using var resp3 = await _handler.SendWithRedirectsAsync(client, req3);
            Assert.That(resp3.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        Assert.That(requestsReceived.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task WebSeedPieceDownloader_integrates_with_redirect_handler_and_preserves_range()
    {
        var requestsReceived = new List<HttpRequestMessage>();
        var pieceData = new byte[16384];
        new Random(99).NextBytes(pieceData);

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requestsReceived.Add(CloneRequest(request));

            if (request.RequestUri.ToString() == "https://archive.org/download/item.iso")
            {
                var redirectResponse = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirectResponse.Headers.Location = new Uri("https://ia9000.us.archive.org/item.iso");
                return Task.FromResult(redirectResponse);
            }

            var finalResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(pieceData)
            };
            return Task.FromResult(finalResponse);
        });

        using var client = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(client, _handler);

        // Piece 0 download
        var result1 = await downloader.DownloadPieceAsync("https://archive.org/download/item.iso", 0, 16384, 32768);

        Assert.That(result1, Is.EqualTo(pieceData));
        Assert.That(requestsReceived.Count, Is.EqualTo(2));
        Assert.That(requestsReceived[1].Headers.Range, Is.Not.Null);
        Assert.That(requestsReceived[1].Headers.Range.ToString(), Is.EqualTo("bytes=0-16383"));

        // Piece 1 download - should use cached URL directly!
        requestsReceived.Clear();
        var pieceData2 = new byte[16384];
        new Random(100).NextBytes(pieceData2);

        var result2 = await downloader.DownloadPieceAsync("https://archive.org/download/item.iso", 1, 16384, 32768);

        Assert.That(requestsReceived.Count, Is.EqualTo(1));
        Assert.That(requestsReceived[0].RequestUri.ToString(), Is.EqualTo("https://ia9000.us.archive.org/item.iso"));
        Assert.That(requestsReceived[0].Headers.Range.ToString(), Is.EqualTo("bytes=16384-32767"));
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFunc;

        public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
        {
            _handlerFunc = handlerFunc;
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return SendAsync(request, cancellationToken).GetAwaiter().GetResult();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handlerFunc(request, cancellationToken);
        }
    }
}
