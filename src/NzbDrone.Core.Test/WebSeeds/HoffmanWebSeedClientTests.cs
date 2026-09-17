using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.WebSeeds;

namespace NzbDrone.Core.Test.WebSeeds;

[TestFixture]
public class HoffmanWebSeedClientTests
{
    private static readonly byte[] SampleInfoHash = new byte[20]
    {
        0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0, 0x11, 0x22,
        0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC
    };

    [Test]
    public void BuildUrl_formats_bep17_url_with_raw_20_byte_hash_and_piece_omitting_ranges()
    {
        var baseUrl = "https://seed.example.com/download";
        var url = HoffmanWebSeedClient.BuildUrl(baseUrl, SampleInfoHash, 2);

        var expectedEscapedHash = "%12%34%56%78%9A%BC%DE%F0%11%22%33%44%55%66%77%88%99%AA%BB%CC";
        Assert.That(url, Is.EqualTo($"https://seed.example.com/download?info_hash={expectedEscapedHash}&piece=2"));
        Assert.That(url, Does.Not.Contain("ranges="));
    }

    [Test]
    public void BuildUrl_formats_bep17_url_with_ranges_for_block_request()
    {
        var baseUrl = "https://seed.example.com/download";
        var url = HoffmanWebSeedClient.BuildUrl(baseUrl, SampleInfoHash, 5, offset: 16384, length: 16384);

        var expectedEscapedHash = "%12%34%56%78%9A%BC%DE%F0%11%22%33%44%55%66%77%88%99%AA%BB%CC";
        Assert.That(url, Is.EqualTo($"https://seed.example.com/download?info_hash={expectedEscapedHash}&piece=5&ranges=16384-16384"));
    }

    [Test]
    public void BuildUrl_preserves_existing_query_parameters()
    {
        var baseUrl = "https://seed.example.com/download?token=abcdef";
        var url = HoffmanWebSeedClient.BuildUrl(baseUrl, SampleInfoHash, 0);

        Assert.That(url, Does.StartWith("https://seed.example.com/download?token=abcdef&info_hash="));
        Assert.That(url, Does.Contain("&piece=0"));
    }

    [Test]
    public void BuildUrl_throws_on_invalid_arguments()
    {
        Assert.Throws<ArgumentException>(() =>
            HoffmanWebSeedClient.BuildUrl(null, SampleInfoHash, 0));

        Assert.Throws<ArgumentException>(() =>
            HoffmanWebSeedClient.BuildUrl(string.Empty, SampleInfoHash, 0));

        Assert.Throws<ArgumentException>(() =>
            HoffmanWebSeedClient.BuildUrl("   ", SampleInfoHash, 0));

        Assert.Throws<ArgumentException>(() =>
            HoffmanWebSeedClient.BuildUrl("https://example.com", null, 0));

        Assert.Throws<ArgumentException>(() =>
            HoffmanWebSeedClient.BuildUrl("https://example.com", new byte[19], 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoffmanWebSeedClient.BuildUrl("https://example.com", SampleInfoHash, -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoffmanWebSeedClient.BuildUrl("https://example.com", SampleInfoHash, 0, offset: -1, length: 100));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HoffmanWebSeedClient.BuildUrl("https://example.com", SampleInfoHash, 0, offset: 0, length: 0));
    }

    [Test]
    public async Task DownloadPieceAsync_downloads_full_piece_payload_successfully()
    {
        var expectedPayload = new byte[32768];
        new Random(42).NextBytes(expectedPayload);
        var requests = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedPayload)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new HoffmanWebSeedClient(httpClient);

        var result = await client.DownloadPieceAsync(
            "https://seed.example.com/endpoint",
            SampleInfoHash,
            1,
            pieceLength: 32768,
            totalTorrentSize: 100000);

        Assert.That(result, Is.EqualTo(expectedPayload));
        Assert.That(requests.Count, Is.EqualTo(1));
        Assert.That(requests[0].Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(requests[0].RequestUri.ToString(), Does.Contain("piece=1"));
        Assert.That(requests[0].RequestUri.ToString(), Does.Not.Contain("ranges="));
    }

    [Test]
    public async Task DownloadBlockAsync_downloads_block_payload_successfully()
    {
        var expectedBlock = new byte[16384];
        new Random(77).NextBytes(expectedBlock);
        var requests = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedBlock)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new HoffmanWebSeedClient(httpClient);

        var result = await client.DownloadBlockAsync(
            "https://seed.example.com/endpoint",
            SampleInfoHash,
            pieceIndex: 2,
            offset: 0,
            length: 16384);

        Assert.That(result, Is.EqualTo(expectedBlock));
        Assert.That(requests.Count, Is.EqualTo(1));
        Assert.That(requests[0].Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(requests[0].RequestUri.ToString(), Does.Contain("piece=2"));
        Assert.That(requests[0].RequestUri.ToString(), Does.Contain("ranges=0-16384"));
    }

    [Test]
    public void DownloadPieceAsync_throws_on_non_200_status()
    {
        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new HoffmanWebSeedClient(httpClient);

        Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.DownloadPieceAsync(
                "https://seed.example.com/endpoint",
                SampleInfoHash,
                0,
                16384,
                16384);
        });
    }

    [Test]
    public void DownloadPieceAsync_throws_on_incomplete_payload()
    {
        var incompletePayload = new byte[1000];
        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(incompletePayload)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new HoffmanWebSeedClient(httpClient);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await client.DownloadPieceAsync(
                "https://seed.example.com/endpoint",
                SampleInfoHash,
                0,
                16384,
                16384);
        });

        Assert.That(ex.Message, Does.Contain("Incomplete payload received"));
    }

    [Test]
    public async Task HoffmanWebSeedClient_follows_redirect_via_redirect_handler()
    {
        var expectedPayload = new byte[1024];
        new Random(88).NextBytes(expectedPayload);
        var requests = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((request, ct) =>
        {
            requests.Add(request);

            if (request.RequestUri.Host == "seed.example.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirect.Headers.Location = new Uri("https://mirror.example.com/download" + request.RequestUri.Query);
                return Task.FromResult(redirect);
            }

            var okResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedPayload)
            };
            return Task.FromResult(okResponse);
        });

        using var httpClient = new HttpClient(mockHandler);
        var redirectHandler = new WebSeedRedirectHandler();
        var client = new HoffmanWebSeedClient(httpClient, redirectHandler);

        var result = await client.DownloadBlockAsync(
            "https://seed.example.com/download",
            SampleInfoHash,
            0,
            0,
            1024);

        Assert.That(result, Is.EqualTo(expectedPayload));
        Assert.That(requests.Count, Is.EqualTo(2));
        Assert.That(requests[1].RequestUri.Host, Is.EqualTo("mirror.example.com"));
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
