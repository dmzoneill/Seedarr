using System.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;

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

        [Test]
        public void Handle_ApplicationStartedEvent_should_reconfigure_logging()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget("console");
            config.AddTarget(consoleTarget);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget);
            LogManager.Configuration = config;

            _configService.DebugMode.Returns(true);

            _subject.Handle(new ApplicationStartedEvent());

            var rules = LogManager.Configuration.LoggingRules;
            Assert.That(rules.Count, Is.GreaterThan(0));
            Assert.That(rules[0].Levels, Does.Contain(LogLevel.Debug));
        }

        [Test]
        public void ReconfigureLogging_should_reconfigure_logging_directly()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget("console");
            config.AddTarget(consoleTarget);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget);
            LogManager.Configuration = config;

            _configService.LogToFile.Returns(true);
            _configService.FileLogLevel.Returns("Debug");

            _subject.ReconfigureLogging();

            Assert.That(LogManager.Configuration.FindTargetByName<FileTarget>("file"), Is.Not.Null);
        }

        [Test]
        public void RemoveRulesForTarget_should_remove_rule_when_target_is_not_first()
        {
            var config = new LoggingConfiguration();
            var otherTarget = new ConsoleTarget("other");
            var consoleTarget = new ConsoleTarget("console");
            config.AddTarget(otherTarget);
            config.AddTarget(consoleTarget);

            var rule = new LoggingRule("*", LogLevel.Info, LogLevel.Fatal, otherTarget);
            rule.Targets.Add(consoleTarget);
            config.LoggingRules.Add(rule);
            LogManager.Configuration = config;

            _subject.ReconfigureLogging();

            // The multi-target rule containing "console" should have been removed and replaced with a fresh console rule
            Assert.That(config.LoggingRules, Has.None.Matches<LoggingRule>(r => r.Targets.Contains(otherTarget)));
        }

        [Test]
        public void Initialize_should_reconfigure_logging()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget("console");
            config.AddTarget(consoleTarget);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget);
            LogManager.Configuration = config;

            _configService.LogToFile.Returns(true);
            _configService.FileLogLevel.Returns("Debug");

            _subject.Initialize();

            Assert.That(LogManager.Configuration.FindTargetByName<FileTarget>("file"), Is.Not.Null);
        }

        [Test]
        public void ReconfigureLogging_when_file_target_already_exists_from_startup_should_update_rules()
        {
            var config = new LoggingConfiguration();
            var fileTarget = new FileTarget("file") { FileName = "/tmp/seedarr-test/logs/seedarr.txt" };
            config.AddTarget(fileTarget);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);
            LogManager.Configuration = config;

            _configService.LogToFile.Returns(true);
            _configService.FileLogLevel.Returns("Trace");

            _subject.ReconfigureLogging();

            var rules = LogManager.Configuration.LoggingRules.Where(r => r.Targets.Contains(fileTarget)).ToList();
            Assert.That(rules, Has.Count.EqualTo(1));
            Assert.That(rules[0].Levels, Does.Contain(LogLevel.Trace));
        }
    }
}
