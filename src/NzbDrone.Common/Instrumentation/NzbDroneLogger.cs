using System;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Instrumentation;

public static class NzbDroneLogger
{
    public const string FileTargetName = "file";

    public static FileTarget CreateFileTarget(string logFilePath)
    {
        return new FileTarget(FileTargetName)
        {
            FileName = logFilePath,
            ArchiveFileName = logFilePath,
            ArchiveSuffixFormat = "_{1:yyyyMMdd}_{0}",
            MaxArchiveFiles = 5,
            ArchiveAboveSize = 1_048_576,
            Layout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.f}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=toString}}"
        };
    }

    public static void Register(StartupContext startupContext = null)
    {
        var config = new LoggingConfiguration();

        var consoleTarget = new ColoredConsoleTarget("console")
        {
            Layout = "${date:format=yyyy-MM-dd HH\\:mm\\:ss.f}|${level:uppercase=true}|${logger}|${message}${onexception:inner=${newline}${exception:format=toString}}"
        };

        config.AddTarget(consoleTarget);
        config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget);

        var ringBufferTarget = new RingBufferTarget(2048) { Name = "ringBufferTarget" };
        RingBufferTarget.Instance = ringBufferTarget;

        var asyncWrapper = new AsyncTargetWrapper(ringBufferTarget, 5000, AsyncTargetWrapperOverflowAction.Discard)
        {
            Name = "ringBuffer"
        };
        config.AddTarget(asyncWrapper);
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, asyncWrapper);

        try
        {
            startupContext ??= new StartupContext();
            var appFolderInfo = new AppFolderInfo(startupContext);
            var logPath = Path.Combine(appFolderInfo.AppDataFolder, "logs");
            Directory.CreateDirectory(logPath);
            var logFilePath = Path.Combine(logPath, "seedarr.txt");

            var fileTarget = CreateFileTarget(logFilePath);
            config.AddTarget(fileTarget);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"Failed to initialize file logger target: {ex.Message}");
        }

        LogManager.Configuration = config;
    }
}
