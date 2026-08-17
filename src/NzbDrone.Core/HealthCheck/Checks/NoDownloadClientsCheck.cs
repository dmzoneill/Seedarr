using System.Linq;
using NzbDrone.Core.DownloadClients;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoDownloadClientsCheck : IHealthCheck
{
    private readonly IDownloadClientFactory _downloadClientFactory;

    public NoDownloadClientsCheck(IDownloadClientFactory downloadClientFactory)
    {
        _downloadClientFactory = downloadClientFactory;
    }

    public HealthCheckResult Check()
    {
        var clients = _downloadClientFactory.All();
        if (!clients.Any(c => c.Enable))
        {
            return HealthCheckResult.Warning(
                "NoDownloadClients",
                "No download clients configured. Add a download client (Deluge, qBittorrent, Transmission) in Settings > Download Clients.");
        }

        return HealthCheckResult.Ok("NoDownloadClients");
    }
}
