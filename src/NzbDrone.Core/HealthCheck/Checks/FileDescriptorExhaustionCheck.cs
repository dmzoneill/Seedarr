using NzbDrone.Core.Instrumentation;

namespace NzbDrone.Core.HealthCheck.Checks;

/// <summary>
/// Health check that warns or errors when file descriptor usage nears the operating system soft limit.
/// </summary>
public class FileDescriptorExhaustionCheck : IHealthCheck
{
    private readonly IFileDescriptorProvider _fileDescriptorProvider;

    public FileDescriptorExhaustionCheck(IFileDescriptorProvider fileDescriptorProvider = null)
    {
        _fileDescriptorProvider = fileDescriptorProvider ?? new FileDescriptorProvider();
    }

    public HealthCheckResult Check()
    {
        var usage = _fileDescriptorProvider.GetFileDescriptorUsagePercentage();
        if (!usage.HasValue)
        {
            return new HealthCheckResult(GetType(), HealthCheckResultType.Ok);
        }

        var count = _fileDescriptorProvider.GetOpenFileDescriptorCount();
        var max = _fileDescriptorProvider.GetMaxFileDescriptors();

        if (usage.Value >= 90.0)
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                $"File descriptor usage is at {usage.Value:0.#}% ({count}/{max}). Risk of socket and file I/O failure (EMFILE). Increase nofile ulimit (e.g. 'ulimits: nofile: 65536' in podman-compose / compose.yaml or /etc/security/limits.conf).");
        }

        if (usage.Value >= 80.0)
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Warning,
                $"File descriptor usage is at {usage.Value:0.#}% ({count}/{max}). Consider increasing nofile ulimit before descriptor exhaustion occurs.");
        }

        return new HealthCheckResult(GetType(), HealthCheckResultType.Ok);
    }
}
