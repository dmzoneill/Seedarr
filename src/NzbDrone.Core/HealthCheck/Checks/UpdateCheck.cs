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
        var info = _updateService.CheckForUpdate();

        if (info.UpdateAvailable)
        {
            return HealthCheckResult.Notice("UpdateCheck", $"Update available: v{info.LatestVersion}");
        }

        return HealthCheckResult.Ok("UpdateCheck");
    }
}
