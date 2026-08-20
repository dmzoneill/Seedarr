using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class IndexerControllerTests : IntegrationTestBase
{
    [Test]
    public async Task GetAll_returns_ok_and_list()
    {
        var response = await GetAsync("/api/v1/indexers");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var list = Deserialize<List<IndexerDefinition>>(json);

        Assert.That(list, Is.Not.Null);
    }

    [Test]
    public async Task Create_and_Get_indexer()
    {
        var indexerDef = new
        {
            name = "Test Prowlarr Integration",
            indexerType = "Prowlarr",
            url = "http://localhost:9696",
            apiKey = "secretapikey",
            apiPath = "/api",
            enableRss = true,
            enableSearch = true,
            enable = true
        };

        var postResponse = await PostJsonAsync("/api/v1/indexers", indexerDef);
        Assert.That(postResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var createdJson = await postResponse.Content.ReadAsStringAsync();
        var created = Deserialize<IndexerDefinition>(createdJson);

        Assert.That(created, Is.Not.Null);
        Assert.That(created.Id, Is.GreaterThan(0));
        Assert.That(created.Name, Is.EqualTo("Test Prowlarr Integration"));
        Assert.That(created.ApiKey, Does.StartWith("*"));

        var getResponse = await GetAsync($"/api/v1/indexers/{created.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var fetchedJson = await getResponse.Content.ReadAsStringAsync();
        var fetched = Deserialize<IndexerDefinition>(fetchedJson);
        Assert.That(fetched.Id, Is.EqualTo(created.Id));

        // Cleanup
        await DeleteAsync($"/api/v1/indexers/{created.Id}");
    }

    [Test]
    public async Task TestDirect_with_invalid_indexer_type_returns_failure()
    {
        var invalidDef = new
        {
            name = "Invalid Indexer",
            indexerType = "UnknownType",
            url = "http://localhost:9696",
            enable = true
        };

        var response = await PostJsonAsync("/api/v1/indexers/test", invalidDef);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var result = Deserialize<IndexerTestResult>(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Unknown indexer type"));
    }

    [Test]
    public async Task TestDirect_with_unreachable_url_returns_detailed_failure()
    {
        var def = new
        {
            name = "Unreachable Prowlarr",
            indexerType = "Prowlarr",
            url = "http://127.0.0.1:59999",
            apiKey = "testkey",
            enable = true
        };

        var response = await PostJsonAsync("/api/v1/indexers/test", def);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var result = Deserialize<IndexerTestResult>(json);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("Unable to connect to Prowlarr"));
    }

    [Test]
    public async Task TestConnection_for_nonexistent_indexer_returns_not_found()
    {
        var response = await PostJsonAsync("/api/v1/indexers/999999/test", new { });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
