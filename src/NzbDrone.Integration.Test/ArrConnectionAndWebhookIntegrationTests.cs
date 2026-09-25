using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class ArrConnectionAndWebhookIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task ArrConnection_Crud_EndToEndFlow_WithWebhookRegistration_Succeeds()
    {
        using var mockArr = new MockArrServer();

        // 1. Create a new Arr Connection for Sonarr pointing to mock Arr server
        var newConnection = new
        {
            name = "Integration Sonarr",
            arrType = "Sonarr",
            url = mockArr.Url,
            apiKey = "sonarr-mock-key",
            enable = true,
            syncEnabled = true,
            enableAutomaticAdd = true,
            webhookEnabled = true,
            webhookHost = "seedarr.local:9898"
        };

        var postResponse = await PostJsonAsync("/api/v1/arrconnections", newConnection);
        Assert.That(postResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var created = Deserialize<ArrConnectionDefinition>(await postResponse.Content.ReadAsStringAsync());
        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(created.Name, Is.EqualTo("Integration Sonarr"));
        Assert.That(created.ArrType, Is.EqualTo("Sonarr"));
        Assert.That(created.Implementation, Is.EqualTo("SonarrConnection"));
        Assert.That(created.ConfigContract, Is.EqualTo("ArrConnectionDefinition"));

        // Verify webhook was registered in the mock Arr instance
        Assert.That(mockArr.Notifications.Count, Is.EqualTo(1));
        Assert.That(mockArr.Notifications[0]["name"].ToString(), Is.EqualTo("Seedarr"));

        // 2. Query connection by ID
        var getResponse = await GetAsync($"/api/v1/arrconnections/{created.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // 3. Update connection omitting Implementation and ConfigContract (verifies NOT NULL fallback fix)
        var updatePayload = new
        {
            id = created.Id,
            name = "Integration Sonarr Updated",
            arrType = "Sonarr",
            url = mockArr.Url,
            enable = true,
            syncEnabled = true,
            webhookEnabled = true,
            webhookHost = "seedarr.local:9898"
        };

        var putResponse = await PutJsonAsync($"/api/v1/arrconnections/{created.Id}", updatePayload);
        Assert.That(putResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updated = Deserialize<ArrConnectionDefinition>(await putResponse.Content.ReadAsStringAsync());
        Assert.That(updated.Name, Is.EqualTo("Integration Sonarr Updated"));
        Assert.That(updated.Implementation, Is.EqualTo("SonarrConnection"));

        // 4. Delete connection
        var deleteResponse = await DeleteAsync($"/api/v1/arrconnections/{created.Id}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify webhook was unregistered from mock Arr instance
        Assert.That(mockArr.Notifications.Count, Is.EqualTo(0));

        // Verify 404 on get
        var getDeleted = await GetAsync($"/api/v1/arrconnections/{created.Id}");
        Assert.That(getDeleted.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ArrConnection_WhenLeecharrWebhookExists_DoesNotOverwriteLeecharr_AndRegistersSeedarrAlongside()
    {
        using var mockArr = new MockArrServer();

        // Pre-register an existing Leecharr webhook notification in Sonarr
        var leecharrWebhookUrl = "http://leecharr.local:7889/api/v1/webhook/arr";
        mockArr.AddExistingNotification("Leecharr", leecharrWebhookUrl);
        Assert.That(mockArr.Notifications.Count, Is.EqualTo(1));

        // Register Seedarr connection to the same Sonarr instance
        var seedarrConnection = new
        {
            name = "Shared Sonarr",
            arrType = "Sonarr",
            url = mockArr.Url,
            apiKey = "sonarr-mock-key",
            enable = true,
            syncEnabled = true,
            enableAutomaticAdd = true,
            webhookEnabled = true,
            webhookHost = "seedarr.local:9898"
        };

        var postResponse = await PostJsonAsync("/api/v1/arrconnections", seedarrConnection);
        Assert.That(postResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var created = Deserialize<ArrConnectionDefinition>(await postResponse.Content.ReadAsStringAsync());

        // CRITICAL CHECK: Both Leecharr and Seedarr must exist concurrently!
        // Seedarr must NOT have clobbered or overwritten Leecharr's webhook.
        Assert.That(mockArr.Notifications.Count, Is.EqualTo(2));

        var leecharrNotif = mockArr.Notifications.Find(n => n["name"].ToString() == "Leecharr");
        Assert.That(leecharrNotif, Is.Not.Null);

        var seedarrNotif = mockArr.Notifications.Find(n => n["name"].ToString() == "Seedarr");
        Assert.That(seedarrNotif, Is.Not.Null);

        // Verify updating Seedarr's connection only updates Seedarr's notification and leaves Leecharr intact
        var updatePayload = new
        {
            id = created.Id,
            name = "Shared Sonarr Reconfigured",
            arrType = "Sonarr",
            url = mockArr.Url,
            enable = true,
            syncEnabled = true,
            webhookEnabled = true,
            webhookHost = "seedarr.local:9898"
        };
        var putResponse = await PutJsonAsync($"/api/v1/arrconnections/{created.Id}", updatePayload);
        Assert.That(putResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That(mockArr.Notifications.Count, Is.EqualTo(2));
        Assert.That(mockArr.Notifications.Find(n => n["name"].ToString() == "Leecharr"), Is.Not.Null);

        // Cleanup
        await DeleteAsync($"/api/v1/arrconnections/{created.Id}");
    }

    [Test]
    public async Task ArrConnection_Prowlarr_SkipsWebhookRegistration_WithoutErrors()
    {
        using var mockArr = new MockArrServer();

        var prowlarrConnection = new
        {
            name = "Integration Prowlarr",
            arrType = "Prowlarr",
            url = mockArr.Url,
            apiKey = "prowlarr-mock-key",
            enable = true,
            syncEnabled = true,
            webhookEnabled = true
        };

        var postResponse = await PostJsonAsync("/api/v1/arrconnections", prowlarrConnection);
        Assert.That(postResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var created = Deserialize<ArrConnectionDefinition>(await postResponse.Content.ReadAsStringAsync());
        Assert.That(created.Id, Is.GreaterThan(0));

        // Prowlarr has no notification endpoint: webhook registration must have been skipped
        Assert.That(mockArr.Notifications.Count, Is.EqualTo(0));

        // Cleanup
        await DeleteAsync($"/api/v1/arrconnections/{created.Id}");
    }

    [Test]
    public async Task WebhookReceiver_RoutesAndAuth_SingularAndPlural_HandleEvents()
    {
        var testPayload = new
        {
            eventType = "Test",
            instanceName = "Sonarr"
        };

        var infoHash = (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..40];
        var grabPayload = new
        {
            eventType = "Grab",
            instanceName = "Sonarr",
            downloadId = infoHash,
            release = new { releaseTitle = "Integration.Test.Show.S01E01", size = 1073741824L }
        };

        // 1. Anonymous request without API key to /api/v1/webhook/arr must be rejected (401 or 403)
        using var anonClient = new HttpClient { BaseAddress = Client.BaseAddress };
        var anonSingularContent = new StringContent(JsonSerializer.Serialize(testPayload), Encoding.UTF8, "application/json");
        var anonSingularResponse = await anonClient.PostAsync("/api/v1/webhook/arr", anonSingularContent);
        Assert.That(anonSingularResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized).Or.EqualTo(HttpStatusCode.Forbidden));

        // 2. Anonymous request without API key to /api/v1/webhooks/arr (plural alias) must also be rejected (401 or 403)
        var anonPluralContent = new StringContent(JsonSerializer.Serialize(testPayload), Encoding.UTF8, "application/json");
        var anonPluralResponse = await anonClient.PostAsync("/api/v1/webhooks/arr", anonPluralContent);
        Assert.That(anonPluralResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized).Or.EqualTo(HttpStatusCode.Forbidden));

        // 3. Authenticated request to /api/v1/webhook/arr with 'Test' event returns 200 OK
        var authSingularResponse = await PostJsonAsync("/api/v1/webhook/arr", testPayload);
        Assert.That(authSingularResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var singularResult = Deserialize<Dictionary<string, object>>(await authSingularResponse.Content.ReadAsStringAsync());
        Assert.That(singularResult.ContainsKey("message"), Is.True);
        Assert.That(singularResult["message"].ToString(), Does.Contain("Seedarr webhook connection test successful"));

        // 4. Authenticated request to /api/v1/webhooks/arr (plural alias) with 'Test' event returns 200 OK
        var authPluralResponse = await PostJsonAsync("/api/v1/webhooks/arr", testPayload);
        Assert.That(authPluralResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var pluralResult = Deserialize<Dictionary<string, object>>(await authPluralResponse.Content.ReadAsStringAsync());
        Assert.That(pluralResult.ContainsKey("message"), Is.True);
        Assert.That(pluralResult["message"].ToString(), Does.Contain("Seedarr webhook connection test successful"));

        // 5. Authenticated request to /api/v1/webhooks/arr with 'Grab' event returns 200 OK
        var authGrabResponse = await PostJsonAsync("/api/v1/webhooks/arr", grabPayload);
        Assert.That(authGrabResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var grabResult = Deserialize<Dictionary<string, object>>(await authGrabResponse.Content.ReadAsStringAsync());
        Assert.That(grabResult["success"].ToString(), Is.EqualTo("True"));
    }
}
