using NzbDrone.Core.Peers;

namespace NzbDrone.Core.HealthCheck.Checks;

public class PeerPortBindingCheck : IProvideHealthCheck
{
    private readonly IPeerServer _peerServer;

    public PeerPortBindingCheck(IPeerServer peerServer = null)
    {
        _peerServer = peerServer;
    }

    public HealthCheckResult Check()
    {
        if (_peerServer != null && (_peerServer.BindFailed || !_peerServer.IsListening))
        {
            var port = _peerServer.ListeningPort;
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
