using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class YamlScriptRunnerTest
{
    private YamlScriptRunner _runner;

    [SetUp]
    public void SetUp()
    {
        _runner = new YamlScriptRunner();
    }

    [Test]
    public void ShouldExecuteYamlWorkflowAndApplyActions()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "The.Matrix.1999.2160p",
            TotalSize = 15_000_000_000,
            Category = "Unknown",
        };

        var yaml = "name: 'Categorize and Tag'\n" +
                    "steps:\n" +
                    "  - name: 'Check Large'\n" +
                    "    condition: '${torrent.size} > 1000000000'\n" +
                    "    actions:\n" +
                    "      - addTag: '4K-UHD'\n" +
                    "      - setCategory: 'Movies'\n";

        var script = new AutomationScript
        {
            Name = "YAML Rule",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TagsToAdd, Contains.Item("4K-UHD"));
        Assert.That(result.NewCategory, Is.EqualTo("Movies"));
    }

    [Test]
    public void ShouldExecuteYamlActionsWithRecheckAndCommand()
    {
        var torrent = new Torrent
        {
            Id = 99,
            Name = "Linux.ISO",
        };

        var yaml = "name: 'Recheck and Command'\n" +
                    "steps:\n" +
                    "  - name: 'Execute pipeline'\n" +
                    "    actions:\n" +
                    "      - command: 'Backup'\n" +
                    "      - recheck: true\n" +
                    "      - reannounce: true\n";

        var script = new AutomationScript
        {
            Name = "Pipeline",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ShouldRecheck, Is.True);
        Assert.That(result.ShouldReannounce, Is.True);
    }

    [Test]
    public void ShouldContinueExecutingSubsequentStepsWhenContinueOnErrorIsTrue()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test Torrent",
        };

        var yaml = "name: 'Fault Tolerant Pipeline'\n" +
                    "steps:\n" +
                    "  - name: 'Failing Step'\n" +
                    "    continueOnError: true\n" +
                    "    actions:\n" +
                    "      - fail: 'Transient error'\n" +
                    "  - name: 'Recovery Step'\n" +
                    "    actions:\n" +
                    "      - addTag: 'Recovered'\n";

        var script = new AutomationScript
        {
            Name = "Fault Tolerant Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TagsToAdd, Contains.Item("Recovered"));
        Assert.That(result.Warnings.Count, Is.GreaterThan(0));
        Assert.That(result.Warnings[0], Does.Contain("Transient error"));
    }

    [Test]
    public void ShouldAbortWorkflowWhenStepFailsAndContinueOnErrorIsFalse()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test Torrent",
        };

        var yaml = "name: 'Strict Pipeline'\n" +
                    "steps:\n" +
                    "  - name: 'Failing Step'\n" +
                    "    continueOnError: false\n" +
                    "    actions:\n" +
                    "      - fail: 'Critical error'\n" +
                    "  - name: 'Unreachable Step'\n" +
                    "    actions:\n" +
                    "      - addTag: 'NeverAdded'\n";

        var script = new AutomationScript
        {
            Name = "Strict Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.False);
        Assert.That(result.TagsToAdd, Does.Not.Contain("NeverAdded"));
        Assert.That(result.Error, Does.Contain("Critical error"));
    }

    [Test]
    public void ShouldRetryStepUpToRetriesCount()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test Torrent",
        };

        var yaml = "name: 'Retry Pipeline'\n" +
                    "steps:\n" +
                    "  - name: 'Retryable Step'\n" +
                    "    continueOnError: true\n" +
                    "    retries: 2\n" +
                    "    actions:\n" +
                    "      - fail: 'Intermittent failure'\n";

        var script = new AutomationScript
        {
            Name = "Retry Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.OutputLog, Does.Contain("attempt 1/3"));
        Assert.That(result.OutputLog, Does.Contain("attempt 2/3"));
        Assert.That(result.OutputLog, Does.Contain("failed after 3 attempt(s)"));
    }

    [Test]
    public void ShouldNormalizeProgressComparisonWhenComparingWithPercentValues()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Progress.Test",
            Progress = 1.0,
            Status = TorrentStatus.Seeding,
        };

        var yaml = "name: 'Progress Check'\n" +
            "steps:\n" +
            "  - name: 'Check Progress 100'\n" +
            "    condition: '${torrent.progress} >= 100'\n" +
            "    actions:\n" +
            "      - addTag: 'Completed-100'\n";

        var script = new AutomationScript
        {
            Name = "Progress Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TagsToAdd, Contains.Item("Completed-100"));
    }

    [Test]
    public void ShouldExposeProgressPercentInYamlContext()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Name = "Progress.Percent.Test",
            Progress = 0.5,
            Status = TorrentStatus.Downloading,
        };

        var yaml = "name: 'Progress Percent Check'\n" +
            "steps:\n" +
            "  - name: 'Check Progress 50'\n" +
            "    condition: '${torrent.progressPercent} >= 50'\n" +
            "    actions:\n" +
            "      - addTag: 'Halfway'\n";

        var script = new AutomationScript
        {
            Name = "Progress Percent Script",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TagsToAdd, Contains.Item("Halfway"));
    }
}
