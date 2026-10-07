using System.IO;
using System.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Configuration;

public interface ILoggingReconfigurationService
{
    void Initialize();
    void ReconfigureLogging();
}

public class LoggingReconfigurationService : ILoggingReconfigurationService, IHandle<ConfigSavedEvent>, IHandle<ApplicationStartedEvent>
{
    private const string FileTargetName = NzbDroneLogger.FileTargetName;
    private const string RingBufferTargetName = "ringBuffer";

    private readonly IConfigService _configService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;

    public LoggingReconfigurationService(IConfigService configService, IAppFolderInfo appFolderInfo)
    {
        _configService = configService;
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Initialize()
    {
        ReconfigureLogging();
    }

    public void Handle(ConfigSavedEvent message)
    {
        ReconfigureLogging();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        ReconfigureLogging();
    }

    public void ReconfigureLogging()
    {
        var config = LogManager.Configuration;
        if (config == null)
        {
            return;
        }

        EnsureProvisionalFileLogging(config);

        var logToFile = _configService.LogToFile;
        var fileLogLevel = _configService.FileLogLevel;
        var debugMode = _configService.DebugMode;

        ConfigureFileLogging(config, logToFile, fileLogLevel, debugMode);
        ConfigureRingBuffer(config, fileLogLevel, debugMode);
        ConfigureDebugMode(config, debugMode);

        LogManager.ReconfigExistingLoggers();
        _logger.Debug("Logging reconfigured: logToFile={0}, fileLogLevel={1}, debugMode={2}", logToFile, fileLogLevel, debugMode);
    }

    private void EnsureProvisionalFileLogging(LoggingConfiguration config)
    {
        if (config.FindTargetByName<FileTarget>(FileTargetName) != null)
        {
            return;
        }

        ConfigureFileLogging(config, logToFile: true, fileLogLevel: "Info", debugMode: false);
        LogManager.ReconfigExistingLoggers();
    }

    private void ConfigureFileLogging(LoggingConfiguration config, bool logToFile, string fileLogLevel, bool debugMode)
    {
        var existingTarget = config.FindTargetByName<FileTarget>(FileTargetName);

        if (logToFile)
        {
            var logFilePath = Path.Combine(_appFolderInfo.AppDataFolder, "logs", "seedarr.txt");
            var level = GetEffectiveMinLevel(fileLogLevel, debugMode);

            if (existingTarget == null)
            {
                var fileTarget = NzbDroneLogger.CreateFileTarget(logFilePath);

                config.AddTarget(fileTarget);
                config.AddRule(level, LogLevel.Fatal, fileTarget);
            }
            else
            {
                // Update the existing file target's rules
                RemoveRulesForTarget(config, FileTargetName);
                config.AddRule(level, LogLevel.Fatal, existingTarget);
            }
        }
        else
        {
            if (existingTarget != null)
            {
                RemoveRulesForTarget(config, FileTargetName);
                config.RemoveTarget(FileTargetName);
            }
        }
    }

    private static void ConfigureRingBuffer(LoggingConfiguration config, string fileLogLevel, bool debugMode)
    {
        var ringBufferTarget = config.FindTargetByName(RingBufferTargetName);
        if (ringBufferTarget == null)
        {
            return;
        }

        var minLevel = GetEffectiveMinLevel(fileLogLevel, debugMode);

        RemoveRulesForTarget(config, RingBufferTargetName);
        config.AddRule(minLevel, LogLevel.Fatal, ringBufferTarget);
    }

    private static void ConfigureDebugMode(LoggingConfiguration config, bool debugMode)
    {
        var consoleTarget = config.FindTargetByName("console");
        if (consoleTarget == null)
        {
            return;
        }

        var minLevel = debugMode ? LogLevel.Debug : LogLevel.Info;

        RemoveRulesForTarget(config, "console");
        config.AddRule(minLevel, LogLevel.Fatal, consoleTarget);
    }

    private static void RemoveRulesForTarget(LoggingConfiguration config, string targetName)
    {
        for (var i = config.LoggingRules.Count - 1; i >= 0; i--)
        {
            var rule = config.LoggingRules[i];
            if (rule.Targets.Count > 0 && rule.Targets.Any(t => t.Name == targetName))
            {
                config.LoggingRules.RemoveAt(i);
            }
        }
    }

    private static LogLevel GetEffectiveMinLevel(string fileLogLevel, bool debugMode)
    {
        var minLevel = ParseLogLevel(fileLogLevel);
        if (debugMode && minLevel.Ordinal > LogLevel.Debug.Ordinal)
        {
            minLevel = LogLevel.Debug;
        }

        return minLevel;
    }

    private static LogLevel ParseLogLevel(string level)
    {
        return level?.ToLower() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info" => LogLevel.Info,
            "warn" => LogLevel.Warn,
            "error" => LogLevel.Error,
            "fatal" => LogLevel.Fatal,
            _ => LogLevel.Info
        };
    }
}
