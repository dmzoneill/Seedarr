using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Network;

public class ProxyTestRequest
{
    public string ProxyType { get; set; }
    public string ProxyHost { get; set; }
    public int ProxyPort { get; set; }
    public bool ProxyAuthEnabled { get; set; }
    public string ProxyUsername { get; set; }
    public string ProxyPassword { get; set; }
    public string TestTargetHost { get; set; } = "example.com";
    public int TestTargetPort { get; set; } = 80;
    public int TimeoutMs { get; set; } = 5000;
}

public class ProxyTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public bool RemoteDnsVerified { get; set; }
}

public interface IProxyTestService
{
    Task<ProxyTestResult> TestProxyAsync(ProxyTestRequest request, CancellationToken cancellationToken = default);
}

public class ProxyTestService : IProxyTestService
{
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public ProxyTestService(IConfigService configService = null)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<ProxyTestResult> TestProxyAsync(ProxyTestRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return new ProxyTestResult { Success = false, Message = "Request cannot be empty." };
        }

        var host = request.ProxyHost?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            return new ProxyTestResult { Success = false, Message = "Proxy hostname or IP address must be specified." };
        }

        if (request.ProxyPort < 1 || request.ProxyPort > 65535)
        {
            return new ProxyTestResult { Success = false, Message = "Proxy port must be between 1 and 65535." };
        }

        var rawType = request.ProxyType?.Trim() ?? string.Empty;
        if (!Enum.TryParse<ProxyType>(rawType, ignoreCase: true, out var proxyType) || proxyType == ProxyType.None)
        {
            return new ProxyTestResult { Success = false, Message = "A valid proxy protocol (SOCKS5, SOCKS5h, or HTTP) must be selected." };
        }

        var username = request.ProxyAuthEnabled ? (request.ProxyUsername?.Trim() ?? string.Empty) : string.Empty;
        var password = request.ProxyPassword;
        if (request.ProxyAuthEnabled && (password == null || password == "(unchanged)" || password == "********"))
        {
            password = _configService?.ProxyPassword ?? string.Empty;
        }

        var targetHost = string.IsNullOrWhiteSpace(request.TestTargetHost) ? "example.com" : request.TestTargetHost.Trim();
        var targetPort = request.TestTargetPort <= 0 ? 80 : request.TestTargetPort;
        var timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : 5000;

        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            await client.ConnectAsync(host, request.ProxyPort, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new ProxyTestResult
            {
                Success = false,
                Message = $"Connection to proxy at {host}:{request.ProxyPort} timed out after {timeoutMs}ms."
            };
        }
        catch (Exception ex)
        {
            return new ProxyTestResult
            {
                Success = false,
                Message = $"Failed to connect to proxy at {host}:{request.ProxyPort}: {ex.Message}"
            };
        }

        try
        {
            using var stream = client.GetStream();
            stream.ReadTimeout = timeoutMs;
            stream.WriteTimeout = timeoutMs;

            bool isHostname = !IPAddress.TryParse(targetHost, out _);

            if (proxyType == ProxyType.Socks5 || proxyType == ProxyType.Socks5h)
            {
                PeerConnection.PerformSocks5Handshake(stream, targetHost, targetPort, username, password);
            }
            else if (proxyType == ProxyType.Http)
            {
                PeerConnection.PerformHttpConnectHandshake(stream, targetHost, targetPort, username, password);
            }

            return new ProxyTestResult
            {
                Success = true,
                Message = isHostname
                    ? $"Proxy connection, authentication, and remote DNS resolution verified successfully through {host}:{request.ProxyPort}."
                    : $"Proxy connection and authentication verified successfully through {host}:{request.ProxyPort}.",
                RemoteDnsVerified = isHostname
            };
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Proxy test handshake failed for {0}:{1}", host, request.ProxyPort);
            return new ProxyTestResult
            {
                Success = false,
                Message = $"Proxy test failed: {ex.Message}"
            };
        }
    }
}
