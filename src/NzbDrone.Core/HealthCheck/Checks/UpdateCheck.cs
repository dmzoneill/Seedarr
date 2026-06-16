namespace NzbDrone.Core.HealthCheck.Checks;

public class UpdateCheck : IHealthCheck
{
    public HealthCheckResult Check()
    {
        // Placeholder — will check for updates when update mechanism is implemented
        return HealthCheckResult.Ok("UpdateCheck");
    }
}
