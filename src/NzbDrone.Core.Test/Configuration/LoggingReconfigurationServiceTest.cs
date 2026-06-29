using NLog;
using NLog.Config;
using NLog.Targets;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Test.Configuration
{
    [TestFixture]
    public class LoggingReconfigurationServiceTest
    {
        private IConfigService _configService;
        private IAppFolderInfo _appFolderInfo;
        private LoggingReconfigurationService _subject;
        private LoggingConfiguration _savedConfig;

        [SetUp]
        public void Setup()
        {
            _savedConfig = LogManager.Configuration;

            _configService = Substitute.For<IConfigService>();
            _appFolderInfo = Substitute.For<IAppFolderInfo>();
            _appFolderInfo.AppDataFolder.Returns("/tmp/seedarr-test");

            _configService.LogToFile.Returns(false);
            _configService.FileLogLevel.Returns("Info");
            _configService.DebugMode.Returns(false);

            _subject = new LoggingReconfigurationService(_configService, _appFolderInfo);
        }

        [TearDown]
        public void TearDown()
        {
            LogManager.Configuration = _savedConfig;
        }

        [Test]
        public void Handle_should_not_throw_when_config_is_null()
        {
            LogManager.Configuration = null;

            Assert.That(() => _subject.Handle(new ConfigSavedEvent()), Throws.Nothing);
        }

        [Test]
        public void Handle_should_reconfigure_when_config_exists()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget("console");
            config.AddTarget(consoleTarget);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget);
            LogManager.Configuration = config;

            Assert.That(() => _subject.Handle(new ConfigSavedEvent()), Throws.Nothing);
        }

        [Test]
        public void Handle_should_add_file_target_when_log_to_file_enabled()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget("console");
            config.AddTarget(consoleTarget);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget);
            LogManager.Configuration = config;

            _configService.LogToFile.Returns(true);
            _configService.FileLogLevel.Returns("Info");

            _subject.Handle(new ConfigSavedEvent());

            Assert.That(LogManager.Configuration.FindTargetByName<FileTarget>("file"), Is.Not.Null);
        }

        [Test]
        public void Handle_should_remove_file_target_when_log_to_file_disabled()
        {
            var config = new LoggingConfiguration();
            var fileTarget = new FileTarget("file") { FileName = "/tmp/seedarr-test/logs/seedarr.txt" };
            config.AddTarget(fileTarget);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);
            LogManager.Configuration = config;

            _configService.LogToFile.Returns(false);

            _subject.Handle(new ConfigSavedEvent());

            Assert.That(LogManager.Configuration.FindTargetByName<FileTarget>("file"), Is.Null);
        }

        [Test]
        public void Handle_should_set_debug_level_when_debug_mode_enabled()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget("console");
            config.AddTarget(consoleTarget);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget);
            LogManager.Configuration = config;

            _configService.DebugMode.Returns(true);

            _subject.Handle(new ConfigSavedEvent());

            var rules = LogManager.Configuration.LoggingRules;
            Assert.That(rules.Count, Is.GreaterThan(0));
            Assert.That(rules[0].Levels, Does.Contain(LogLevel.Debug));
        }

        [Test]
        public void Handle_should_set_info_level_when_debug_mode_disabled()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget("console");
            config.AddTarget(consoleTarget);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, consoleTarget);
            LogManager.Configuration = config;

            _configService.DebugMode.Returns(false);

            _subject.Handle(new ConfigSavedEvent());

            var rules = LogManager.Configuration.LoggingRules;
            Assert.That(rules.Count, Is.GreaterThan(0));
            Assert.That(rules[0].Levels, Does.Not.Contain(LogLevel.Debug));
            Assert.That(rules[0].Levels, Does.Contain(LogLevel.Info));
        }
    }
}
