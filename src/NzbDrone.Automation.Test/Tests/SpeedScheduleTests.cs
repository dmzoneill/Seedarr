using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class SpeedScheduleTests : ApiTestBase
{
    private static readonly object CreatePayload = new
    {
        name = "TestSchedule-Test",
        days = 62,
        startTime = "08:00",
        endTime = "17:00",
        maxUploadSpeed = 1024,
        maxDownloadSpeed = 0,
        isEnabled = true,
        priority = 1
    };

    [SetUp]
    public async Task SetUp()
    {
        await CleanupTestSchedulesAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await CleanupTestSchedulesAsync();
    }

    private async Task CleanupTestSchedulesAsync()
    {
        try
        {
            var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/speedschedule");
            using var doc = JsonDocument.Parse(json);

            foreach (var schedule in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var name = schedule.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString() ?? string.Empty
                        : string.Empty;

                    if (name.StartsWith("TestSchedule-"))
                    {
                        var id = schedule.GetProperty("id").GetInt32();
                        await DeleteAsync($"{SeedarrUrl}/api/v1/speedschedule/{id}");
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
    public async Task Speed_schedule_list_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/speedschedule");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Create_speed_schedule_returns_created()
    {
        var json = await PostJsonAsync($"{SeedarrUrl}/api/v1/speedschedule", CreatePayload);
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.GetProperty("id").GetInt32();
        var name = doc.RootElement.GetProperty("name").GetString();

        Assert.That(id, Is.GreaterThan(0));
        Assert.That(name, Is.EqualTo("TestSchedule-Test"));
    }

    [Test]
    public async Task Get_speed_schedule_by_id()
    {
        var createJson = await PostJsonAsync($"{SeedarrUrl}/api/v1/speedschedule", CreatePayload);
        using var createDoc = JsonDocument.Parse(createJson);
        var id = createDoc.RootElement.GetProperty("id").GetInt32();

        var getJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/speedschedule/{id}");
        using var getDoc = JsonDocument.Parse(getJson);
        var name = getDoc.RootElement.GetProperty("name").GetString();

        Assert.That(name, Is.EqualTo("TestSchedule-Test"));
    }

    [Test]
    public async Task Update_speed_schedule_changes_name()
    {
        var createJson = await PostJsonAsync($"{SeedarrUrl}/api/v1/speedschedule", CreatePayload);
        using var createDoc = JsonDocument.Parse(createJson);
        var id = createDoc.RootElement.GetProperty("id").GetInt32();

        var updatePayload = new
        {
            id,
            name = "TestSchedule-Updated",
            days = 62,
            startTime = "08:00",
            endTime = "17:00",
            maxUploadSpeed = 2048,
            maxDownloadSpeed = 0,
            isEnabled = true,
            priority = 1
        };

        var (statusCode, _) = await PutJsonAsync($"{SeedarrUrl}/api/v1/speedschedule/{id}", updatePayload);
        Assert.That(statusCode, Is.EqualTo(200).Or.EqualTo(202));
    }

    [Test]
    public async Task Delete_speed_schedule_removes_it()
    {
        var createJson = await PostJsonAsync($"{SeedarrUrl}/api/v1/speedschedule", CreatePayload);
        using var createDoc = JsonDocument.Parse(createJson);
        var id = createDoc.RootElement.GetProperty("id").GetInt32();

        await DeleteAsync($"{SeedarrUrl}/api/v1/speedschedule/{id}");

        var listJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/speedschedule");
        using var listDoc = JsonDocument.Parse(listJson);

        var found = false;
        foreach (var schedule in listDoc.RootElement.EnumerateArray())
        {
            if (schedule.TryGetProperty("id", out var idProp) && idProp.GetInt32() == id)
            {
                found = true;
                break;
            }
        }

        Assert.That(found, Is.False, $"Speed schedule with id {id} should have been deleted");
    }

    [Test]
    public async Task Active_speed_schedule_returns_result()
    {
        var response = await Client.GetAsync($"{SeedarrUrl}/api/v1/speedschedule/active");
        Assert.That((int)response.StatusCode, Is.EqualTo(200));
    }
}
