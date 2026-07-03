using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class PeerLogTests : ApiTestBase
{
    [Test]
    public async Task Peerlog_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/peerlog");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Peerlog_entries_have_required_fields()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/peerlog");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetArrayLength() == 0)
            Assert.Ignore("No peer log entries; skipping field check.");

        var first = doc.RootElement[0];
        Assert.That(first.TryGetProperty("remoteIp", out _), Is.True, "Peer log entry missing 'remoteIp'");
        Assert.That(first.TryGetProperty("remotePort", out _), Is.True, "Peer log entry missing 'remotePort'");
        Assert.That(first.TryGetProperty("eventType", out _), Is.True, "Peer log entry missing 'eventType'");
        Assert.That(first.TryGetProperty("timestamp", out _), Is.True, "Peer log entry missing 'timestamp'");
    }

    [Test]
    public async Task Peerlog_active_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/peerlog/active");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Peerlog_graph_returns_object()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/peerlog/graph");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task Peerlog_graph_has_nodes_and_links()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/peerlog/graph");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.TryGetProperty("nodes", out var nodes), Is.True, "Graph missing 'nodes'");
        Assert.That(root.TryGetProperty("links", out var links), Is.True, "Graph missing 'links'");
        Assert.That(nodes.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(links.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Peerlog_graph_always_has_seedarr_node()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/peerlog/graph");
        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");

        var hasSeedarrNode = false;
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.TryGetProperty("id", out var id) && id.GetString() == "seedarr")
            {
                hasSeedarrNode = true;
                break;
            }
        }

        Assert.That(hasSeedarrNode, Is.True, "Graph must always contain the 'seedarr' center node");
    }

    [Test]
    public async Task Peerlog_purge_returns_ok()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{SeedarrUrl}/api/v1/peerlog");
        var response = await Client.SendAsync(request);
        Assert.That((int)response.StatusCode, Is.EqualTo(200));
    }
}
