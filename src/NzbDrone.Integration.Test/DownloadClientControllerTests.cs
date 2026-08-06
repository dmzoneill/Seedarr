using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class DownloadClientControllerTests : IntegrationTestBase
{
    [Test]
    public async Task GetAll_returns_ok_and_list()
    {
        var response = await GetAsync("/api/v1/downloadclients");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var list = Deserialize<List<DownloadClientDefinition>>(json);

        Assert.That(list, Is.Not.Null);
    }

    [Test]
    public async Task Create_and_Get_download_client()
    {
        var clientDef = new
        {
            name = "Test qBittorrent Integration",
            clientType = "QBitTorrent",
            host = "localhost",
            port = 8080,
            useSsl = false,
            username = "admin",
            password = "secretpassword",
            category = "seedarr",
            enable = true
        };

        var postResponse = await PostJsonAsync("/api/v1/downloadclients", clientDef);
        Assert.That(postResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var createdJson = await postResponse.Content.ReadAsStringAsync();
        var created = Deserialize<DownloadClientDefinition>(createdJson);

        Assert.That(created, Is.Not.Null);
        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(created.Name, Is.EqualTo("Test qBittorrent Integration"));
        Assert.That(created.Password, Is.EqualTo("********"));

        var getResponse = await GetAsync($"/api/v1/downloadclients/{created.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var fetchedJson = await getResponse.Content.ReadAsStringAsync();
        var fetched = Deserialize<DownloadClientDefinition>(fetchedJson);
        Assert.That(fetched.Id, Is.EqualTo(created.Id));
        Assert.That(fetched.Password, Is.EqualTo("********"));

        // Cleanup
        await DeleteAsync($"/api/v1/downloadclients/{created.Id}");
    }

    [Test]
    public async Task TestDirect_with_invalid_client_type_returns_failure()
    {
        var invalidDef = new
        {
            name = "Invalid Client",
            clientType = "UnknownType",
            host = "localhost",
            port = 8080,
            enable = true
        };

        var response = await PostJsonAsync("/api/v1/downloadclients/test", invalidDef);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var result = Deserialize<DownloadClientTestResult>(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Unknown client type"));
    }

    [Test]
    public async Task TestConnection_for_nonexistent_client_returns_not_found()
    {
        var response = await PostJsonAsync("/api/v1/downloadclients/999999/test", new { });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetItems_for_nonexistent_client_returns_not_found()
    {
        var response = await GetAsync("/api/v1/downloadclients/999999/items");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ImportTorrent_for_nonexistent_client_returns_bad_request()
    {
        var response = await PostJsonAsync("/api/v1/downloadclients/999999/import/deadbeef", new { });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
