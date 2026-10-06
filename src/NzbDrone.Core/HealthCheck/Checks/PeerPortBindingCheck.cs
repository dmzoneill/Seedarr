using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.HealthCheck.Checks;

public class PeerPortBindingCheck : IProvideHealthCheck
{
    private readonly IPeerServer _peerServer;
    private readonly IConfigService _configService;

    public PeerPortBindingCheck(IPeerServer peerServer = null, IConfigService configService = null)
    {
        _peerServer = peerServer;
        _configService = configService;
    }

    public HealthCheckResult Check()
    {
        if (_peerServer == null)
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                "Peer server is not initialized or unavailable.");
        }

        var configuredPort = _configService?.ListeningPort ?? 0;

        if (_peerServer.BindFailed || !_peerServer.IsListening ||
            (_peerServer.ListeningPort > 0 && configuredPort > 0 && _peerServer.ListeningPort != configuredPort))
        {
            var port = _peerServer.ListeningPort > 0 ? _peerServer.ListeningPort : configuredPort;
            var portStr = port > 0 ? $"Port {port}" : "Peer listening port";
            var errorDetail = !string.IsNullOrWhiteSpace(_peerServer.BindErrorMessage)
                ? $" ({_peerServer.BindErrorMessage})"
                : string.Empty;

            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                $"{portStr} failed to bind or is already in use by another application{errorDetail}. Please choose another port in Network Settings.");
        }

        return new HealthCheckResult(GetType(), HealthCheckResultType.Ok);
    }
}
