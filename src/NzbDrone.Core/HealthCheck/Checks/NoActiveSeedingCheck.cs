using System.Linq;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoActiveSeedingCheck : IHealthCheck
{
    private readonly ITorrentService _torrentService;

    public NoActiveSeedingCheck(ITorrentService torrentService)
    {
        _torrentService = torrentService;
    }

    public HealthCheckResult Check()
    {
        var torrents = _torrentService.GetAll();
        if (torrents.Any() && !torrents.Any(t => t.Status == TorrentStatus.Seeding))
        {
            return HealthCheckResult.Notice("NoActiveSeeding", "No torrents are currently seeding.");
        }

        return HealthCheckResult.Ok("NoActiveSeeding");
    }
}
