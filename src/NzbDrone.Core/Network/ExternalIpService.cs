using System;
using System.Net.Http;
using System.Net.NetworkInformation;
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
    private static readonly TimeSpan FallbackInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

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
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private string _cachedIp = "";
    private DateTime _lastFetch = DateTime.MinValue;
    private volatile bool _networkChanged;

    public string CachedIp => _cachedIp;

    public ExternalIpService(HttpClient httpClient = null)
    {
        _client = httpClient ?? SharedClient;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await FetchExternalIpAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                if (_networkChanged)
                {
                    _networkChanged = false;
                    _logger.Info("Network change detected, refreshing external IP");
                    await RefreshIp(stoppingToken);
                }
                else if (DateTime.UtcNow - _lastFetch > FallbackInterval)
                {
                    await RefreshIp(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        }
    }

    private void OnNetworkChanged(object sender, EventArgs e)
    {
        _networkChanged = true;
    }

    private async Task RefreshIp(CancellationToken cancellationToken)
    {
        try
        {
            var oldIp = _cachedIp;
            var newIp = await FetchExternalIpAsync(cancellationToken);

            if (!string.IsNullOrEmpty(newIp) && newIp != oldIp)
            {
                _logger.Info("External IP changed: {0} -> {1}", oldIp, newIp);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "External IP refresh failed");
        }
    }

    public async Task<string> GetExternalIpAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_cachedIp) && DateTime.UtcNow - _lastFetch < CacheDuration)
        {
            return _cachedIp;
        }

        return await FetchExternalIpAsync(cancellationToken);
    }

    private async Task<string> FetchExternalIpAsync(CancellationToken cancellationToken)
    {
        if (!await _fetchLock.WaitAsync(0, cancellationToken))
        {
            return _cachedIp;
        }

        try
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
        finally
        {
            _fetchLock.Release();
        }
    }
}
