using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Host;

namespace NzbDrone.Integration.Test;

public sealed class SeedarrWebApplicationFactory : IDisposable
{
    private readonly WebApplication _app;
    private readonly string _tempDir;
    private bool _disposed;

    public string BaseUrl { get; }

    public string ApiKey { get; private set; } = string.Empty;

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    public SeedarrWebApplicationFactory()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "seedarr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var startupContext = new StartupContext("--data=" + _tempDir);
        _app = Bootstrap.CreateApplication(startupContext, new[] { "http://127.0.0.1:0" });
        _app.StartAsync().GetAwaiter().GetResult();

        var server = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addressesFeature = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        BaseUrl = addressesFeature?.Addresses?.FirstOrDefault() ?? "http://127.0.0.1:0";

        LoadApiKey();
        Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        if (!string.IsNullOrEmpty(ApiKey))
        {
            Client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        }

        WaitForHealthy();
    }

    private void LoadApiKey()
    {
        try
        {
            var configFile = Path.Combine(_tempDir, "config.xml");
            if (!File.Exists(configFile))
            {
                return;
            }

            using var stream = File.OpenRead(configFile);
            var doc = System.Xml.Linq.XDocument.Load(stream);
            ApiKey = doc.Root?.Element("ApiKey")?.Value ?? string.Empty;
        }
        catch
        {
            // Key stays empty; tests that need it will fail explicitly.
        }
    }

    public HttpClient CreateClient()
    {
        return new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Client?.Dispose();

        try
        {
            _app?.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort
        }

        try
        {
            _app?.DisposeAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort
        }

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    private void WaitForHealthy()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = Client.GetAsync("/api/v1/system/status").GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Not ready yet
            }

            Thread.Sleep(200);
        }

        throw new TimeoutException("Seedarr did not become healthy within 30 seconds");
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
