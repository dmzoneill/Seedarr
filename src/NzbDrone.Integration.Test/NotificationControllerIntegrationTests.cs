using System.Net;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class NotificationControllerIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Create_and_Update_persist_fallback_notification_id()
    {
        var backupResponse = await PostJsonAsync("/api/v1/notification", new
        {
            name = "Integration Backup Webhook",
            implementation = "Webhook",
            settings = "{\"url\":\"https://example.com/backup\"}",
            enable = true,
            onGrab = true
        });
        var backupBody = await backupResponse.Content.ReadAsStringAsync();
        Assert.That(backupResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), backupBody);

        var backup = Deserialize<NotificationResponse>(backupBody);
        Assert.That(backup.Id, Is.GreaterThan(0));
        Assert.That(backup.FallbackNotificationId, Is.Null);

        var createResponse = await PostJsonAsync("/api/v1/notification", new
        {
            name = "Integration Primary Webhook",
            implementation = "Webhook",
            settings = "{\"url\":\"https://example.com/primary\"}",
            enable = true,
            onGrab = true,
            fallbackNotificationId = backup.Id
        });
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), createBody);

        var created = Deserialize<NotificationResponse>(createBody);
        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(created.Id, Is.Not.EqualTo(backup.Id));
        Assert.That(created.FallbackNotificationId, Is.EqualTo(backup.Id));

        var fetchedResponse = await GetAsync($"/api/v1/notification/{created.Id}");
        Assert.That(fetchedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var fetched = Deserialize<NotificationResponse>(await fetchedResponse.Content.ReadAsStringAsync());
        Assert.That(fetched.Name, Is.EqualTo("Integration Primary Webhook"));
        Assert.That(fetched.FallbackNotificationId, Is.EqualTo(backup.Id));

        var updateResponse = await PutJsonAsync($"/api/v1/notification/{created.Id}", new
        {
            name = "Integration Primary Webhook Updated",
            fallbackNotificationId = (int?)null,
            onGrab = true
        });
        var updateBody = await updateResponse.Content.ReadAsStringAsync();
        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), updateBody);

        var updated = Deserialize<NotificationResponse>(updateBody);
        Assert.That(updated.Name, Is.EqualTo("Integration Primary Webhook Updated"));
        Assert.That(updated.FallbackNotificationId, Is.Null);

        var clearedResponse = await GetAsync($"/api/v1/notifications/{created.Id}");
        Assert.That(clearedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cleared = Deserialize<NotificationResponse>(await clearedResponse.Content.ReadAsStringAsync());
        Assert.That(cleared.Name, Is.EqualTo("Integration Primary Webhook Updated"));
        Assert.That(cleared.FallbackNotificationId, Is.Null);

        var restoreResponse = await PutJsonAsync($"/api/v1/notifications/{created.Id}", new
        {
            fallbackNotificationId = backup.Id
        });
        var restoreBody = await restoreResponse.Content.ReadAsStringAsync();
        Assert.That(restoreResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), restoreBody);
        var restored = Deserialize<NotificationResponse>(restoreBody);
        Assert.That(restored.FallbackNotificationId, Is.EqualTo(backup.Id));

        var restoredGet = await GetAsync($"/api/v1/notification/{created.Id}");
        var restoredModel = Deserialize<NotificationResponse>(await restoredGet.Content.ReadAsStringAsync());
        Assert.That(restoredModel.FallbackNotificationId, Is.EqualTo(backup.Id));
        Assert.That(restoredModel.Implementation, Is.EqualTo("Webhook"));

        var deletePrimary = await DeleteAsync($"/api/v1/notification/{created.Id}");
        Assert.That(deletePrimary.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var deleteBackup = await DeleteAsync($"/api/v1/notifications/{backup.Id}");
        Assert.That(deleteBackup.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private sealed class NotificationResponse
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Implementation { get; set; }

        [JsonPropertyName("fallbackNotificationId")]
        public int? FallbackNotificationId { get; set; }
    }
}
