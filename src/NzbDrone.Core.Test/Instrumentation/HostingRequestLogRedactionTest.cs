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
    [Test]
    public void AspNet_request_logs_redact_query_secrets()
    {
        if (LogManager.Configuration == null)
        {
            NzbDroneLogger.Register(new StartupContext());
        }

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
            logger.LogInformation(
                "Request starting HTTP/1.1 GET http://127.0.0.1:9899/api/v1/system/status?apikey=scratchkeypass7aaaaaaaaaaaaaaaaaa&access_token=othertokenvalue99 - - -");
            logger.LogInformation(
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
}
