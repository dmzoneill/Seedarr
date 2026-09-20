using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Blocklist;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.Blocklist;

[TestFixture]
public class BlocklistArchiveStreamProviderTests
{
    private BlocklistArchiveStreamProvider _provider;

    [SetUp]
    public void SetUp()
    {
        _provider = new BlocklistArchiveStreamProvider();
    }

    [Test]
    public async Task ExtractRulesAsync_should_stream_and_decompress_gzip_archive()
    {
        var text = "# Bluetack comment\n1.2.3.4-1.2.3.5\n\n// Another comment\n8.8.8.8\n";
        var gzipBytes = CreateGzipBytes(text);
        using var stream = new MemoryStream(gzipBytes);

        var rules = await _provider.ExtractRulesAsync(stream, url: "http://example.com/rules.gz");

        Assert.That(rules.Count, Is.EqualTo(2));
        Assert.That(rules[0], Is.EqualTo("1.2.3.4-1.2.3.5"));
        Assert.That(rules[1], Is.EqualTo("8.8.8.8"));
    }

    [Test]
    public async Task ExtractRulesAsync_should_stream_and_decompress_zip_archive_and_select_primary_entry()
    {
        var entries = new[]
        {
            ("readme.txt", "This is Bluetack level 1 blocklist\nWebsite: http://example.com"),
            ("level1.p2p", "# Bluetack Level 1\nPrimary:192.168.1.0-192.168.1.255\n10.0.0.1\n")
        };
        var zipBytes = CreateZipBytes(entries);
        using var stream = new MemoryStream(zipBytes);

        var rules = await _provider.ExtractRulesAsync(stream, url: "http://example.com/bluetack.zip");

        Assert.That(rules.Count, Is.EqualTo(2));
        Assert.That(rules[0], Is.EqualTo("Primary:192.168.1.0-192.168.1.255"));
        Assert.That(rules[1], Is.EqualTo("10.0.0.1"));
    }

    [Test]
    public async Task DetectFormat_should_detect_gzip_via_magic_bytes_without_url_or_header_hints()
    {
        var text = "1.1.1.1\n2.2.2.2\n";
        var gzipBytes = CreateGzipBytes(text);
        using var stream = new MemoryStream(gzipBytes);

        // No URL extension, no Content-Type, no Content-Encoding
        var rules = await _provider.ExtractRulesAsync(stream, url: "http://example.com/download?id=123");

        Assert.That(rules.Count, Is.EqualTo(2));
        Assert.That(rules[0], Is.EqualTo("1.1.1.1"));
        Assert.That(rules[1], Is.EqualTo("2.2.2.2"));
    }

    [Test]
    public async Task DetectFormat_should_detect_zip_via_magic_bytes_without_url_or_header_hints()
    {
        var entries = new[]
        {
            ("rules.txt", "100.100.100.1\n200.200.200.2\n")
        };
        var zipBytes = CreateZipBytes(entries);
        using var stream = new MemoryStream(zipBytes);

        // No URL extension, no Content-Type, no Content-Encoding
        var rules = await _provider.ExtractRulesAsync(stream, url: "http://example.com/raw_archive");

        Assert.That(rules.Count, Is.EqualTo(2));
        Assert.That(rules[0], Is.EqualTo("100.100.100.1"));
        Assert.That(rules[1], Is.EqualTo("200.200.200.2"));
    }

    [Test]
    public void DetectFormat_should_identify_all_formats_correctly()
    {
        var gzipMagic = new byte[] { 0x1F, 0x8B, 0x08, 0x00 };
        Assert.That(
            BlocklistArchiveStreamProvider.DetectFormat(gzipMagic, 4),
            Is.EqualTo(BlocklistArchiveFormat.GZip));

        var zipMagic = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        Assert.That(
            BlocklistArchiveStreamProvider.DetectFormat(zipMagic, 4),
            Is.EqualTo(BlocklistArchiveFormat.Zip));

        var plainText = Encoding.UTF8.GetBytes("# Comment");
        Assert.That(
            BlocklistArchiveStreamProvider.DetectFormat(plainText, 4),
            Is.EqualTo(BlocklistArchiveFormat.PlainText));

        // Format detection by content-type
        Assert.That(
            BlocklistArchiveStreamProvider.DetectFormat(plainText, 4, contentType: "application/x-gzip"),
            Is.EqualTo(BlocklistArchiveFormat.GZip));

        Assert.That(
            BlocklistArchiveStreamProvider.DetectFormat(plainText, 4, contentType: "application/zip"),
            Is.EqualTo(BlocklistArchiveFormat.Zip));

        // Format detection by URL
        Assert.That(
            BlocklistArchiveStreamProvider.DetectFormat(plainText, 4, url: "http://site.test/list.gz"),
            Is.EqualTo(BlocklistArchiveFormat.GZip));

        Assert.That(
            BlocklistArchiveStreamProvider.DetectFormat(plainText, 4, url: "http://site.test/list.zip"),
            Is.EqualTo(BlocklistArchiveFormat.Zip));
    }

    [Test]
    public async Task ExtractRulesAsync_with_100k_entries_should_stream_efficiently_without_loh_spikes()
    {
        const int entryCount = 100_000;
        var sb = new StringBuilder(entryCount * 18);
        for (var i = 0; i < entryCount; i++)
        {
            sb.Append("10.").Append((i >> 8) & 0xFF).Append('.').Append(i & 0xFF).Append(".1\n");
        }

        var fullText = sb.ToString();
        var gzipBytes = CreateGzipBytes(fullText);

        using var stream = new MemoryStream(gzipBytes);
        var rules = await _provider.ExtractRulesAsync(stream, url: "http://example.com/large.gz");

        Assert.That(rules.Count, Is.EqualTo(entryCount));
        Assert.That(rules[0], Is.EqualTo("10.0.0.1"));
        Assert.That(rules[entryCount - 1], Is.EqualTo("10.134.159.1"));
    }

    [Test]
    public void ExtractRulesAsync_should_abort_when_uncompressed_bytes_quota_exceeded()
    {
        var options = new BlocklistArchiveStreamOptions
        {
            MaxUncompressedBytes = 256
        };
        var strictProvider = new BlocklistArchiveStreamProvider(options);

        var largeText = new string('A', 512) + "\n1.2.3.4\n";
        var gzipBytes = CreateGzipBytes(largeText);
        using var stream = new MemoryStream(gzipBytes);

        var ex = Assert.ThrowsAsync<BlocklistQuotaExceededException>(async () =>
        {
            await strictProvider.ExtractRulesAsync(stream, url: "http://example.com/bomb.gz");
        });

        Assert.That(ex.Message, Does.Contain("quota"));
    }

    [Test]
    public void ExtractRulesAsync_should_abort_when_max_rule_lines_quota_exceeded()
    {
        var options = new BlocklistArchiveStreamOptions
        {
            MaxRuleLines = 5
        };
        var strictProvider = new BlocklistArchiveStreamProvider(options);

        var text = "1.1.1.1\n2.2.2.2\n3.3.3.3\n4.4.4.4\n5.5.5.5\n6.6.6.6\n";
        var gzipBytes = CreateGzipBytes(text);
        using var stream = new MemoryStream(gzipBytes);

        var ex = Assert.ThrowsAsync<BlocklistQuotaExceededException>(async () =>
        {
            await strictProvider.ExtractRulesAsync(stream, url: "http://example.com/manyrules.gz");
        });

        Assert.That(ex.Message, Does.Contain("limit"));
    }

    [Test]
    public void IsZipSlip_should_detect_directory_traversal_attempts()
    {
        Assert.That(BlocklistArchiveStreamProvider.IsZipSlip("../../evil.txt"), Is.True);
        Assert.That(BlocklistArchiveStreamProvider.IsZipSlip("..\\evil.txt"), Is.True);
        Assert.That(BlocklistArchiveStreamProvider.IsZipSlip("/etc/passwd"), Is.True);
        Assert.That(BlocklistArchiveStreamProvider.IsZipSlip("C:\\windows\\system32\\calc.exe"), Is.True);
        Assert.That(BlocklistArchiveStreamProvider.IsZipSlip("sub/../../evil.txt"), Is.True);

        Assert.That(BlocklistArchiveStreamProvider.IsZipSlip("rules.p2p"), Is.False);
        Assert.That(BlocklistArchiveStreamProvider.IsZipSlip("sub/rules.txt"), Is.False);
    }

    [Test]
    public async Task ExtractRulesAsync_should_reject_zip_slip_entries()
    {
        var entries = new[]
        {
            ("../../evil.txt", "1.2.3.4\n"),
            ("legit.txt", "5.6.7.8\n")
        };
        var zipBytes = CreateZipBytes(entries);
        using var stream = new MemoryStream(zipBytes);

        var rules = await _provider.ExtractRulesAsync(stream, url: "http://example.com/zipslip.zip");

        Assert.That(rules.Count, Is.EqualTo(1));
        Assert.That(rules[0], Is.EqualTo("5.6.7.8"));
    }

    [Test]
    public void ExtractRulesAsync_should_throw_on_corrupted_gzip_stream()
    {
        var validBytes = CreateGzipBytes("1.2.3.4\n5.6.7.8\n9.10.11.12\n" + new string('A', 2000));
        // Corrupt deflate payload bytes after the 10-byte header
        for (var i = 10; i < 25; i++)
        {
            validBytes[i] = 0xFF;
        }

        using var stream = new MemoryStream(validBytes);

        Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await _provider.ExtractRulesAsync(stream, url: "http://example.com/corrupted.gz");
        });
    }

    [Test]
    public void ExtractRulesAsync_should_throw_on_corrupted_zip_stream()
    {
        var corruptBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0xFF, 0xFF };
        using var stream = new MemoryStream(corruptBytes);

        Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await _provider.ExtractRulesAsync(stream, url: "http://example.com/corrupted.zip");
        });
    }

    [Test]
    public async Task PeerBlocklistSyncService_should_seamlessly_sync_gzip_blocklist()
    {
        using var mockHandler = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHandler);
        var now = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

        var service = new PeerBlocklistSyncService(
            httpClient,
            configService: null,
            nowProvider: () => now,
            streamProvider: _provider);

        var gzipBytes = CreateGzipBytes("# Bluetack\n192.168.10.0/24\n10.20.30.40\n");
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(gzipBytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        mockHandler.EnqueueResponse(response);

        var result = await service.SyncAsync("http://example.com/blocklist.gz");

        Assert.That(result.Success, Is.True);
        Assert.That(result.RuleCount, Is.EqualTo(2));
        Assert.That(service.ActiveRules.Count, Is.EqualTo(2));
        Assert.That(service.ActiveRules[0], Is.EqualTo("192.168.10.0/24"));
        Assert.That(service.ActiveRules[1], Is.EqualTo("10.20.30.40"));
    }

    [Test]
    public async Task PeerBlocklistSyncService_should_gracefully_handle_quota_exceeded_and_retain_prior_rules()
    {
        using var mockHandler = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHandler);
        var now = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

        var strictOptions = new BlocklistArchiveStreamOptions
        {
            MaxUncompressedBytes = 100
        };
        var strictProvider = new BlocklistArchiveStreamProvider(strictOptions);

        var service = new PeerBlocklistSyncService(
            httpClient,
            configService: null,
            nowProvider: () => now,
            streamProvider: strictProvider);

        // Pre-set valid active rules
        service.SetActiveRules(new[] { "1.1.1.1", "2.2.2.2" });

        // Enqueue response that exceeds 100 bytes
        var largeText = new string('X', 200) + "\n9.9.9.9\n";
        var gzipBytes = CreateGzipBytes(largeText);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(gzipBytes)
        };
        mockHandler.EnqueueResponse(response);

        var result = await service.SyncAsync("http://example.com/oversized.gz");

        Assert.That(result.Success, Is.False);
        Assert.That(result.RuleCount, Is.EqualTo(2));
        Assert.That(service.ActiveRules.Count, Is.EqualTo(2));
        Assert.That(service.ActiveRules[0], Is.EqualTo("1.1.1.1"));
        Assert.That(service.ActiveRules[1], Is.EqualTo("2.2.2.2"));
        Assert.That(service.Metadata.ConsecutiveFailures, Is.EqualTo(1));
    }

    private static byte[] CreateGzipBytes(string content)
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, Encoding.UTF8))
        {
            writer.Write(content);
        }

        return ms.ToArray();
    }

    private static byte[] CreateZipBytes(IEnumerable<(string Name, string Content)> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }
}
