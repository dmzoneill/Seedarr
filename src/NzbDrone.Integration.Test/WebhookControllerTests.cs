using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class WebhookControllerTests : IntegrationTestBase
{
    [OneTimeSetUp]
    public async Task CreateTestArrConnectionAsync()
    {
        var connection = new
        {
            enable = true,
            webhookEnabled = true,
            name = "TestConnection",
            implementation = "SonarrConnection",
            arrType = "Sonarr",
            url = "http://localhost:8989",
            apiKey = "test-api-key",
        };
        var response = await PostJsonAsync("/api/v1/arrconnections", connection);
        response.EnsureSuccessStatusCode();
    }

    private async Task<(HttpStatusCode Status, Dictionary<string, object> Body)> PostWebhookAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhook/arr") { Content = content };
        var response = await Client.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, Deserialize<Dictionary<string, object>>(responseJson));
    }

    [Test]
    public async Task PostWebhook_with_Download_event_returns_ignored_message()
    {
        var (status, result) = await PostWebhookAsync(new { eventType = "Download", instanceName = "Sonarr" });

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result["message"].ToString(), Does.Contain("Ignored event type"));
    }

    [Test]
    public async Task PostWebhook_with_Grab_and_no_downloadId_returns_error_message()
    {
        var payload = new
        {
            eventType = "Grab",
            instanceName = "Sonarr",
            release = new { releaseTitle = "Some.Show.S01E01" }
        };

        var (status, result) = await PostWebhookAsync(payload);

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result["message"].ToString(), Does.Contain("No downloadId"));
    }

    [Test]
    public async Task PostWebhook_with_valid_Grab_returns_success_with_infoHash()
    {
        var infoHash = (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..40];
        var payload = new
        {
            eventType = "Grab",
            instanceName = "Sonarr",
            downloadId = infoHash,
            release = new { releaseTitle = "Some.Show.S01E01", size = 1073741824L }
        };

        var (status, result) = await PostWebhookAsync(payload);

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result["success"].ToString(), Is.EqualTo("True"));
        Assert.That(result.ContainsKey("infoHash"), Is.True);
        Assert.That(result["infoHash"].ToString(), Is.Not.Empty);
    }

    [Test]
    public async Task PostWebhook_same_hash_twice_returns_already_exists()
    {
        var infoHash = (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..40];
        var payload = new
        {
            eventType = "Grab",
            instanceName = "Radarr",
            downloadId = infoHash,
            release = new { releaseTitle = "Some.Movie.2024", size = 2147483648L }
        };

        var (firstStatus, _) = await PostWebhookAsync(payload);
        Assert.That(firstStatus, Is.EqualTo(HttpStatusCode.OK));

        var (secondStatus, secondResult) = await PostWebhookAsync(payload);
        Assert.That(secondStatus, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(secondResult["message"].ToString(), Does.Contain("already exists"));
    }
}
