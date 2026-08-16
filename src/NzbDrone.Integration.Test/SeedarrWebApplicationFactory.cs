using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Host;

namespace NzbDrone.Integration.Test;

public sealed class SeedarrWebApplicationFactory : IDisposable
{
    private readonly WebApplication _app;
    private readonly string _tempDir;
    private bool _disposed;

    public string BaseUrl { get; }

    public HttpClient Client { get; }

    public SeedarrWebApplicationFactory()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "seedarr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var port = FindFreePort();
        BaseUrl = $"http://127.0.0.1:{port}";

        var startupContext = new StartupContext("--data=" + _tempDir);
        _app = Bootstrap.CreateApplication(startupContext, new[] { BaseUrl });
        _app.StartAsync().GetAwaiter().GetResult();

        Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        WaitForHealthy();
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
