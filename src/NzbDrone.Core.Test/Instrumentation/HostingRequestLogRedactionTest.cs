using System.Linq;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Host;

namespace NzbDrone.Core.Test.Instrumentation;

[TestFixture]
public class HostingRequestLogRedactionTest
{
    [SetUp]
    public void EnsureLoggingConfigured()
    {
        if (LogManager.Configuration == null)
        {
            NzbDroneLogger.Register(new StartupContext());
        }
    }

    [Test]
    public void AspNet_request_logs_redact_query_secrets()
    {
        var target = new MemoryTarget("hosting-redaction-test")
        {
            Layout = "${sanitized-message}"
        };
        var rule = new NLog.Config.LoggingRule("*", NLog.LogLevel.Info, target);
        LogManager.Configuration.AddTarget(target);
        LogManager.Configuration.LoggingRules.Add(rule);
        LogManager.ReconfigExistingLoggers();

        try
        {
            using var factory = LoggerFactory.Create(HostingLog.Configure);
            var logger = factory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");
            logger.LogWarning(
                "Request starting HTTP/1.1 GET http://127.0.0.1:9899/api/v1/system/status" +
                "?apikey=scratchkeypass7aaaaaaaaaaaaaaaaaa&access_token=othertokenvalue99 - - -");
            logger.LogWarning(
                "Request finished HTTP/1.1 GET http://127.0.0.1:9899/api/v1/terminal/ws?token=terminal-secret-value - 101");

            var text = string.Join("\n", target.Logs);
            Assert.That(text, Does.Contain("apikey=[REDACTED]"));
            Assert.That(text, Does.Contain("access_token=[REDACTED]"));
            Assert.That(text, Does.Contain("token=[REDACTED]"));
            Assert.That(text, Does.Not.Contain("scratchkeypass7"));
            Assert.That(text, Does.Not.Contain("othertokenvalue99"));
            Assert.That(text, Does.Not.Contain("terminal-secret-value"));
        }
        finally
        {
            LogManager.Configuration.LoggingRules.Remove(rule);
            LogManager.Configuration.RemoveTarget(target.Name);
            LogManager.ReconfigExistingLoggers();
        }
    }

    [Test]
    public void AspNet_information_logs_are_filtered_but_other_categories_still_log()
    {
        var target = new MemoryTarget("hosting-filter-test")
        {
            Layout = "${logger}|${level}|${sanitized-message}"
        };
        var rule = new NLog.Config.LoggingRule("*", NLog.LogLevel.Info, target);
        LogManager.Configuration.AddTarget(target);
        LogManager.Configuration.LoggingRules.Add(rule);
        LogManager.ReconfigExistingLoggers();

        try
        {
            using var factory = LoggerFactory.Create(HostingLog.Configure);
            var hosting = factory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");
            var routing = factory.CreateLogger("Microsoft.AspNetCore.Routing.EndpointMiddleware");
            var lifetime = factory.CreateLogger("Microsoft.Hosting.Lifetime");

            Assert.That(hosting.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information), Is.False);
            Assert.That(hosting.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning), Is.True);

            hosting.LogInformation("Request starting HTTP/1.1 GET http://127.0.0.1:9899/api/v1/system/status");
            routing.LogInformation("Executing endpoint 'status'");
            hosting.LogWarning("Request body too large");
            lifetime.LogInformation("Application started.");

            var lines = target.Logs.ToList();
            Assert.That(lines, Has.None.Contains("Request starting"));
            Assert.That(lines, Has.None.Contains("Executing endpoint"));
            Assert.That(
                lines,
                Has.Some.Contains("Microsoft.AspNetCore.Hosting.Diagnostics|Warn|Request body too large"));
            Assert.That(lines, Has.Some.Contains("Microsoft.Hosting.Lifetime|Info|Application started."));
        }
        finally
        {
            LogManager.Configuration.LoggingRules.Remove(rule);
            LogManager.Configuration.RemoveTarget(target.Name);
            LogManager.ReconfigExistingLoggers();
        }
    }
}
