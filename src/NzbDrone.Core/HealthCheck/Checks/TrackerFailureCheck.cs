using System.Linq;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.HealthCheck.Checks;

public class TrackerFailureCheck : IHealthCheck
{
    private readonly ITorrentService _torrentService;

    public TrackerFailureCheck(ITorrentService torrentService)
    {
        _torrentService = torrentService;
    }

    public HealthCheckResult Check()
    {
        var errored = _torrentService.GetAll().Where(t => t.Status == TorrentStatus.Error).ToList();
        if (errored.Count > 0)
        {
            return HealthCheckResult.Warning("TrackerFailure",
                $"{errored.Count} torrent(s) have errors. Check tracker connectivity.");
        }

        return HealthCheckResult.Ok("TrackerFailure");
    }
}
