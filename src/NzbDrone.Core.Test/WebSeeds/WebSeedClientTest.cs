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
            response.Content.Headers.ContentLength = 16384;
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

    [Test]
    public void DownloadBlockAsync_404_permanently_marks_web_seed_dead_and_prevents_subsequent_requests()
    {
        var requestCount = 0;
        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            requestCount++;
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
        Assert.That(requestCount, Is.EqualTo(1));
        Assert.That(client.IsSeedDead("https://seed.example.com/files/movie.mkv"), Is.True);
        Assert.That(client.IsSeedAvailable("https://seed.example.com/files/movie.mkv"), Is.False);

        // Second request to same file must fail immediately with WebSeedException without hitting network
        var ex2 = Assert.ThrowsAsync<WebSeedException>(async () =>
        {
            await client.DownloadBlockAsync("https://seed.example.com/files/movie.mkv", 0, 16384);
        });
        Assert.That(ex2.Message, Does.Contain("permanently disabled/dead"));
        Assert.That(requestCount, Is.EqualTo(1));

        // Third request to a different file on the same host must also be blocked without hitting network
        var ex3 = Assert.ThrowsAsync<WebSeedException>(async () =>
        {
            await client.DownloadBlockAsync("https://seed.example.com/files/other.mkv", 0, 16384);
        });
        Assert.That(ex3.Message, Does.Contain("permanently disabled/dead"));
        Assert.That(requestCount, Is.EqualTo(1));
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.Gone)]
    public void DownloadBlockAsync_permanent_errors_mark_web_seed_dead(HttpStatusCode statusCode)
    {
        var requestCount = 0;
        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode) { ReasonPhrase = statusCode.ToString() });
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var url = $"https://seed-{statusCode}.example.com/file.dat";

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.DownloadBlockAsync(url, 0, 1024);
        });
        Assert.That(ex.StatusCode, Is.EqualTo(statusCode));
        Assert.That(client.IsSeedDead(url), Is.True);
        Assert.That(requestCount, Is.EqualTo(1));

        // Subsequent call throws WebSeedException
        Assert.ThrowsAsync<WebSeedException>(async () =>
        {
            await client.DownloadBlockAsync(url, 0, 1024);
        });
        Assert.That(requestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DownloadBlockAsync_429_RetryAfter_backs_off_requests_until_cooldown_expires()
    {
        var payload = new byte[1024];
        new Random(123).NextBytes(payload);
        var requestCount = 0;

        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    ReasonPhrase = "Too Many Requests"
                };
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
                return Task.FromResult(response);
            }

            var successResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload)
            };
            return Task.FromResult(successResponse);
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var fixedTime = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        client.UtcNow = () => fixedTime;

        var url = "https://rate-limited.example.com/file.dat";

        // 1st call: returns 429
        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.DownloadBlockAsync(url, 0, 1024);
        });
        Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(requestCount, Is.EqualTo(1));
        Assert.That(client.IsSeedCoolingDown(url), Is.True);
        Assert.That(client.IsSeedAvailable(url), Is.False);
        Assert.That(client.GetCooldownRemaining(url), Is.EqualTo(TimeSpan.FromSeconds(60)));

        // 2nd call while cooldown is active: immediately throws WebSeedException without network call
        var ex2 = Assert.ThrowsAsync<WebSeedException>(async () =>
        {
            await client.DownloadBlockAsync(url, 0, 1024);
        });
        Assert.That(ex2.Message, Does.Contain("cooling down"));
        Assert.That(requestCount, Is.EqualTo(1));

        // Advance time partially (30s) - still cooling down
        fixedTime = fixedTime.AddSeconds(30);
        Assert.That(client.IsSeedCoolingDown(url), Is.True);
        Assert.That(client.GetCooldownRemaining(url), Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.ThrowsAsync<WebSeedException>(async () =>
        {
            await client.DownloadBlockAsync(url, 0, 1024);
        });
        Assert.That(requestCount, Is.EqualTo(1));

        // Advance time past cooldown (31s more -> 61s total)
        fixedTime = fixedTime.AddSeconds(31);
        Assert.That(client.IsSeedCoolingDown(url), Is.False);
        Assert.That(client.IsSeedAvailable(url), Is.True);

        // 3rd call succeeds!
        var result = await client.DownloadBlockAsync(url, 0, 1024);
        Assert.That(result, Is.EqualTo(payload));
        Assert.That(requestCount, Is.EqualTo(2));
    }

    [Test]
    public async Task DownloadBlockAsync_503_with_RetryAfter_date_backs_off()
    {
        var payload = new byte[512];
        var requestCount = 0;
        var baseTime = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var targetDate = new DateTimeOffset(baseTime.AddSeconds(45));

        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(targetDate);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload)
            });
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);
        client.UtcNow = () => baseTime;

        var url = "https://service-unavailable.example.com/file.dat";

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.DownloadBlockAsync(url, 0, 512);
        });
        Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(client.IsSeedCoolingDown(url), Is.True);
        Assert.That(client.GetCooldownRemaining(url), Is.EqualTo(TimeSpan.FromSeconds(45)));

        // Advance time past targetDate
        baseTime = baseTime.AddSeconds(50);
        Assert.That(client.IsSeedCoolingDown(url), Is.False);

        var result = await client.DownloadBlockAsync(url, 0, 512);
        Assert.That(result, Is.EqualTo(payload));
        Assert.That(requestCount, Is.EqualTo(2));
    }

    [Test]
    public async Task DownloadBlockAsync_consecutive_5xx_trips_circuit_breaker_with_exponential_backoff()
    {
        var payload = new byte[1024];
        new Random(456).NextBytes(payload);
        var requestCount = 0;
        var shouldSucceed = false;

        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            requestCount++;
            if (!shouldSucceed)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    ReasonPhrase = "Internal Server Error"
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload)
            });
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient)
        {
            CircuitBreakerThreshold = 5
        };

        var fixedTime = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        client.UtcNow = () => fixedTime;

        var url = "https://faulty.example.com/file.dat";

        // Failures 1 through 4: increment consecutive failure count, circuit breaker not tripped yet
        for (var i = 1; i <= 4; i++)
        {
            var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await client.DownloadBlockAsync(url, 0, 1024);
            });
            Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(client.GetConsecutiveFailures(url), Is.EqualTo(i));
            Assert.That(client.IsSeedCoolingDown(url), Is.False);
            Assert.That(requestCount, Is.EqualTo(i));
        }

        // 5th failure: threshold reached, trips circuit breaker!
        var ex5 = Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.DownloadBlockAsync(url, 0, 1024);
        });
        Assert.That(ex5.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(client.GetConsecutiveFailures(url), Is.EqualTo(5));
        Assert.That(client.GetCircuitBreakerTripCount(url), Is.EqualTo(1));
        Assert.That(client.IsSeedCoolingDown(url), Is.True);
        Assert.That(client.GetCooldownRemaining(url), Is.EqualTo(TimeSpan.FromSeconds(15))); // 1st backoff step: 15s
        Assert.That(requestCount, Is.EqualTo(5));

        // Next call during cooldown: immediately rejected with WebSeedException without network request
        var exCooling = Assert.ThrowsAsync<WebSeedException>(async () =>
        {
            await client.DownloadBlockAsync(url, 0, 1024);
        });
        Assert.That(exCooling.Message, Does.Contain("cooling down"));
        Assert.That(requestCount, Is.EqualTo(5));

        // Advance time past 1st cooldown (16 seconds)
        fixedTime = fixedTime.AddSeconds(16);
        Assert.That(client.IsSeedCoolingDown(url), Is.False);

        // Probe call fails again (6th failure) -> 2nd trip with exponential backoff (30s)!
        var ex6 = Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.DownloadBlockAsync(url, 0, 1024);
        });
        Assert.That(requestCount, Is.EqualTo(6));
        Assert.That(client.GetCircuitBreakerTripCount(url), Is.EqualTo(2));
        Assert.That(client.IsSeedCoolingDown(url), Is.True);
        Assert.That(client.GetCooldownRemaining(url), Is.EqualTo(TimeSpan.FromSeconds(30))); // 2nd backoff step: 30s

        // Advance time past 2nd cooldown (31 seconds)
        fixedTime = fixedTime.AddSeconds(31);
        Assert.That(client.IsSeedCoolingDown(url), Is.False);

        // Now server recovers: probe call succeeds!
        shouldSucceed = true;
        var result = await client.DownloadBlockAsync(url, 0, 1024);
        Assert.That(result, Is.EqualTo(payload));
        Assert.That(requestCount, Is.EqualTo(7));

        // Circuit breaker completely reset after successful response
        Assert.That(client.GetConsecutiveFailures(url), Is.EqualTo(0));
        Assert.That(client.GetCircuitBreakerTripCount(url), Is.EqualTo(0));
        Assert.That(client.IsSeedCoolingDown(url), Is.False);
        Assert.That(client.IsSeedAvailable(url), Is.True);
    }

    [Test]
    public void DownloadBlockAsync_network_exception_counts_towards_circuit_breaker()
    {
        var requestCount = 0;
        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            requestCount++;
            throw new HttpRequestException("Connection refused");
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient)
        {
            CircuitBreakerThreshold = 3
        };

        var url = "https://down.example.com/file.dat";

        for (var i = 1; i <= 2; i++)
        {
            Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await client.DownloadBlockAsync(url, 0, 1024);
            });
            Assert.That(client.GetConsecutiveFailures(url), Is.EqualTo(i));
            Assert.That(client.IsSeedCoolingDown(url), Is.False);
        }

        // 3rd failure trips circuit breaker
        Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.DownloadBlockAsync(url, 0, 1024);
        });
        Assert.That(client.GetConsecutiveFailures(url), Is.EqualTo(3));
        Assert.That(client.IsSeedCoolingDown(url), Is.True);
        Assert.That(client.GetCircuitBreakerTripCount(url), Is.EqualTo(1));
    }

    [Test]
    public void DownloadBlockAsync_416_RangeNotSatisfiable_throws_informative_HttpRequestException()
    {
        var mockHandler = new TestHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                ReasonPhrase = "Range Not Satisfiable"
            });
        });

        using var httpClient = new HttpClient(mockHandler);
        var client = new WebSeedClient(httpClient);

        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.DownloadBlockAsync("https://seed.example.com/file.dat", 1000, 500);
        });

        Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.RequestedRangeNotSatisfiable));
        Assert.That(ex.Message, Does.Contain("416 (Requested Range Not Satisfiable)"));
        Assert.That(ex.Message, Does.Contain("exceed file boundaries"));
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
