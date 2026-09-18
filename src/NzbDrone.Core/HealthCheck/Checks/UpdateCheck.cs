using System.Threading.Tasks;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.HealthCheck.Checks;

public class UpdateCheck : IHealthCheck
{
    private readonly IUpdateService _updateService;

    public UpdateCheck(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    public HealthCheckResult Check()
    {
        var info = _updateService.CachedUpdateInfo;
        if (info == null)
        {
            _ = Task.Run(() => _updateService.CheckForUpdateAsync());
            return HealthCheckResult.Ok("UpdateCheck");
        }

        if (_updateService.IsCacheExpired)
        {
            _ = Task.Run(() => _updateService.CheckForUpdateAsync());
        }

        if (info.UpdateAvailable)
        {
            return HealthCheckResult.Notice("UpdateCheck", $"Update available: v{info.LatestVersion}");
        }

        return HealthCheckResult.Ok("UpdateCheck");
    }
}
