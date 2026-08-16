using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class WebhookControllerTests : IntegrationTestBase
{
    [Test]
    public async Task PostWebhook_with_Download_event_returns_ignored_message()
    {
        var payload = new { eventType = "Download", instanceName = "Sonarr" };
        var response = await PostJsonAsync("/api/v1/webhook/arr", payload);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var result = Deserialize<Dictionary<string, object>>(json);

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

        var response = await PostJsonAsync("/api/v1/webhook/arr", payload);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var result = Deserialize<Dictionary<string, object>>(json);

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

        var response = await PostJsonAsync("/api/v1/webhook/arr", payload);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var result = Deserialize<Dictionary<string, object>>(json);

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

        // First request - should succeed
        var first = await PostJsonAsync("/api/v1/webhook/arr", payload);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Second request - same hash, should say already exists
        var second = await PostJsonAsync("/api/v1/webhook/arr", payload);
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await second.Content.ReadAsStringAsync();
        var result = Deserialize<Dictionary<string, object>>(json);

        Assert.That(result["message"].ToString(), Does.Contain("already exists"));
    }
}
