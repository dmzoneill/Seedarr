using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Configuration;

public class LoggingReconfigurationService : IHandle<ConfigSavedEvent>
{
    private const string FileTargetName = "file";

    private readonly IConfigService _configService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;

    public LoggingReconfigurationService(IConfigService configService, IAppFolderInfo appFolderInfo)
    {
        _configService = configService;
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(ConfigSavedEvent message)
    {
        ReconfigureLogging();
    }

    private void ReconfigureLogging()
    {
        var config = LogManager.Configuration;
        if (config == null)
        {
            return;
        }

        var logToFile = _configService.LogToFile;
        var fileLogLevel = _configService.FileLogLevel;
        var debugMode = _configService.DebugMode;

        ConfigureFileLogging(config, logToFile, fileLogLevel);
        ConfigureDebugMode(config, debugMode);

        LogManager.ReconfigExistingLoggers();
        _logger.Debug("Logging reconfigured: logToFile={0}, fileLogLevel={1}, debugMode={2}", logToFile, fileLogLevel, debugMode);
    }

    private void ConfigureFileLogging(LoggingConfiguration config, bool logToFile, string fileLogLevel)
    {
        var existingTarget = config.FindTargetByName<FileTarget>(FileTargetName);

        if (logToFile)
        {
            var logFilePath = Path.Combine(_appFolderInfo.AppDataFolder, "logs", "seedarr.txt");
            var level = ParseLogLevel(fileLogLevel);

            if (existingTarget == null)
            {
                var fileTarget = new FileTarget(FileTargetName)
                {
                    FileName = logFilePath,
                    ArchiveFileName = Path.Combine(_appFolderInfo.AppDataFolder, "logs", "seedarr.{#}.txt"),
                    ArchiveNumbering = ArchiveNumberingMode.Rolling,
                    MaxArchiveFiles = 5,
                    ArchiveAboveSize = 1_048_576,
                    Layout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.f}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=toString}}"
                };

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
            if (rule.Targets.Count > 0 && rule.Targets[0].Name == targetName)
            {
                config.LoggingRules.RemoveAt(i);
            }
        }
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
