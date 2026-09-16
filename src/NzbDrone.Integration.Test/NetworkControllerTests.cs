using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class NetworkControllerTests : IntegrationTestBase
{
    [Test]
    public async Task TestPort_returns_200_and_port_test_result()
    {
        var response = await Client.PostAsync("/api/v1/network/test-port", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.TryGetProperty("port", out var portProp), Is.True);
        Assert.That(portProp.GetInt32(), Is.GreaterThan(0));

        Assert.That(root.TryGetProperty("externalIp", out _), Is.True);
        Assert.That(root.TryGetProperty("isOpen", out _), Is.True);
        Assert.That(root.TryGetProperty("responseTime", out _), Is.True);
    }

    [Test]
    public async Task TestPort_with_explicit_port_returns_200()
    {
        var response = await Client.PostAsync("/api/v1/network/test-port?port=6881", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("port").GetInt32(), Is.EqualTo(6881));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    public async Task TestPort_with_invalid_port_returns_400(int invalidPort)
    {
        var response = await Client.PostAsync($"/api/v1/network/test-port?port={invalidPort}", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
