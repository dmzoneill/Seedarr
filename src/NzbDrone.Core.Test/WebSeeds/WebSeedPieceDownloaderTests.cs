using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
        Assert.That(handler.AllowAutoRedirect, Is.False);
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

    [Test]
    public void VerifyPiece_returns_true_for_valid_sha1_hash()
    {
        var pieceData = new byte[16384];
        new Random(42).NextBytes(pieceData);
        var expectedHash = SHA1.HashData(pieceData);

        var downloader = new WebSeedPieceDownloader();
        var isValid = downloader.VerifyPiece(pieceData, expectedHash);

        Assert.That(isValid, Is.True);
    }

    [Test]
    public void VerifyPiece_returns_false_for_corrupted_piece_or_hash()
    {
        var pieceData = new byte[16384];
        new Random(42).NextBytes(pieceData);
        var expectedHash = SHA1.HashData(pieceData);

        var corruptData = (byte[])pieceData.Clone();
        corruptData[0] ^= 0xFF;

        var corruptHash = (byte[])expectedHash.Clone();
        corruptHash[0] ^= 0xFF;

        var downloader = new WebSeedPieceDownloader();

        Assert.That(downloader.VerifyPiece(corruptData, expectedHash), Is.False);
        Assert.That(downloader.VerifyPiece(pieceData, corruptHash), Is.False);
    }

    [Test]
    public void VerifyPiece_returns_false_for_null_or_invalid_hash_length()
    {
        var pieceData = new byte[1024];
        var validHash = SHA1.HashData(pieceData);
        var invalidLengthHash = new byte[19];

        var downloader = new WebSeedPieceDownloader();

        Assert.That(downloader.VerifyPiece(null, validHash), Is.False);
        Assert.That(downloader.VerifyPiece(pieceData, null), Is.False);
        Assert.That(downloader.VerifyPiece(pieceData, invalidLengthHash), Is.False);
        Assert.That(downloader.VerifyPiece(pieceData, Array.Empty<byte>()), Is.False);
    }

    [Test]
    public async Task DownloadAndVerifyPieceAsync_successful_download_and_hash_verification()
    {
        const string url = "https://webseed.example.com/files/test.iso";
        var pieceData = new byte[16384];
        new Random(77).NextBytes(pieceData);
        var expectedHash = SHA1.HashData(pieceData);

        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.PartialContent, pieceData);
        using var httpClient = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(httpClient);

        var result = await downloader.DownloadAndVerifyPieceAsync(
            url,
            0,
            16384,
            50000,
            expectedHash);

        Assert.That(result, Is.EqualTo(pieceData));
        Assert.That(downloader.GetCorruptionCount(url), Is.EqualTo(0));
        Assert.That(downloader.IsWebSeedBanned(url), Is.False);
    }

    [Test]
    public void DownloadAndVerifyPieceAsync_corrupted_piece_detected_throws_and_increments_corruption_count()
    {
        const string url = "https://webseed.example.com/files/test.iso";
        var pieceData = new byte[16384];
        new Random(88).NextBytes(pieceData);

        var mismatchedHash = new byte[20];
        Array.Fill(mismatchedHash, (byte)0xAB);

        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.PartialContent, pieceData);
        using var httpClient = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(httpClient);

        var ex = Assert.ThrowsAsync<WebSeedException>(async () =>
        {
            await downloader.DownloadAndVerifyPieceAsync(
                url,
                0,
                16384,
                50000,
                mismatchedHash);
        });

        Assert.That(ex.Message, Does.Contain("failed SHA-1"));
        Assert.That(downloader.GetCorruptionCount(url), Is.EqualTo(1));
        Assert.That(downloader.IsWebSeedBanned(url), Is.False);
    }

    [Test]
    public void DownloadAndVerifyPieceAsync_bans_web_seed_when_corruption_threshold_is_reached()
    {
        const string url = "https://webseed.example.com/files/test.iso";
        var pieceData = new byte[16384];
        new Random(99).NextBytes(pieceData);

        var mismatchedHash = new byte[20];
        Array.Fill(mismatchedHash, (byte)0xCD);

        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.PartialContent, pieceData);
        using var httpClient = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(httpClient)
        {
            CorruptionThreshold = 3
        };

        // Failure 1
        Assert.ThrowsAsync<WebSeedException>(async () =>
            await downloader.DownloadAndVerifyPieceAsync(url, 0, 16384, 50000, mismatchedHash));
        Assert.That(downloader.GetCorruptionCount(url), Is.EqualTo(1));
        Assert.That(downloader.IsWebSeedBanned(url), Is.False);

        // Failure 2
        Assert.ThrowsAsync<WebSeedException>(async () =>
            await downloader.DownloadAndVerifyPieceAsync(url, 0, 16384, 50000, mismatchedHash));
        Assert.That(downloader.GetCorruptionCount(url), Is.EqualTo(2));
        Assert.That(downloader.IsWebSeedBanned(url), Is.False);

        // Failure 3 (reaches threshold)
        Assert.ThrowsAsync<WebSeedException>(async () =>
            await downloader.DownloadAndVerifyPieceAsync(url, 0, 16384, 50000, mismatchedHash));
        Assert.That(downloader.GetCorruptionCount(url), Is.EqualTo(3));
        Assert.That(downloader.IsWebSeedBanned(url), Is.True);
        Assert.That(downloader.IsWebSeedBlacklisted(url), Is.True);

        var requestCountBeforeBannedCall = mockHandler.RequestCount;

        // 4th attempt must be rejected immediately without making any HTTP request
        var bannedEx = Assert.ThrowsAsync<WebSeedException>(async () =>
            await downloader.DownloadAndVerifyPieceAsync(url, 0, 16384, 50000, mismatchedHash));
        Assert.That(bannedEx.Message, Does.Contain("banned"));
        Assert.That(mockHandler.RequestCount, Is.EqualTo(requestCountBeforeBannedCall));
    }

    [Test]
    public void DownloadAndVerifyPieceAsync_supports_per_torrent_banning()
    {
        const string url = "https://webseed.example.com/files/test.iso";
        const string torrentA = "urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string torrentB = "urn:btih:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        var pieceData = new byte[1024];
        var mismatchedHash = new byte[20];
        Array.Fill(mismatchedHash, (byte)0xEE);

        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.PartialContent, pieceData);
        using var httpClient = new HttpClient(mockHandler);
        var downloader = new WebSeedPieceDownloader(httpClient)
        {
            CorruptionThreshold = 2
        };

        // Fail twice for torrentA
        Assert.ThrowsAsync<WebSeedException>(async () =>
            await downloader.DownloadAndVerifyPieceAsync(url, 0, 1024, 1024, mismatchedHash, torrentA));
        Assert.ThrowsAsync<WebSeedException>(async () =>
            await downloader.DownloadAndVerifyPieceAsync(url, 0, 1024, 1024, mismatchedHash, torrentA));

        Assert.That(downloader.IsWebSeedBanned(url, torrentA), Is.True);
        Assert.That(downloader.IsWebSeedBanned(url, torrentB), Is.False);
    }

    [Test]
    public void ShouldUseWebSeeds_returns_true_when_active_peer_count_is_zero_and_has_web_seeds()
    {
        var downloader = new WebSeedPieceDownloader();

        Assert.That(downloader.ShouldUseWebSeeds(0, true), Is.True);
        Assert.That(downloader.ShouldUseWebSeeds(-1, true), Is.True);
    }

    [Test]
    public void ShouldUseWebSeeds_returns_true_when_swarm_availability_is_degraded()
    {
        var downloader = new WebSeedPieceDownloader();

        // Default threshold is 3 peers, so 1 and 2 active peers are considered degraded
        Assert.That(downloader.ShouldUseWebSeeds(1, true), Is.True);
        Assert.That(downloader.ShouldUseWebSeeds(2, true), Is.True);
    }

    [Test]
    public void ShouldUseWebSeeds_returns_false_when_swarm_availability_is_healthy()
    {
        var downloader = new WebSeedPieceDownloader();

        // 3 or more active peers indicates a healthy swarm, so fallback is not required
        Assert.That(downloader.ShouldUseWebSeeds(3, true), Is.False);
        Assert.That(downloader.ShouldUseWebSeeds(10, true), Is.False);
        Assert.That(downloader.ShouldUseWebSeeds(50, true), Is.False);
    }

    [Test]
    public void ShouldUseWebSeeds_returns_false_when_has_web_seeds_is_false()
    {
        var downloader = new WebSeedPieceDownloader();

        Assert.That(downloader.ShouldUseWebSeeds(0, false), Is.False);
        Assert.That(downloader.ShouldUseWebSeeds(1, false), Is.False);
        Assert.That(downloader.ShouldUseWebSeeds(10, false), Is.False);
    }

    [Test]
    public void ShouldUseWebSeeds_supports_custom_degraded_threshold()
    {
        var downloader = new WebSeedPieceDownloader();

        Assert.That(downloader.ShouldUseWebSeeds(4, true, degradedPeerThreshold: 5), Is.True);
        Assert.That(downloader.ShouldUseWebSeeds(5, true, degradedPeerThreshold: 5), Is.False);

        downloader.DegradedPeerThreshold = 5;
        Assert.That(downloader.ShouldUseWebSeeds(4, true), Is.True);
        Assert.That(downloader.ShouldUseWebSeeds(5, true), Is.False);
    }

    [Test]
    public void ResetCorruption_clears_failures_and_unbans_web_seed()
    {
        const string url = "https://webseed.example.com/files/test.iso";
        var downloader = new WebSeedPieceDownloader();

        downloader.BanWebSeed(url);
        Assert.That(downloader.IsWebSeedBanned(url), Is.True);

        downloader.ResetCorruption(url);
        Assert.That(downloader.IsWebSeedBanned(url), Is.False);
        Assert.That(downloader.GetCorruptionCount(url), Is.EqualTo(0));
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFunc;

        public HttpRequestMessage LastRequest { get; private set; }
        public int RequestCount { get; private set; }

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
            RequestCount++;
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
