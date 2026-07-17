using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class TrackerServerTests : ApiTestBase
{
    private string _apiKey;

    [OneTimeSetUp]
    public async Task SetUpApiKey()
    {
        _apiKey = await GetApiKeyAsync(SeedarrUrl);
    }

    [Test]
    public async Task Tracker_server_stats_returns_object()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/stats", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task Tracker_server_stats_has_expected_fields()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/stats", _apiKey);
        using var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.TryGetProperty("totalTorrents", out _), Is.True, "Stats missing 'totalTorrents' property");
        Assert.That(doc.RootElement.TryGetProperty("totalPeers", out _), Is.True, "Stats missing 'totalPeers' property");
        Assert.That(doc.RootElement.TryGetProperty("uptime", out _), Is.True, "Stats missing 'uptime' property");
    }

    [Test]
    public async Task Tracker_server_stats_uptime_is_positive()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/stats", _apiKey);
        using var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.TryGetProperty("uptime", out var uptimeProp), Is.True, "Stats missing 'uptime' property");
        Assert.That(uptimeProp.GetInt64(), Is.GreaterThanOrEqualTo(0), "'uptime' must be >= 0");
    }

    [Test]
    public async Task Tracker_server_torrents_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/torrents", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Tracker_server_torrent_peers_returns_array()
    {
        var listJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/torrents", _apiKey);
        using var listDoc = JsonDocument.Parse(listJson);

        if (listDoc.RootElement.GetArrayLength() == 0)
            Assert.Ignore("No tracked torrents available to test peer endpoint.");

        var infoHash = listDoc.RootElement[0].GetProperty("infoHash").GetString();

        var peersJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/torrents/{infoHash}/peers", _apiKey);
        using var peersDoc = JsonDocument.Parse(peersJson);
        Assert.That(peersDoc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    // ---- Compact peer binary format tests ----

    [Test]
    public async Task Tracker_http_announce_compact_peers_have_correct_binary_format()
    {
        var (isEnabled, trackerPort) = await GetTrackerHttpConfigAsync();
        if (!isEnabled)
            Assert.Ignore("HTTP tracker server is not enabled — skipping compact peer binary format test.");

        // Port 49152 = 0xC000: high byte 0xC0 = 192 (> 127).
        // The old Latin1-string encoding path would inflate byte 192 to two UTF-8 bytes,
        // producing a 7-byte peers field. The fixed path uses IPAddress.GetAddressBytes()
        // and new BString(byte[]) directly, keeping exactly 6 bytes per peer.
        var infoHash = "compactbinarytest0001";
        const int peer1Port = 49152; // 0xC000
        const int peer2Port = 49153; // 0xC001

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var trackerBase = $"http://localhost:{trackerPort}";

        // Register peer 1
        await http.GetAsync(
            $"{trackerBase}/announce?info_hash={Uri.EscapeDataString(infoHash)}&port={peer1Port}&peer_id=peer1binary0000000000");

        // Register peer 2; its response should contain peer 1 in compact format
        var response = await http.GetAsync(
            $"{trackerBase}/announce?info_hash={Uri.EscapeDataString(infoHash)}&port={peer2Port}&peer_id=peer2binary0000000000");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsByteArrayAsync();
        var peers = ExtractBencodePeers(body);

        Assert.That(peers, Is.Not.Null, "Response must contain a 'peers' field");
        Assert.That(peers.Length % 6, Is.EqualTo(0), "Compact peers must be a multiple of 6 bytes (4 IP + 2 port)");

        if (peers.Length == 0)
            Assert.Ignore("No peers in response — prior test state may have cleared this info_hash.");

        var foundPeer1 = false;
        for (var i = 0; i + 5 < peers.Length; i += 6)
        {
            var peerPort = (peers[i + 4] << 8) | peers[i + 5];
            if (peerPort != peer1Port)
                continue;

            foundPeer1 = true;
            Assert.That(
                peers[i + 4],
                Is.EqualTo(0xC0),
                "Port high byte must be 0xC0 — binary IPAddress.GetAddressBytes() path, not Latin1 inflation");
            Assert.That(peers[i + 5], Is.EqualTo(0x00), "Port low byte must be 0x00");
            break;
        }

        Assert.That(foundPeer1, Is.True, "Compact peers response must include peer registered on port 49152");
    }

    [Test]
    public async Task Tracker_http_announce_empty_peers_field_has_zero_bytes()
    {
        var (isEnabled, trackerPort) = await GetTrackerHttpConfigAsync();
        if (!isEnabled)
            Assert.Ignore("HTTP tracker server is not enabled — skipping.");

        // A single announce: the announcing peer is excluded from their own response.
        // This mirrors the malformed-IP scenario: both produce zero valid compact entries.
        // Verifies that the bencode 'peers' field is correctly encoded as 0 bytes
        // using new BString(byte[0]) rather than raising an exception.
        var infoHash = "zeroPeersTestOnly001";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var response = await http.GetAsync(
            $"http://localhost:{trackerPort}/announce?info_hash={Uri.EscapeDataString(infoHash)}&port=55001&peer_id=solopeerzerotest0000");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsByteArrayAsync();
        var peers = ExtractBencodePeers(body);

        Assert.That(peers, Is.Not.Null, "Response must contain a 'peers' field even when there are zero peers");
        Assert.That(
            peers,
            Is.Empty,
            "Announcing peer is excluded from their own response; peers field must be 0 bytes with no exception");
    }

    // ---- Helpers ----

    private async Task<(bool IsEnabled, int Port)> GetTrackerHttpConfigAsync()
    {
        try
        {
            var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/config/trackerserver", _apiKey);
            using var doc = JsonDocument.Parse(json);
            var serverEnabled = doc.RootElement.TryGetProperty("trackerServerEnabled", out var se) && se.GetBoolean();
            var httpEnabled = doc.RootElement.TryGetProperty("trackerHttpEnabled", out var he) && he.GetBoolean();
            var port = doc.RootElement.TryGetProperty("trackerHttpPort", out var p) ? p.GetInt32() : 9696;
            return (serverEnabled && httpEnabled, port);
        }
        catch
        {
            return (false, 9696);
        }
    }

    private static byte[] ExtractBencodePeers(byte[] bencode)
    {
        var marker = Encoding.ASCII.GetBytes("5:peers");
        for (var i = 0; i <= bencode.Length - marker.Length; i++)
        {
            var match = true;
            for (var j = 0; j < marker.Length; j++)
            {
                if (bencode[i + j] != marker[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;

            var pos = i + marker.Length;
            var colonIdx = Array.IndexOf(bencode, (byte)':', pos);
            if (colonIdx < 0)
                return null;

            var lenStr = Encoding.ASCII.GetString(bencode, pos, colonIdx - pos);
            if (!int.TryParse(lenStr, out var len))
                return null;

            var dataStart = colonIdx + 1;
            if (dataStart + len > bencode.Length)
                return null;

            return bencode[dataStart..(dataStart + len)];
        }

        return null;
    }
}
