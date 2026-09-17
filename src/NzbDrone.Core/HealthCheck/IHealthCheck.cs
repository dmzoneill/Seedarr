using System;

namespace NzbDrone.Core.HealthCheck;

public interface IHealthCheck
{
    HealthCheckResult Check();
}

public interface IProvideHealthCheck : IHealthCheck
{
}

public enum HealthCheckResultType
{
    Ok,
    Notice,
    Warning,
    Error
}

public class HealthCheckResult
{
    public enum HealthType
    {
        Ok = HealthCheckResultType.Ok,
        Notice = HealthCheckResultType.Notice,
        Warning = HealthCheckResultType.Warning,
        Error = HealthCheckResultType.Error
    }

    public HealthCheckResultType Type { get; set; }

    public string Source { get; set; }

    public string Message { get; set; }

    public HealthCheckResult()
    {
    }

    public HealthCheckResult(Type source)
        : this(source, HealthType.Ok, null)
    {
    }

    public HealthCheckResult(Type source, HealthType type, string message = null)
    {
        Source = source?.Name;
        Type = (HealthCheckResultType)type;
        Message = message;
    }

    public HealthCheckResult(Type source, HealthCheckResultType type, string message = null)
    {
        Source = source?.Name;
        Type = type;
        Message = message;
    }

    public static HealthCheckResult Ok(string source)
    {
        return new HealthCheckResult { Type = HealthCheckResultType.Ok, Source = source };
    }

    public static HealthCheckResult Notice(string source, string message)
    {
        return new HealthCheckResult { Type = HealthCheckResultType.Notice, Source = source, Message = message };
    }

    public static HealthCheckResult Warning(string source, string message)
    {
        return new HealthCheckResult { Type = HealthCheckResultType.Warning, Source = source, Message = message };
    }

    public static HealthCheckResult Error(string source, string message)
    {
        return new HealthCheckResult { Type = HealthCheckResultType.Error, Source = source, Message = message };
    }
}
