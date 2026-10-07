// Copyright (c) FeedItOut. All rights reserved.

using NUnit.Framework;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.Terminal;

[TestFixture]
public class TerminalEnvironmentSanitizerTest
{
    [Test]
    public void BuildSanitizedEnvironment_drops_dangerous_inherited_variables()
    {
        Environment.SetEnvironmentVariable("LD_PRELOAD", "/tmp/evil.so");
        Environment.SetEnvironmentVariable("BASH_ENV", "/tmp/malicious.sh");
        Environment.SetEnvironmentVariable("SEEDARR_API_KEY", "secret");
        Environment.SetEnvironmentVariable("MY_DEPLOYMENT_LEAK", "supersecret");

        try
        {
            var env = TerminalEnvironmentSanitizer.BuildSanitizedEnvironment("/tmp/cwd");

            Assert.That(env.ContainsKey("LD_PRELOAD"), Is.False);
            Assert.That(env.ContainsKey("BASH_ENV"), Is.False);
            Assert.That(env.ContainsKey("SEEDARR_API_KEY"), Is.False);
            Assert.That(env.ContainsKey("MY_DEPLOYMENT_LEAK"), Is.False);
            Assert.That(env["PWD"], Is.EqualTo("/tmp/cwd"));
            Assert.That(env["TERM"], Is.EqualTo("xterm-256color"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LD_PRELOAD", null);
            Environment.SetEnvironmentVariable("BASH_ENV", null);
            Environment.SetEnvironmentVariable("SEEDARR_API_KEY", null);
            Environment.SetEnvironmentVariable("MY_DEPLOYMENT_LEAK", null);
        }
    }

    [Test]
    public void Sanitize_process_start_info_matches_build_sanitized_environment()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("/bin/sh")
        {
            WorkingDirectory = "/var/seedarr",
        };

        TerminalEnvironmentSanitizer.Sanitize(startInfo);
        var expected = TerminalEnvironmentSanitizer.BuildSanitizedEnvironment("/var/seedarr");

        Assert.That(startInfo.Environment.Count, Is.EqualTo(expected.Count));
        foreach (var kvp in expected)
        {
            Assert.That(startInfo.Environment[kvp.Key], Is.EqualTo(kvp.Value));
        }
    }
}
