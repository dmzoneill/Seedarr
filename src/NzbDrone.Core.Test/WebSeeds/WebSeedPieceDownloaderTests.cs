using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.WebSeeds;

namespace NzbDrone.Core.Test.WebSeeds;

[TestFixture]
public class WebSeedPieceDownloaderTests
{
    [Test]
    public void CalculateByteRange_first_piece_calculates_correct_range()
    {
        var (start, end) = WebSeedPieceDownloader.CalculateByteRange(0, 16384, 50000);

        Assert.That(start, Is.EqualTo(0));
        Assert.That(end, Is.EqualTo(16383));
        Assert.That(end - start + 1, Is.EqualTo(16384));
    }

    [Test]
    public void CalculateByteRange_middle_piece_calculates_correct_range()
    {
        var (start, end) = WebSeedPieceDownloader.CalculateByteRange(1, 16384, 50000);

        Assert.That(start, Is.EqualTo(16384));
        Assert.That(end, Is.EqualTo(32767));
        Assert.That(end - start + 1, Is.EqualTo(16384));
    }

    [Test]
    public void CalculateByteRange_truncated_last_piece_calculates_correct_range()
    {
        var (start, end) = WebSeedPieceDownloader.CalculateByteRange(3, 16384, 50000);

        Assert.That(start, Is.EqualTo(49152));
        Assert.That(end, Is.EqualTo(49999));
        Assert.That(end - start + 1, Is.EqualTo(848));
    }

    [Test]
    public void CalculateByteRange_single_piece_torrent_calculates_correct_range()
    {
        var (start, end) = WebSeedPieceDownloader.CalculateByteRange(0, 10000, 5000);

        Assert.That(start, Is.EqualTo(0));
        Assert.That(end, Is.EqualTo(4999));
        Assert.That(end - start + 1, Is.EqualTo(5000));
    }

    [Test]
    public void CalculateByteRange_invalid_arguments_throw_argument_out_of_range_exception()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebSeedPieceDownloader.CalculateByteRange(-1, 16384, 50000));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebSeedPieceDownloader.CalculateByteRange(0, 0, 50000));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebSeedPieceDownloader.CalculateByteRange(0, -100, 50000));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebSeedPieceDownloader.CalculateByteRange(0, 16384, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebSeedPieceDownloader.CalculateByteRange(0, 16384, -500));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WebSeedPieceDownloader.CalculateByteRange(4, 16384, 50000));
    }

    [Test]
    public async Task DownloadPieceAsync_successful_download_with_206_partial_content()
    {
        var expectedBytes = new byte[16384];
        new Random(42).NextBytes(expectedBytes);

        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.PartialContent, expectedBytes);
        using var httpClient = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(httpClient);

        var result = await downloader.DownloadPieceAsync(
            "https://webseed.example.com/files/test.iso",
            1,
            16384,
            50000);

        Assert.That(result, Is.EqualTo(expectedBytes));
        Assert.That(mockHandler.LastRequest, Is.Not.Null);
        Assert.That(mockHandler.LastRequest.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(mockHandler.LastRequest.RequestUri.ToString(), Is.EqualTo("https://webseed.example.com/files/test.iso"));
        Assert.That(mockHandler.LastRequest.Headers.Range, Is.Not.Null);
        Assert.That(mockHandler.LastRequest.Headers.Range.Ranges.Count, Is.EqualTo(1));

        var range = mockHandler.LastRequest.Headers.Range.Ranges.GetEnumerator();
        range.MoveNext();
        Assert.That(range.Current.From, Is.EqualTo(16384));
        Assert.That(range.Current.To, Is.EqualTo(32767));
    }

    [Test]
    public async Task DownloadPieceAsync_successful_download_with_200_ok()
    {
        var expectedBytes = new byte[848];
        new Random(101).NextBytes(expectedBytes);

        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.OK, expectedBytes);
        using var httpClient = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(httpClient);

        var result = await downloader.DownloadPieceAsync(
            "https://webseed.example.com/files/test.iso",
            3,
            16384,
            50000);

        Assert.That(result, Is.EqualTo(expectedBytes));
    }

    [Test]
    public void DownloadPieceAsync_throws_http_request_exception_on_error_status_code()
    {
        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, Array.Empty<byte>());
        using var httpClient = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(httpClient);

        Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await downloader.DownloadPieceAsync(
                "https://webseed.example.com/files/test.iso",
                0,
                16384,
                50000);
        });
    }

    [Test]
    public void DownloadPieceAsync_throws_argument_exception_on_invalid_url()
    {
        using var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, Array.Empty<byte>()));
        var downloader = new WebSeedPieceDownloader(httpClient);

        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await downloader.DownloadPieceAsync(null, 0, 16384, 50000);
        });

        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await downloader.DownloadPieceAsync(string.Empty, 0, 16384, 50000);
        });

        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await downloader.DownloadPieceAsync("   ", 0, 16384, 50000);
        });
    }

    [Test]
    public async Task DownloadPieceAsync_streams_large_piece_in_chunks()
    {
        // 40 KiB piece exceeds 16 KiB chunk size, requiring at least 3 chunk reads
        const int pieceSize = 40 * 1024;
        var expectedBytes = new byte[pieceSize];
        new Random(123).NextBytes(expectedBytes);

        var chunkReadCount = 0;
        var chunkTrackingStream = new ChunkTrackingStream(new MemoryStream(expectedBytes), onRead: () => chunkReadCount++);

        var mockHandler = new MockHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new StreamContent(chunkTrackingStream)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(httpClient);

        var result = await downloader.DownloadPieceAsync(
            "https://webseed.example.com/files/test.iso",
            0,
            pieceSize,
            pieceSize);

        Assert.That(result, Is.EqualTo(expectedBytes));
        // Should have read at least 3 chunks (16 KiB + 16 KiB + 8 KiB)
        Assert.That(chunkReadCount, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void DownloadPieceAsync_throws_when_cancellation_requested_beforehand()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var httpClient = new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, Array.Empty<byte>()));
        var downloader = new WebSeedPieceDownloader(httpClient);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await downloader.DownloadPieceAsync(
                "https://webseed.example.com/files/test.iso",
                0,
                16384,
                50000,
                cts.Token);
        });
    }

    [Test]
    public void DownloadPieceAsync_handles_cancellation_during_stream_read()
    {
        using var cts = new CancellationTokenSource();
        var pieceBytes = new byte[32768];

        var cancellingStream = new ChunkTrackingStream(new MemoryStream(pieceBytes), onRead: () =>
        {
            cts.Cancel();
        });

        var mockHandler = new MockHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new StreamContent(cancellingStream)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(httpClient);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await downloader.DownloadPieceAsync(
                "https://webseed.example.com/files/test.iso",
                0,
                32768,
                32768,
                cts.Token);
        });
    }

    [Test]
    public void DownloadPieceAsync_throws_invalid_operation_exception_on_incomplete_stream()
    {
        // Expecting 16384 bytes, but only returning 1000 bytes
        var incompleteBytes = new byte[1000];
        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.PartialContent, incompleteBytes);
        using var httpClient = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(httpClient);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await downloader.DownloadPieceAsync(
                "https://webseed.example.com/files/test.iso",
                0,
                16384,
                50000);
        });

        Assert.That(ex.Message, Does.Contain("Incomplete piece received"));
    }

    [Test]
    public void WebSeedHttpClientFactory_configures_sockets_http_handler_correctly()
    {
        using var factory = new WebSeedHttpClientFactory();
        using var handler = factory.CreateHandler();

        Assert.That(handler.PooledConnectionLifetime, Is.EqualTo(TimeSpan.FromMinutes(15)));
        Assert.That(handler.PooledConnectionIdleTimeout, Is.EqualTo(TimeSpan.FromMinutes(2)));
        Assert.That(handler.MaxConnectionsPerServer, Is.EqualTo(4));
        Assert.That(handler.EnableMultipleHttp2Connections, Is.True);
    }

    [Test]
    public void WebSeedHttpClientFactory_GetClient_returns_pooled_singleton_client()
    {
        using var factory = new WebSeedHttpClientFactory();
        var client1 = factory.GetClient();
        var client2 = factory.GetClient();

        Assert.That(client1, Is.Not.Null);
        Assert.That(client2, Is.SameAs(client1));
        Assert.That(client1.DefaultRequestVersion, Is.EqualTo(HttpVersion.Version20));
        Assert.That(client1.DefaultVersionPolicy, Is.EqualTo(HttpVersionPolicy.RequestVersionOrLower));
    }

    [Test]
    public async Task WebSeedPieceDownloader_works_with_factory_injection()
    {
        var factory = Substitute.For<IWebSeedHttpClientFactory>();
        var pieceData = new byte[1024];
        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.PartialContent, pieceData);
        using var httpClient = new HttpClient(mockHandler);
        factory.GetClient().Returns(httpClient);

        var downloader = new WebSeedPieceDownloader(factory);
        var result = await downloader.DownloadPieceAsync(
            "https://webseed.example.com/files/test.iso",
            0,
            1024,
            1024);

        Assert.That(result, Is.EqualTo(pieceData));
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFunc;

        public HttpRequestMessage LastRequest { get; private set; }

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
        {
            _handlerFunc = handlerFunc;
        }

        public MockHttpMessageHandler(HttpStatusCode statusCode, byte[] content)
            : this((req, ct) =>
            {
                var response = new HttpResponseMessage(statusCode)
                {
                    Content = new ByteArrayContent(content)
                };
                return Task.FromResult(response);
            })
        {
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return SendAsync(request, cancellationToken).GetAwaiter().GetResult();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return _handlerFunc(request, cancellationToken);
        }
    }

    private sealed class ChunkTrackingStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly Action _onRead;

        public ChunkTrackingStream(Stream innerStream, Action onRead)
        {
            _innerStream = innerStream;
            _onRead = onRead;
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;

        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override void Flush() => _innerStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            _onRead();
            return _innerStream.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _onRead();
            cancellationToken.ThrowIfCancellationRequested();
            return _innerStream.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _onRead();
            cancellationToken.ThrowIfCancellationRequested();
            return _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
    }
}
