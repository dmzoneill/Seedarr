using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class LogApiTests : ApiTestBase
{
    // --- In-memory log endpoint ---

    [Test]
    public async Task Log_endpoint_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/log");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Log_with_count_param_limits_results()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/log?count=5");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(doc.RootElement.GetArrayLength(), Is.LessThanOrEqualTo(5));
    }

    [Test]
    public async Task Log_with_level_filter_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/log?level=Info");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Log_entries_have_required_fields()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/log");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetArrayLength() == 0)
            Assert.Ignore("No log entries in ring buffer; skipping field check.");

        var first = doc.RootElement[0];
        Assert.That(first.TryGetProperty("time", out _), Is.True, "Log entry missing 'time'");
        Assert.That(first.TryGetProperty("level", out _), Is.True, "Log entry missing 'level'");
        Assert.That(first.TryGetProperty("logger", out _), Is.True, "Log entry missing 'logger'");
        Assert.That(first.TryGetProperty("message", out _), Is.True, "Log entry missing 'message'");
    }

    // --- Log file endpoint ---

    [Test]
    public async Task Logfile_list_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/logfile");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Logfile_entries_have_required_fields()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/logfile");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetArrayLength() == 0)
            Assert.Ignore("No log files present; skipping field check.");

        var first = doc.RootElement[0];
        Assert.That(first.TryGetProperty("filename", out var filenameProp), Is.True, "Log file entry missing 'filename'");
        Assert.That(filenameProp.GetString(), Is.Not.Null.And.Not.Empty, "'filename' must be non-empty");
        Assert.That(first.TryGetProperty("size", out var sizeProp), Is.True, "Log file entry missing 'size'");
        Assert.That(sizeProp.GetInt64(), Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task Download_logfile_returns_text()
    {
        var listJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/logfile");
        using var listDoc = JsonDocument.Parse(listJson);
        if (listDoc.RootElement.GetArrayLength() == 0)
            Assert.Ignore("No log files present; skipping download test.");

        var filename = listDoc.RootElement[0].GetProperty("filename").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{SeedarrUrl}/api/v1/logfile/{filename}");
        var response = await Client.SendAsync(request);

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.That(contentType, Is.EqualTo("text/plain"), "Log file download should return text/plain");
    }

    [Test]
    public async Task Clear_logfiles_returns_ok()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{SeedarrUrl}/api/v1/logfile");
        var response = await Client.SendAsync(request);
        Assert.That((int)response.StatusCode, Is.EqualTo(200));
    }
}
