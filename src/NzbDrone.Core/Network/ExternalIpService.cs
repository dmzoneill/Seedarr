using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Network;

public interface IExternalIpService
{
    string CachedIp { get; }
    Task<string> GetExternalIpAsync(CancellationToken cancellationToken = default);
}

public class ExternalIpService : BackgroundService, IExternalIpService
{
    private const string PrimaryEndpointTemplate = "https://www.seedarr.net/ip/?uuid={0}";

    private static readonly TimeSpan FallbackInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private static readonly string[] FallbackSources =
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
    private readonly IConfigService _configService;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private string _cachedIp = "";
    private DateTime _lastFetch = DateTime.MinValue;
    private volatile bool _networkChanged;

    public string CachedIp => _cachedIp;

    public ExternalIpService(IConfigService configService, HttpClient httpClient = null)
    {
        _configService = configService;
        _client = httpClient ?? SharedClient;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public ExternalIpService(HttpClient httpClient)
        : this(null, httpClient)
    {
    }

    public ExternalIpService()
        : this(null, null)
    {
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
        if (!await _fetchLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
        {
            return _cachedIp;
        }

        try
        {
            if (!string.IsNullOrEmpty(_cachedIp) && DateTime.UtcNow - _lastFetch < CacheDuration)
            {
                return _cachedIp;
            }

            var uuid = _configService?.InstanceUuid;
            if (string.IsNullOrWhiteSpace(uuid))
            {
                uuid = Guid.NewGuid().ToString().ToLowerInvariant();
            }

            var sources = new List<string>
            {
                string.Format(PrimaryEndpointTemplate, Uri.EscapeDataString(uuid))
            };
            sources.AddRange(FallbackSources);

            foreach (var source in sources)
            {
                try
                {
                    var response = await _client.GetStringAsync(source, cancellationToken);

                    if (TryExtractIpFromResponse(response, out var ip))
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

    public static bool IsPublicRoutableIpAddress(IPAddress address)
    {
        if (address == null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublicRoutableIpAddress(address.MapToIPv4());
        }

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // 0.0.0.0/8 - Unspecified / current network
            if (bytes[0] == 0)
            {
                return false;
            }

            // 10.0.0.0/8 - RFC 1918 Private
            if (bytes[0] == 10)
            {
                return false;
            }

            // 100.64.0.0/10 - RFC 6598 Shared Address Space (CGNAT)
            if (bytes[0] == 100 && (bytes[1] & 0xC0) == 64)
            {
                return false;
            }

            // 127.0.0.0/8 - Loopback
            if (bytes[0] == 127)
            {
                return false;
            }

            // 169.254.0.0/16 - RFC 3927 Link-Local
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return false;
            }

            // 172.16.0.0/12 - RFC 1918 Private
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return false;
            }

            // 192.0.2.0/24 - RFC 5737 TEST-NET-1
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
            {
                return false;
            }

            // 192.168.0.0/16 - RFC 1918 Private
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return false;
            }

            // 198.51.100.0/24 - RFC 5737 TEST-NET-2
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            {
                return false;
            }

            // 203.0.113.0/24 - RFC 5737 TEST-NET-3
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
            {
                return false;
            }

            // 224.0.0.0/4 - Multicast & 240.0.0.0/4 - Reserved / Broadcast
            if (bytes[0] >= 224)
            {
                return false;
            }

            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // :: - Unspecified
            if (address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any))
            {
                return false;
            }

            // Link-Local (fe80::/10)
            if (address.IsIPv6LinkLocal || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80))
            {
                return false;
            }

            // Multicast (ff00::/8)
            if (address.IsIPv6Multicast || bytes[0] == 0xFF)
            {
                return false;
            }

            // Site-Local (fec0::/10)
            if (address.IsIPv6SiteLocal || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0xC0))
            {
                return false;
            }

            // Unique Local Address (fc00::/7) - RFC 4193
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return false;
            }

            // Documentation (2001:db8::/32) - RFC 3849
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool TryValidateCandidate(string candidate, out string ip)
    {
        ip = string.Empty;
        if (!string.IsNullOrEmpty(candidate) && IPAddress.TryParse(candidate, out var parsed) && IsPublicRoutableIpAddress(parsed))
        {
            ip = parsed.ToString();
            return true;
        }

        return false;
    }

    public static bool TryExtractIpFromResponse(string responseText, out string ip)
    {
        ip = string.Empty;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        var trimmed = responseText.Trim();

        // 1. Try parsing JSON format (e.g. from seedarr.net/ip/?uuid=...)
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            // Check for { "data": { "ip": "..." } } or { "data": { "ip_address": "..." } }
            if (root.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Object)
            {
                if (dataElem.TryGetProperty("ip", out var ipElem) && ipElem.ValueKind == JsonValueKind.String)
                {
                    if (TryValidateCandidate(ipElem.GetString()?.Trim(), out ip))
                    {
                        return true;
                    }
                }

                if (dataElem.TryGetProperty("ip_address", out var ipAddressElem) && ipAddressElem.ValueKind == JsonValueKind.String)
                {
                    if (TryValidateCandidate(ipAddressElem.GetString()?.Trim(), out ip))
                    {
                        return true;
                    }
                }
            }

            // Check for root { "ip": "..." }
            if (root.TryGetProperty("ip", out var simpleIpElem) && simpleIpElem.ValueKind == JsonValueKind.String)
            {
                if (TryValidateCandidate(simpleIpElem.GetString()?.Trim(), out ip))
                {
                    return true;
                }
            }

            // Check for root { "ip_address": "..." }
            if (root.TryGetProperty("ip_address", out var rootIpElem) && rootIpElem.ValueKind == JsonValueKind.String)
            {
                if (TryValidateCandidate(rootIpElem.GetString()?.Trim(), out ip))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Not JSON, continue to plain IP parsing
        }

        // 2. Try parsing plain text IP
        if (TryValidateCandidate(trimmed, out ip))
        {
            return true;
        }

        return false;
    }
}
