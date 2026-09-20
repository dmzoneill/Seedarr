using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WebSeeds;

namespace NzbDrone.Core.Test.WebSeeds;

[TestFixture]
public class WebSeedClientTest
{
    private static readonly byte[] SampleInfoHash = new byte[20]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A,
        0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14
    };

    [Test]
    public void CalculateByteRange_calculates_correct_absolute_offsets_for_block()
    {
        // Piece 0, offset 0, 16 KiB
        var (start0, end0) = WebSeedClient.CalculateByteRange(0, 65536, 0, 16384);
        Assert.That(start0, Is.EqualTo(0));
        Assert.That(end0, Is.EqualTo(16383));

        // Piece 0, offset 16384, 16 KiB
        var (start1, end1) = WebSeedClient.CalculateByteRange(0, 65536, 16384, 16384);
        Assert.That(start1, Is.EqualTo(16384));
        Assert.That(end1, Is.EqualTo(32767));

        // Piece 2, offset 0, 16 KiB
        var (start2, end2) = WebSeedClient.CalculateByteRange(2, 65536, 0, 16384);
        Assert.That(start2, Is.EqualTo(131072));
        Assert.That(end2, Is.EqualTo(147455));
    }

    [Test]
    public void CalculateByteRange_throws_on_invalid_parameters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSeedClient.CalculateByteRange(-1, 65536, 0, 16384));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSeedClient.CalculateByteRange(0, 0, 0, 16384));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSeedClient.CalculateByteRange(0, -100, 0, 16384));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSeedClient.CalculateByteRange(0, 65536, -1, 16384));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSeedClient.CalculateByteRange(0, 65536, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebSeedClient.CalculateByteRange(0, 65536, 0, -500));
    }

    [Test]
    public void CalculateByteRange_for_total_torrent_size_calculates_piece_ranges()
    {
        var (start, end) = WebSeedClient.CalculateByteRange(1, 16384, 50000);
        Assert.That(start, Is.EqualTo(16384));
        Assert.That(end, Is.EqualTo(32767));

        var (lastStart, lastEnd) = WebSeedClient.CalculateByteRange(3, 16384, 50000);
        Assert.That(lastStart, Is.EqualTo(49152));
        Assert.That(lastEnd, Is.EqualTo(49999));
    }

    [Test]
    public async Task DownloadBlockAsync_successful_range_request_with_206_partial_content()
    {
        var expectedBytes = new byte[16384];
        new Random(42).NextBytes(expectedBytes);

        HttpRequestMessage sentRequest = null;
        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            sentRequest = req;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(expectedBytes)
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 16383, 100000);
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var result = await client.DownloadBlockAsync("https://seed.example.com/files/movie.mkv", 0, 16384);

        Assert.That(result, Is.EqualTo(expectedBytes));
        Assert.That(sentRequest, Is.Not.Null);
        Assert.That(sentRequest.Headers.Range, Is.Not.Null);
        Assert.That(sentRequest.Headers.Range.Ranges.First().From, Is.EqualTo(0));
        Assert.That(sentRequest.Headers.Range.Ranges.First().To, Is.EqualTo(16383));
    }

    [Test]
    public void DownloadBlockAsync_throws_WebSeedException_and_aborts_when_server_returns_200_OK()
    {
        // Misconfigured server returns 200 OK instead of 206 Partial Content
        var multiGigabytePayload = new byte[1024]; // simulated stream
        var streamRead = false;

        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MonitoredStream(() => streamRead = true))
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var ex = Assert.ThrowsAsync<WebSeedException>(async () =>
        {
            await client.DownloadBlockAsync("https://seed.example.com/files/movie.mkv", 16384, 16384);
        });

        Assert.That(ex.Message, Does.Contain("Server does not support byte ranges; returned 200 OK"));
        Assert.That(streamRead, Is.False, "Response stream must NOT be read when 200 OK is returned");
    }

    [Test]
    public void DownloadBlockAsync_throws_HttpRequestException_on_server_error()
    {
        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                ReasonPhrase = "Not Found"
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.DownloadBlockAsync("https://seed.example.com/files/movie.mkv", 0, 16384);
        });

        Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public void DownloadBlockAsync_throws_when_ContentLength_header_mismatches_expected_length()
    {
        var payload = new byte[16384];
        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload)
            };
            response.Content.Headers.ContentLength = 8192; // mismatched length
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var ex = Assert.ThrowsAsync<WebSeedException>(async () =>
        {
            await client.DownloadBlockAsync("https://seed.example.com/files/movie.mkv", 0, 16384);
        });

        Assert.That(ex.Message, Does.Contain("Unexpected Content-Length"));
    }

    [Test]
    public void DownloadBlockAsync_throws_when_stream_is_incomplete()
    {
        var payload = new byte[1000]; // requested 16384, only 1000 returned
        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await client.DownloadBlockAsync("https://seed.example.com/files/movie.mkv", 0, 16384);
        });

        Assert.That(ex.Message, Does.Contain("Incomplete block received"));
    }

    [Test]
    public void CalculateFileSlices_within_single_file()
    {
        var files = new List<WebSeedFileInfo>
        {
            new("file1.dat", 100000),
            new("file2.dat", 100000)
        };

        var slices = WebSeedClient.CalculateFileSlices(5000, 16384, files, "https://seed.example.com/torrent/");

        Assert.That(slices.Count, Is.EqualTo(1));
        Assert.That(slices[0].FilePath, Is.EqualTo("file1.dat"));
        Assert.That(slices[0].FileOffset, Is.EqualTo(5000));
        Assert.That(slices[0].Length, Is.EqualTo(16384));
        Assert.That(slices[0].BufferOffset, Is.EqualTo(0));
        Assert.That(slices[0].Url, Is.EqualTo("https://seed.example.com/torrent/file1.dat"));
    }

    [Test]
    public void CalculateFileSlices_spanning_two_files()
    {
        var files = new List<WebSeedFileInfo>
        {
            new("file1.dat", 10000),
            new("file2.dat", 20000)
        };

        // Torrent offset 8000, length 4000:
        // File 1 has [0..9999]: overlap [8000..9999] = 2000 bytes at file offset 8000, buffer offset 0
        // File 2 has [10000..29999]: overlap [10000..11999] = 2000 bytes at file offset 0, buffer offset 2000
        var slices = WebSeedClient.CalculateFileSlices(8000, 4000, files, "https://seed.example.com/dir/");

        Assert.That(slices.Count, Is.EqualTo(2));

        Assert.That(slices[0].FilePath, Is.EqualTo("file1.dat"));
        Assert.That(slices[0].FileOffset, Is.EqualTo(8000));
        Assert.That(slices[0].Length, Is.EqualTo(2000));
        Assert.That(slices[0].BufferOffset, Is.EqualTo(0));
        Assert.That(slices[0].Url, Is.EqualTo("https://seed.example.com/dir/file1.dat"));

        Assert.That(slices[1].FilePath, Is.EqualTo("file2.dat"));
        Assert.That(slices[1].FileOffset, Is.EqualTo(0));
        Assert.That(slices[1].Length, Is.EqualTo(2000));
        Assert.That(slices[1].BufferOffset, Is.EqualTo(2000));
        Assert.That(slices[1].Url, Is.EqualTo("https://seed.example.com/dir/file2.dat"));
    }

    [Test]
    public void CalculateFileSlices_skips_empty_files_and_flags_padding_files()
    {
        var files = new List<WebSeedFileInfo>
        {
            new("empty.txt", 0),
            new("file1.dat", 1000),
            new(".pad/100", 100, isPaddingFile: true),
            new("file2.dat", 1000)
        };

        // Torrent offset 950, length 200
        // File 1: [0..999], overlap [950..999] = 50 bytes (offset 950, buf 0)
        // Pad:    [1000..1099], overlap [1000..1099] = 100 bytes (offset 0, buf 50)
        // File 2: [1100..2099], overlap [1100..1149] = 50 bytes (offset 0, buf 150)
        var slices = WebSeedClient.CalculateFileSlices(950, 200, files, "https://seed.example.com/dir/");

        Assert.That(slices.Count, Is.EqualTo(3));
        Assert.That(slices[0].FilePath, Is.EqualTo("file1.dat"));
        Assert.That(slices[0].Length, Is.EqualTo(50));
        Assert.That(slices[0].BufferOffset, Is.EqualTo(0));
        Assert.That(slices[0].IsPadding, Is.False);

        Assert.That(slices[1].FilePath, Is.EqualTo(".pad/100"));
        Assert.That(slices[1].Length, Is.EqualTo(100));
        Assert.That(slices[1].BufferOffset, Is.EqualTo(50));
        Assert.That(slices[1].IsPadding, Is.True);

        Assert.That(slices[2].FilePath, Is.EqualTo("file2.dat"));
        Assert.That(slices[2].Length, Is.EqualTo(50));
        Assert.That(slices[2].BufferOffset, Is.EqualTo(150));
        Assert.That(slices[2].IsPadding, Is.False);
    }

    [Test]
    public async Task DownloadMultiFileBlockAsync_downloads_and_stitches_across_files()
    {
        var file1Data = new byte[2000];
        var file2Data = new byte[2000];
        new Random(101).NextBytes(file1Data);
        new Random(202).NextBytes(file2Data);

        var requestedUrls = new List<string>();
        var requestedRanges = new List<(long From, long To)>();

        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            requestedUrls.Add(req.RequestUri.ToString());
            var range = req.Headers.Range.Ranges.First();
            requestedRanges.Add((range.From.Value, range.To.Value));

            byte[] returnBytes;
            if (req.RequestUri.AbsolutePath.EndsWith("part1.bin"))
            {
                var length = (int)(range.To.Value - range.From.Value + 1);
                returnBytes = new byte[length];
                Buffer.BlockCopy(file1Data, (int)range.From.Value, returnBytes, 0, length);
            }
            else
            {
                var length = (int)(range.To.Value - range.From.Value + 1);
                returnBytes = new byte[length];
                Buffer.BlockCopy(file2Data, (int)range.From.Value, returnBytes, 0, length);
            }

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(returnBytes)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var files = new List<WebSeedFileInfo>
        {
            new("part1.bin", 2000),
            new("part2.bin", 2000)
        };

        // Request 1000 bytes starting at offset 1500:
        // 500 bytes from part1.bin (1500..1999)
        // 500 bytes from part2.bin (0..499)
        var result = await client.DownloadMultiFileBlockAsync("https://seed.example.com/torrent/", files, 1500, 1000);

        Assert.That(result.Length, Is.EqualTo(1000));
        Assert.That(requestedUrls.Count, Is.EqualTo(2));
        Assert.That(requestedUrls[0], Is.EqualTo("https://seed.example.com/torrent/part1.bin"));
        Assert.That(requestedRanges[0], Is.EqualTo((1500L, 1999L)));
        Assert.That(requestedUrls[1], Is.EqualTo("https://seed.example.com/torrent/part2.bin"));
        Assert.That(requestedRanges[1], Is.EqualTo((0L, 499L)));

        var expectedFirst500 = new byte[500];
        Buffer.BlockCopy(file1Data, 1500, expectedFirst500, 0, 500);
        var actualFirst500 = new byte[500];
        Buffer.BlockCopy(result, 0, actualFirst500, 0, 500);
        Assert.That(actualFirst500, Is.EqualTo(expectedFirst500));

        var expectedSecond500 = new byte[500];
        Buffer.BlockCopy(file2Data, 0, expectedSecond500, 0, 500);
        var actualSecond500 = new byte[500];
        Buffer.BlockCopy(result, 500, actualSecond500, 0, 500);
        Assert.That(actualSecond500, Is.EqualTo(expectedSecond500));
    }

    [Test]
    public async Task DownloadMultiFileBlockAsync_handles_padding_file_without_http_request()
    {
        var fileData = new byte[100];
        new Random(303).NextBytes(fileData);

        var requestedUrls = new List<string>();
        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            requestedUrls.Add(req.RequestUri.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(fileData)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var files = new List<WebSeedFileInfo>
        {
            new("file.bin", 100),
            new(".pad/50", 50, isPaddingFile: true)
        };

        var result = await client.DownloadMultiFileBlockAsync("https://seed.example.com/torrent/", files, 0, 150);

        Assert.That(result.Length, Is.EqualTo(150));
        Assert.That(requestedUrls.Count, Is.EqualTo(1)); // only 1 HTTP request, pad was skipped

        for (var i = 100; i < 150; i++)
        {
            Assert.That(result[i], Is.EqualTo(0));
        }
    }

    [Test]
    public async Task WebSeedClient_follows_redirects()
    {
        var payload = new byte[1024];
        new Random(77).NextBytes(payload);
        var requests = new List<HttpRequestMessage>();

        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            requests.Add(req);
            if (req.RequestUri.Host == "seed.example.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirect.Headers.Location = new Uri("https://mirror.example.com/file.bin");
                return Task.FromResult(redirect);
            }

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var redirectHandler = new WebSeedRedirectHandler();
        var client = new WebSeedClient(httpClient, redirectHandler);

        var result = await client.DownloadBlockAsync("https://seed.example.com/file.bin", 0, 1024);

        Assert.That(result, Is.EqualTo(payload));
        Assert.That(requests.Count, Is.EqualTo(2));
        Assert.That(requests[1].RequestUri.Host, Is.EqualTo("mirror.example.com"));
        Assert.That(requests[1].Headers.Range, Is.Not.Null);
    }

    [Test]
    public async Task DownloadPieceAsync_calculates_piece_range_and_downloads_block()
    {
        var payload = new byte[16384];
        new Random(99).NextBytes(payload);
        HttpRequestMessage sentRequest = null;

        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            sentRequest = req;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var result = await client.DownloadPieceAsync(
            "https://seed.example.com/single.iso",
            SampleInfoHash,
            1,
            16384,
            50000);

        Assert.That(result, Is.EqualTo(payload));
        Assert.That(sentRequest.Headers.Range.Ranges.First().From, Is.EqualTo(16384));
        Assert.That(sentRequest.Headers.Range.Ranges.First().To, Is.EqualTo(32767));
    }

    private sealed class MonitoredStream : Stream
    {
        private readonly Action _onRead;

        public MonitoredStream(Action onRead)
        {
            _onRead = onRead;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 1024 * 1024 * 1024L;
        public override long Position { get; set; }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _onRead();
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _onRead();
            return Task.FromResult(0);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _onRead();
            return ValueTask.FromResult(0);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
