using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Automation;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class ScriptSystemContextTest
{
    [Test]
    public void RunScript_should_throw_InvalidOperationException_when_external_scripts_disabled_by_default()
    {
        var context = new ScriptSystemContext();

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            context.runScript("myscript.sh");
        });

        Assert.That(ex!.Message, Does.Contain("External script execution is disabled"));
    }

    [Test]
    public void RunScript_should_throw_ArgumentException_when_path_is_empty_or_whitespace()
    {
        var context = new ScriptSystemContext(allowExternalScripts: true);

        Assert.Throws<ArgumentException>(() => context.runScript(""));
        Assert.Throws<ArgumentException>(() => context.runScript("   "));
    }

    [Test]
    public void RunScript_should_throw_ArgumentException_when_path_contains_path_traversal()
    {
        var context = new ScriptSystemContext(allowExternalScripts: true);

        var ex1 = Assert.Throws<ArgumentException>(() => context.runScript("../myscript.sh"));
        Assert.That(ex1!.Message, Does.Contain("Path traversal"));

        var ex2 = Assert.Throws<ArgumentException>(() => context.runScript("/var/scripts/../../bin/evil.sh"));
        Assert.That(ex2!.Message, Does.Contain("Path traversal"));

        var ex3 = Assert.Throws<ArgumentException>(() => context.runScript("sub/..\\evil.py"));
        Assert.That(ex3!.Message, Does.Contain("Path traversal"));
    }

    [Test]
    public void RunScript_should_throw_ArgumentException_when_extension_is_disallowed()
    {
        var context = new ScriptSystemContext(allowExternalScripts: true);

        var ex1 = Assert.Throws<ArgumentException>(() => context.runScript("/bin/bash"));
        Assert.That(ex1!.Message, Does.Contain("allowed extensions"));

        var ex2 = Assert.Throws<ArgumentException>(() => context.runScript("/usr/bin/curl"));
        Assert.That(ex2!.Message, Does.Contain("allowed extensions"));

        var ex3 = Assert.Throws<ArgumentException>(() => context.runScript("/bin/rm"));
        Assert.That(ex3!.Message, Does.Contain("allowed extensions"));
    }

    [Test]
    public void RunScript_should_throw_ArgumentException_when_outside_allowed_script_directory()
    {
        var allowedDir = Path.Combine(Path.GetTempPath(), "seedarr-allowed-scripts");
        var context = new ScriptSystemContext(allowExternalScripts: true, allowedScriptDirectory: allowedDir);

        var ex = Assert.Throws<ArgumentException>(() =>
        {
            context.runScript("/etc/cron.daily/update.sh");
        });

        Assert.That(ex!.Message, Does.Contain("configured script directory"));
    }

    [Test]
    public void RunScript_should_queue_script_when_path_and_extension_are_valid()
    {
        var result = new AutomationExecutionResult();
        var context = new ScriptSystemContext(result: result, allowExternalScripts: true);

        context.runScript("/scripts/clean.py", new[] { "param1", "bad\0arg\nwith\rnewline" }, 30);

        Assert.That(result.ScriptsToRun.Count, Is.EqualTo(1));
        var queued = result.ScriptsToRun[0];
        Assert.That(queued.Path, Is.EqualTo("/scripts/clean.py"));
        Assert.That(queued.Arguments[0], Is.EqualTo("param1"));
        Assert.That(queued.Arguments[1], Is.EqualTo("badarg with newline"));
        Assert.That(queued.TimeoutSeconds, Is.EqualTo(30));
    }

    [Test]
    public void Sleep_should_clamp_duration_to_maximum_two_seconds()
    {
        var context = new ScriptSystemContext();
        var sw = Stopwatch.StartNew();

        // Pass 30 seconds - should be clamped to 2 seconds
        context.sleep(30);

        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(5000));
        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(1900));
    }
}
