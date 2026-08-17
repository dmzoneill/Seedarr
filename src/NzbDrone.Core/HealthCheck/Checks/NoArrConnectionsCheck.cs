using System.Linq;
using NzbDrone.Core.ArrIntegration;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoArrConnectionsCheck : IHealthCheck
{
    private readonly IArrConnectionFactory _connectionFactory;

    public NoArrConnectionsCheck(IArrConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public HealthCheckResult Check()
    {
        var connections = _connectionFactory.All();
        if (!connections.Any(c => c.Enable))
        {
            return HealthCheckResult.Warning(
                "NoArrConnections",
                "No *arr connections configured. Connect Sonarr, Radarr, or Lidarr in Settings > Connections to automatically import grabbed torrents.");
        }

        return HealthCheckResult.Ok("NoArrConnections");
    }
}
