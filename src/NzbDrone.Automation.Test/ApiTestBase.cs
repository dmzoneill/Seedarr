using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test;

[TestFixture]
[Category("AutomationTest")]
public abstract class ApiTestBase
{
    protected static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    protected string SeedarrUrl { get; private set; }
    protected string SonarrUrl { get; private set; }
    protected string RadarrUrl { get; private set; }
    protected string LidarrUrl { get; private set; }
    protected string ProwlarrUrl { get; private set; }
    protected string TransmissionUrl { get; private set; }

    [OneTimeSetUp]
    public void SetUpUrls()
    {
        SeedarrUrl = Environment.GetEnvironmentVariable("SEEDARR_URL") ?? "http://localhost:9898";
        SonarrUrl = Environment.GetEnvironmentVariable("SONARR_URL") ?? "http://localhost:8989";
        RadarrUrl = Environment.GetEnvironmentVariable("RADARR_URL") ?? "http://localhost:7878";
        LidarrUrl = Environment.GetEnvironmentVariable("LIDARR_URL") ?? "http://localhost:8686";
        ProwlarrUrl = Environment.GetEnvironmentVariable("PROWLARR_URL") ?? "http://localhost:9696";
        TransmissionUrl = Environment.GetEnvironmentVariable("TRANSMISSION_URL") ?? "http://localhost:9091";
    }

    protected async Task<string> GetJsonAsync(string url, string apiKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Add("X-Api-Key", apiKey);
        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    protected async Task<string> PostJsonAsync(string url, object body, string apiKey = null)
    {
        var response = await SendWithJsonBodyAsync(HttpMethod.Post, url, JsonSerializer.Serialize(body), apiKey);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    protected async Task<(int StatusCode, string Body)> PutJsonAsync(string url, object body, string apiKey = null)
    {
        var response = await SendWithJsonBodyAsync(HttpMethod.Put, url, JsonSerializer.Serialize(body), apiKey);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> SendWithJsonBodyAsync(HttpMethod method, string url, string json, string apiKey)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Add("X-Api-Key", apiKey);
        return await Client.SendAsync(request);
    }

    protected async Task<bool> DeleteAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        var response = await Client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    protected async Task<string> GetApiKeyAsync(string baseUrl)
    {
        try
        {
            var json = await GetJsonAsync($"{baseUrl}/initialize.json");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("apiKey", out var apiKeyElement))
                return apiKeyElement.GetString() ?? string.Empty;
        }
        catch
        {
        }

        return string.Empty;
    }

    protected async Task CleanupTorrentsAsync()
    {
        try
        {
            var apiKey = await GetApiKeyAsync(SeedarrUrl);
            var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent", apiKey);
            using var doc = JsonDocument.Parse(json);

            foreach (var torrent in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var name = torrent.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString() ?? string.Empty
                        : string.Empty;

                    if (name.Contains("Integration.Test") ||
                        name.Contains("VideoHive") ||
                        name.Contains("Matrix"))
                    {
                        var id = torrent.GetProperty("id").GetInt32();
                        await DeleteAsync($"{SeedarrUrl}/api/v1/torrent/{id}");
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

    protected async Task<string> TransmissionRpcAsync(string method, object arguments)
    {
        var rpcUrl = $"{TransmissionUrl}/transmission/rpc";

        string sessionId;

        // Transmission requires a session ID obtained from the 409 response header.
        using (var probe = new HttpRequestMessage(HttpMethod.Get, rpcUrl))
        {
            var probeResponse = await Client.SendAsync(probe);
            sessionId = probeResponse.Headers.TryGetValues("X-Transmission-Session-Id", out var values)
                ? string.Join("", values)
                : string.Empty;
        }

        var payload = JsonSerializer.Serialize(new { method, arguments });
        using var request = new HttpRequestMessage(HttpMethod.Post, rpcUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await Client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    protected static string FindTorrentFixturePath()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "tests", "fixtures", "test.torrent");
            if (File.Exists(candidate))
                return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir)
                break;
            dir = parent;
        }

        return string.Empty;
    }

    protected async Task<JsonDocument> UploadTestTorrentAsync()
    {
        var torrentPath = FindTorrentFixturePath();
        if (string.IsNullOrEmpty(torrentPath))
            return null;

        var fileBytes = await File.ReadAllBytesAsync(torrentPath);
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
        form.Add(fileContent, "file", "test.torrent");

        var response = await Client.PostAsync($"{SeedarrUrl}/api/v1/torrent/upload", form);
        if (!response.IsSuccessStatusCode)
            return null;

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    protected async Task<string> RunCommandAsync(string command, string args)
    {
        var psi = new ProcessStartInfo(command, args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        var stdout = await process!.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return stdout;
    }
}
