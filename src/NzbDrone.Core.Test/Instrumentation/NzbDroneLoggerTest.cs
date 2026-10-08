using System;
using System.IO;
using System.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Core.Test.Instrumentation;

[TestFixture]
public class NzbDroneLoggerTest
{
    private LoggingConfiguration _savedConfig;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _savedConfig = LogManager.Configuration;
        _tempDir = Path.Combine(Path.GetTempPath(), "nzbdrone-logger-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        LogManager.Configuration = _savedConfig;
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best-effort test cleanup
            }
        }
    }

    [Test]
    public void Register_should_initialize_FileTarget_RingBufferTarget_and_ColoredConsoleTarget()
    {
        var startupContext = new StartupContext("--data=" + _tempDir);
        NzbDroneLogger.Register(startupContext);

        Assert.That(LogManager.Configuration, Is.Not.Null);

        var consoleTarget = LogManager.Configuration.FindTargetByName("console");
        Assert.That(consoleTarget, Is.Not.Null);

        var ringBufferTarget = LogManager.Configuration.FindTargetByName("ringBuffer");
        Assert.That(ringBufferTarget, Is.Not.Null);

        var fileTarget = LogManager.Configuration.FindTargetByName<FileTarget>(NzbDroneLogger.FileTargetName);
        Assert.That(fileTarget, Is.Not.Null);
    }

    [Test]
    public void Register_should_route_Info_level_to_FileTarget()
    {
        var startupContext = new StartupContext("--data=" + _tempDir);
        NzbDroneLogger.Register(startupContext);

        var fileTarget = LogManager.Configuration.FindTargetByName<FileTarget>(NzbDroneLogger.FileTargetName);
        Assert.That(fileTarget, Is.Not.Null);

        var matchingRules = LogManager.Configuration.LoggingRules
            .Where(r => r.Targets.Contains(fileTarget))
            .ToList();

        Assert.That(matchingRules, Is.Not.Empty);
        Assert.That(matchingRules[0].Levels, Does.Contain(LogLevel.Info));
        Assert.That(matchingRules[0].Levels, Does.Contain(LogLevel.Fatal));
        Assert.That(matchingRules[0].Levels, Does.Not.Contain(LogLevel.Debug));
    }

    [Test]
    public void Register_with_null_startup_context_should_not_throw()
    {
        Assert.DoesNotThrow(() => NzbDroneLogger.Register(null));
        Assert.That(LogManager.Configuration.FindTargetByName<FileTarget>(NzbDroneLogger.FileTargetName), Is.Not.Null);
    }

    [Test]
    public void Register_should_create_log_file_and_write_startup_logs()
    {
        var startupContext = new StartupContext("--data=" + _tempDir);
        NzbDroneLogger.Register(startupContext);

        var logger = LogManager.GetCurrentClassLogger();
        var testMessage = "Startup test log message " + Guid.NewGuid().ToString("N");
        logger.Info(testMessage);

        LogManager.Flush();

        var logFilePath = Path.Combine(_tempDir, "logs", "seedarr.txt");
        Assert.That(File.Exists(logFilePath), Is.True, "seedarr.txt should exist on disk after logging on startup");

        var fileContent = File.ReadAllText(logFilePath);
        Assert.That(fileContent, Does.Contain(testMessage));
    }

    [Test]
    public void CreateFileTarget_should_configure_target_correctly()
    {
        var dummyPath = "/tmp/test/logs/seedarr.txt";
        var target = NzbDroneLogger.CreateFileTarget(dummyPath);

        Assert.That(target.Name, Is.EqualTo(NzbDroneLogger.FileTargetName));
        Assert.That(target.FileName.ToString(), Does.Contain("seedarr.txt"));
        if (target.ArchiveFileName != null)
        {
            Assert.That(target.ArchiveFileName.ToString(), Does.Contain("seedarr.txt"));
        }
        Assert.That(target.ArchiveSuffixFormat, Is.EqualTo("_{1:yyyyMMdd}_{0}"));
        Assert.That(target.MaxArchiveFiles, Is.EqualTo(5));
        Assert.That(target.ArchiveAboveSize, Is.EqualTo(1_048_576));
    }

    [Test]
    public void Register_should_redact_sensitive_data_in_file_log()
    {
        var startupContext = new StartupContext("--data=" + _tempDir);
        NzbDroneLogger.Register(startupContext);

        var logger = LogManager.GetCurrentClassLogger();
        logger.Info("Failed request Authorization: Bearer eyJhbGciOiJIUzI1NiJ9 payload {\"apiKey\": \"super-secret\"}");

        LogManager.Flush();

        var logFilePath = Path.Combine(_tempDir, "logs", "seedarr.txt");
        var fileContent = File.ReadAllText(logFilePath);
        Assert.That(fileContent, Does.Not.Contain("super-secret"));
        Assert.That(fileContent, Does.Not.Contain("eyJhbGciOiJIUzI1NiJ9"));
        Assert.That(fileContent, Does.Contain("Bearer [REDACTED]"));
        Assert.That(fileContent, Does.Contain("[REDACTED]"));
    }

    [Test]
    public void CreateFileTarget_should_use_sanitized_layout()
    {
        var target = NzbDroneLogger.CreateFileTarget("/tmp/test/logs/seedarr.txt");

        Assert.That(target.Layout.ToString(), Does.Contain("sanitized-message"));
        Assert.That(target.Layout.ToString(), Does.Contain("sanitized-exception"));
        Assert.That(target.Layout.ToString(), Does.Not.Contain("${message}"));
    }
}
