using System.Collections.Generic;
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

    [Test]
    public async Task Should_execute_bitTorrent_and_network_smoke_checks()
    {
        var engine = await _runner.RunTestAsync("engine-status");
        Assert.That(engine.Status, Is.EqualTo("Passed"));
        Assert.That(engine.Output, Is.Not.Empty);

        var dns = await _runner.RunTestAsync("network-dns");
        Assert.That(dns.Status, Is.EqualTo("Passed"));
        Assert.That(dns.Output, Does.Contain("DNS").Or.Contain("dns"));

        var interfaces = await _runner.RunTestAsync("network-interfaces");
        Assert.That(interfaces.Status, Is.EqualTo("Passed"));
    }

    [Test]
    public async Task Should_execute_messaging_and_scheduler_checks()
    {
        var signalR = await _runner.RunTestAsync("signalr-hub");
        Assert.That(signalR.Status, Is.EqualTo("Passed"));

        var scheduler = await _runner.RunTestAsync("scheduler-queue");
        Assert.That(scheduler.Status, Is.EqualTo("Passed"));

        var eventBus = await _runner.RunTestAsync("event-bus");
        Assert.That(eventBus.Status, Is.EqualTo("Passed"));
    }

    [Test]
    public async Task Should_run_tests_by_explicit_ids()
    {
        var response = await _runner.RunTestsAsync(new TestExecutionRequest
        {
            TestIds = new List<string> { "thread-pool", "gc-memory-info", "process-telemetry" },
        });

        Assert.That(response.TotalTests, Is.EqualTo(3));
        Assert.That(response.Passed, Is.EqualTo(3));
        Assert.That(response.Results.All(r => r.Status == "Passed"), Is.True);
    }

    [Test]
    public async Task Should_cap_recent_history_limit()
    {
        for (var i = 0; i < 5; i++)
        {
            await _runner.RunTestAsync("cpu-affinity");
        }

        var limited = _runner.GetRecentResults(2);
        Assert.That(limited.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Should_execute_full_diagnostic_catalog_when_run_all_requested()
    {
        var catalogSize = _runner.DiscoverTests().Count;
        var response = await _runner.RunTestsAsync(new TestExecutionRequest { RunAll = true });

        Assert.That(response.TotalTests, Is.EqualTo(catalogSize));
        Assert.That(response.Results, Has.Count.EqualTo(catalogSize));
        Assert.That(response.Passed + response.Failed + response.Skipped, Is.EqualTo(catalogSize));
        Assert.That(response.Results.All(r => !string.IsNullOrWhiteSpace(r.Status)), Is.True);
    }

    [Test]
    public async Task Should_run_each_diagnostic_category_batch()
    {
        foreach (var category in new[] { "Database", "Storage", "BitTorrent", "Network", "Scheduler", "Messaging", "System", "Configuration" })
        {
            var response = await _runner.RunTestsAsync(new TestExecutionRequest { Category = category });
            Assert.That(response.TotalTests, Is.GreaterThan(0), $"Expected tests in category {category}");
            Assert.That(response.Results.All(r => r.Category == category), Is.True);
        }
    }
}
