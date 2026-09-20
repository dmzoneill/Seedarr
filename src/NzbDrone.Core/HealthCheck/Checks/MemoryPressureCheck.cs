using System;
using System.Diagnostics;

namespace NzbDrone.Core.HealthCheck.Checks;

/// <summary>
/// Health check that evaluates process working set against container or host memory limits.
/// </summary>
public class MemoryPressureCheck : IProvideHealthCheck
{
    private readonly Func<long> _getWorkingSetBytes;
    private readonly Func<long> _getAvailableMemoryBytes;

    public MemoryPressureCheck()
        : this(null, null)
    {
    }

    public MemoryPressureCheck(Func<long> getWorkingSetBytes, Func<long> getAvailableMemoryBytes)
    {
        _getWorkingSetBytes = getWorkingSetBytes ?? (() => Process.GetCurrentProcess().WorkingSet64);
        _getAvailableMemoryBytes = getAvailableMemoryBytes ?? (() => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
    }

    public HealthCheckResult Check()
    {
        var workingSet = _getWorkingSetBytes();
        var limit = _getAvailableMemoryBytes();

        if (limit <= 0)
        {
            return new HealthCheckResult(GetType(), HealthCheckResultType.Ok);
        }

        var usagePercentage = (double)workingSet / limit * 100.0;

        if (usagePercentage > 95.0)
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                $"Memory usage is critical at {usagePercentage:0.#}% ({FormatBytes(workingSet)} / {FormatBytes(limit)}). Container OOM kill risk is high.");
        }

        if (usagePercentage > 85.0)
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Warning,
                $"Memory usage is high at {usagePercentage:0.#}% ({FormatBytes(workingSet)} / {FormatBytes(limit)}). Consider increasing container or host memory limits.");
        }

        return new HealthCheckResult(GetType(), HealthCheckResultType.Ok);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        return bytes switch
        {
            >= 1024L * 1024L * 1024L * 1024L => $"{(double)bytes / (1024L * 1024L * 1024L * 1024L):F2} TB",
            >= 1024L * 1024L * 1024L => $"{(double)bytes / (1024L * 1024L * 1024L):F2} GB",
            >= 1024L * 1024L => $"{(double)bytes / (1024L * 1024L):F2} MB",
            >= 1024L => $"{(double)bytes / 1024L:F2} KB",
            _ => $"{bytes} B"
        };
    }
}
