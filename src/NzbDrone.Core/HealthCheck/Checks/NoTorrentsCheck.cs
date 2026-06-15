using System.Linq;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoTorrentsCheck : IHealthCheck
{
    private readonly ITorrentService _torrentService;

    public NoTorrentsCheck(ITorrentService torrentService)
    {
        _torrentService = torrentService;
    }

    public HealthCheckResult Check()
    {
        var torrents = _torrentService.GetAll();
        if (!torrents.Any())
        {
            return HealthCheckResult.Warning("NoTorrents", "No torrents have been added. Add torrents to begin seeding.");
        }

        return HealthCheckResult.Ok("NoTorrents");
    }
}
