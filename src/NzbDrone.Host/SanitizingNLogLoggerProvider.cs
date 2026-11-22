using System;
using Microsoft.Extensions.Logging;
using NLog;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;
using NLogLevel = NLog.LogLevel;

namespace NzbDrone.Host;

public static class HostingLog
{
    public static void Configure(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddProvider(new SanitizingNLogLoggerProvider());
    }
}

public sealed class SanitizingNLogLoggerProvider : ILoggerProvider
{
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new SanitizingNLogLogger(LogManager.GetLogger(categoryName));
    }

    public void Dispose()
    {
    }
}

internal sealed class SanitizingNLogLogger : Microsoft.Extensions.Logging.ILogger
{
    private readonly Logger _logger;

    public SanitizingNLogLogger(Logger logger)
    {
        _logger = logger;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(MelLogLevel logLevel)
    {
        return logLevel != MelLogLevel.None && _logger.IsEnabled(Map(logLevel));
    }

    public void Log<TState>(MelLogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (!IsEnabled(logLevel) || formatter == null)
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
        {
            return;
        }

        var info = new LogEventInfo
        {
            Level = Map(logLevel),
            LoggerName = _logger.Name,
            Message = message ?? string.Empty,
            Exception = exception
        };
        _logger.Log(typeof(SanitizingNLogLogger), info);
    }

    private static NLogLevel Map(MelLogLevel level)
    {
        return level switch
        {
            MelLogLevel.Trace => NLogLevel.Trace,
            MelLogLevel.Debug => NLogLevel.Debug,
            MelLogLevel.Information => NLogLevel.Info,
            MelLogLevel.Warning => NLogLevel.Warn,
            MelLogLevel.Error => NLogLevel.Error,
            MelLogLevel.Critical => NLogLevel.Fatal,
            _ => NLogLevel.Off
        };
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
