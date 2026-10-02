using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Developer.Testing;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class DeveloperTestRunnerTest
{
    private DeveloperTestRunner _runner;

    [SetUp]
    public void SetUp()
    {
        _runner = new DeveloperTestRunner();
    }

    [Test]
    public void Should_discover_registered_diagnostic_tests()
    {
        var tests = _runner.DiscoverTests();

        Assert.That(tests, Is.Not.Null);
        Assert.That(tests.Count, Is.GreaterThanOrEqualTo(50));
        Assert.That(tests.Any(t => t.Id == "db-integrity"), Is.True);
        Assert.That(tests.Any(t => t.Id == "engine-status"), Is.True);
        Assert.That(tests.Any(t => t.Id == "network-dns"), Is.True);
        Assert.That(tests.Any(t => t.Id == "memory-health"), Is.True);
    }

    [Test]
    public async Task Should_execute_system_uptime_check_successfully()
    {
        var result = await _runner.RunTestAsync("system-uptime");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.TestId, Is.EqualTo("system-uptime"));
        Assert.That(result.Status, Is.EqualTo("Passed"));
        Assert.That(result.Output, Does.Contain("uptime"));
    }

    [Test]
    public async Task Should_execute_memory_health_check_successfully()
    {
        var result = await _runner.RunTestAsync("memory-health");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Status, Is.EqualTo("Passed"));
        Assert.That(result.Output, Does.Contain("Allocated Memory"));
    }

    [Test]
    public async Task Should_skip_unrecognized_test_identifier()
    {
        var result = await _runner.RunTestAsync("nonexistent-test-id");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Status, Is.EqualTo("Skipped"));
        Assert.That(result.Output, Does.Contain("Unrecognized test identifier"));
    }

    [Test]
    public async Task Should_run_batch_tests_with_category_filter()
    {
        var request = new TestExecutionRequest
        {
            Category = "System",
        };

        var response = await _runner.RunTestsAsync(request);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.TotalTests, Is.EqualTo(6));
        Assert.That(response.Results.Count, Is.EqualTo(6));
        Assert.That(response.Results.All(r => r.Category == "System"), Is.True);
    }

    [Test]
    public async Task Should_record_history_and_clear_history()
    {
        await _runner.RunTestAsync("system-uptime");
        await _runner.RunTestAsync("cpu-affinity");

        var history = _runner.GetRecentResults();
        Assert.That(history.Count, Is.EqualTo(2));

        _runner.ClearHistory();
        var emptyHistory = _runner.GetRecentResults();
        Assert.That(emptyHistory.Count, Is.EqualTo(0));
    }
}
