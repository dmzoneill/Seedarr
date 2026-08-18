using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Network;

public interface IExternalIpService
{
    string CachedIp { get; }
    Task<string> GetExternalIpAsync(CancellationToken cancellationToken = default);
}

public class ExternalIpService : BackgroundService, IExternalIpService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

    private static readonly string[] Sources =
    {
        "https://api.ipify.org",
        "https://ifconfig.me/ip",
        "https://icanhazip.com",
        "https://checkip.amazonaws.com"
    };

    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    })
    { Timeout = TimeSpan.FromSeconds(5) };

    private readonly HttpClient _client;
    private readonly Logger _logger;
    private string _cachedIp = "";
    private DateTime _lastFetch = DateTime.MinValue;

    public string CachedIp => _cachedIp;

    public ExternalIpService(HttpClient httpClient = null)
    {
        _client = httpClient ?? SharedClient;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ip = await FetchExternalIpAsync(stoppingToken);
                if (!string.IsNullOrEmpty(ip) && ip != _cachedIp)
                {
                    _logger.Info("External IP updated: {0}", ip);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Periodic external IP refresh failed");
            }

            await Task.Delay(RefreshInterval, stoppingToken);
        }
    }

    public async Task<string> GetExternalIpAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_cachedIp) && DateTime.UtcNow - _lastFetch < RefreshInterval)
        {
            return _cachedIp;
        }

        return await FetchExternalIpAsync(cancellationToken);
    }

    private async Task<string> FetchExternalIpAsync(CancellationToken cancellationToken)
    {
        foreach (var source in Sources)
        {
            try
            {
                var ip = (await _client.GetStringAsync(source, cancellationToken)).Trim();

                if (System.Net.IPAddress.TryParse(ip, out _))
                {
                    _cachedIp = ip;
                    _lastFetch = DateTime.UtcNow;
                    _logger.Debug("External IP from {0}: {1}", source, ip);
                    return ip;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to get external IP from {0}", source);
            }
        }

        return _cachedIp;
    }
}
