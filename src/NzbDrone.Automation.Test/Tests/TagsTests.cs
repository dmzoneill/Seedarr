using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class TagsTests : ApiTestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await CleanupTestTagsAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await CleanupTestTagsAsync();
    }

    private async Task CleanupTestTagsAsync()
    {
        try
        {
            var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/tag");
            using var doc = JsonDocument.Parse(json);

            foreach (var tag in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var label = tag.TryGetProperty("label", out var labelProp)
                        ? labelProp.GetString() ?? string.Empty
                        : string.Empty;

                    if (label.StartsWith("TestTag-"))
                    {
                        var id = tag.GetProperty("id").GetInt32();
                        await DeleteAsync($"{SeedarrUrl}/api/v1/tag/{id}");
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    [Test]
    public async Task Tags_endpoint_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/tag");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Create_tag_returns_created_tag()
    {
        var json = await PostJsonAsync($"{SeedarrUrl}/api/v1/tag", new { label = "TestTag-Create" });
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.GetProperty("id").GetInt32();
        var label = doc.RootElement.GetProperty("label").GetString();
        Assert.That(id, Is.GreaterThan(0), "Created tag should have id > 0");
        Assert.That(label, Is.EqualTo("TestTag-Create"), "Created tag should have the submitted label");
    }

    [Test]
    public async Task Get_tag_by_id_returns_correct_tag()
    {
        var createJson = await PostJsonAsync($"{SeedarrUrl}/api/v1/tag", new { label = "TestTag-GetById" });
        using var createDoc = JsonDocument.Parse(createJson);
        var id = createDoc.RootElement.GetProperty("id").GetInt32();

        var getJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/tag/{id}");
        using var getDoc = JsonDocument.Parse(getJson);
        var label = getDoc.RootElement.GetProperty("label").GetString();
        Assert.That(label, Is.EqualTo("TestTag-GetById"), "GET by id should return the tag with the correct label");
    }

    [Test]
    public async Task Update_tag_changes_label()
    {
        var createJson = await PostJsonAsync($"{SeedarrUrl}/api/v1/tag", new { label = "TestTag-BeforeUpdate" });
        using var createDoc = JsonDocument.Parse(createJson);
        var id = createDoc.RootElement.GetProperty("id").GetInt32();

        var (_, putBody) = await PutJsonAsync($"{SeedarrUrl}/api/v1/tag", new { id, label = "TestTag-Updated" });
        using var putDoc = JsonDocument.Parse(putBody);
        var label = putDoc.RootElement.GetProperty("label").GetString();
        Assert.That(label, Is.EqualTo("TestTag-Updated"), "PUT should update the tag label");
    }

    [Test]
    public async Task Delete_tag_removes_it()
    {
        var createJson = await PostJsonAsync($"{SeedarrUrl}/api/v1/tag", new { label = "TestTag-Delete" });
        using var createDoc = JsonDocument.Parse(createJson);
        var id = createDoc.RootElement.GetProperty("id").GetInt32();

        await DeleteAsync($"{SeedarrUrl}/api/v1/tag/{id}");

        var allJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/tag");
        using var allDoc = JsonDocument.Parse(allJson);
        foreach (var tag in allDoc.RootElement.EnumerateArray())
        {
            var tagId = tag.GetProperty("id").GetInt32();
            Assert.That(tagId, Is.Not.EqualTo(id), $"Deleted tag with id {id} should not appear in the tags list");
        }
    }
}
