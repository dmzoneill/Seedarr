using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.HealthCheck.Checks;

public class PeerListeningPortHealthCheck : IProvideHealthCheck
{
    private readonly IPeerServer _peerServer;
    private readonly IConfigService _configService;

    public PeerListeningPortHealthCheck(IPeerServer peerServer, IConfigService configService)
    {
        _peerServer = peerServer;
        _configService = configService;
    }

    public HealthCheckResult Check()
    {
        var configuredPort = _configService?.ListeningPort ?? 0;

        if (_peerServer == null || _peerServer.BindFailed || !_peerServer.IsListening ||
            (_peerServer.ListeningPort > 0 && configuredPort > 0 && _peerServer.ListeningPort != configuredPort))
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResult.HealthType.Error,
                $"Peer server failed to listen on port {configuredPort}: Port is already in use by another process or address could not be bound.");
        }

        return new HealthCheckResult(GetType(), HealthCheckResult.HealthType.Ok);
    }
}
