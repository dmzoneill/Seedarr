using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Notifications;

[TestFixture]
public class LifecycleScriptEventHandlerTest
{
    [Test]
    public async Task Handle_should_throttle_rapid_events_to_max_concurrency_of_four()
    {
        var configService = Substitute.For<IConfigService>();
        configService.OnDownloadCompleteScript.Returns("/scripts/done.sh");

        var customScriptService = Substitute.For<ICustomScriptService>();

        var currentConcurrency = 0;
        var maxObservedConcurrency = 0;
        var lockObj = new object();
        var completedCount = 0;
        var allCompleted = new TaskCompletionSource<bool>();

        customScriptService.ExecuteScriptAsync(Arg.Any<string>(), Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(async callInfo =>
            {
                lock (lockObj)
                {
                    currentConcurrency++;
                    if (currentConcurrency > maxObservedConcurrency)
                    {
                        maxObservedConcurrency = currentConcurrency;
                    }
                }

                await Task.Delay(25);

                lock (lockObj)
                {
                    currentConcurrency--;
                    completedCount++;
                    if (completedCount == 8)
                    {
                        allCompleted.TrySetResult(true);
                    }
                }

                return true;
            });

        using var handler = new LifecycleScriptEventHandler(customScriptService, configService, maxConcurrency: 4);

        var torrent = new Torrent { Id = 1, Name = "Test" };
        for (var i = 0; i < 8; i++)
        {
            handler.Handle(new TorrentDownloadCompletedEvent(torrent));
        }

        var completed = await Task.WhenAny(allCompleted.Task, Task.Delay(5000));
        Assert.That(completed, Is.EqualTo(allCompleted.Task), "Timed out waiting for events to complete");
        Assert.That(maxObservedConcurrency, Is.EqualTo(4));
        await customScriptService.Received(8).ExecuteScriptAsync("/scripts/done.sh", torrent, "OnDownloadComplete");
    }

    [Test]
    public async Task Handle_TorrentDeletedEvent_should_execute_configured_scripts()
    {
        var configService = Substitute.For<IConfigService>();
        configService.OnTorrentDeletedScript.Returns("/scripts/delete.sh");
        configService.ScriptTorrentRemovedFilename.Returns("/scripts/removed.sh");

        var customScriptService = Substitute.For<ICustomScriptService>();
        var tcs1 = new TaskCompletionSource<bool>();
        var tcs2 = new TaskCompletionSource<bool>();

        customScriptService.ExecuteScriptAsync("/scripts/delete.sh", Arg.Any<Torrent>(), "OnDelete")
            .Returns(callInfo =>
            {
                tcs1.TrySetResult(true);
                return Task.FromResult(true);
            });
        customScriptService.ExecuteScriptAsync("/scripts/removed.sh", Arg.Any<Torrent>(), "TorrentRemoved")
            .Returns(callInfo =>
            {
                tcs2.TrySetResult(true);
                return Task.FromResult(true);
            });

        using var handler = new LifecycleScriptEventHandler(customScriptService, configService);

        var torrent = new Torrent { Id = 42, Name = "DeletedTorrent" };
        handler.Handle(new TorrentDeletedEvent(42, torrent));

        await Task.WhenAll(tcs1.Task, tcs2.Task);

        await customScriptService.Received(1).ExecuteScriptAsync("/scripts/delete.sh", torrent, "OnDelete");
        await customScriptService.Received(1).ExecuteScriptAsync("/scripts/removed.sh", torrent, "TorrentRemoved");
    }

    [Test]
    public async Task Handle_HealthIssueEvent_should_execute_health_script()
    {
        var configService = Substitute.For<IConfigService>();
        configService.OnHealthIssueScript.Returns("/scripts/health.sh");

        var customScriptService = Substitute.For<ICustomScriptService>();
        var tcs = new TaskCompletionSource<bool>();

        customScriptService.ExecuteScriptAsync("/scripts/health.sh", Arg.Any<Torrent>(), "OnHealthIssue")
            .Returns(callInfo =>
            {
                tcs.TrySetResult(true);
                return Task.FromResult(true);
            });

        using var handler = new LifecycleScriptEventHandler(customScriptService, configService);

        var torrent = new Torrent { Id = 5, Name = "UnhealthyTorrent" };
        handler.Handle(new HealthIssueEvent(torrent, "DiskCheck", "Low space"));

        await tcs.Task;

        await customScriptService.Received(1).ExecuteScriptAsync("/scripts/health.sh", torrent, "OnHealthIssue");
    }

    [Test]
    public async Task Handle_FileMoveCompletedEvent_should_execute_rename_script()
    {
        var configService = Substitute.For<IConfigService>();
        configService.OnFileMoveScript.Returns("/scripts/move.sh");

        var customScriptService = Substitute.For<ICustomScriptService>();
        var tcs = new TaskCompletionSource<bool>();

        customScriptService.ExecuteScriptAsync("/scripts/move.sh", Arg.Any<Torrent>(), "OnRename")
            .Returns(callInfo =>
            {
                tcs.TrySetResult(true);
                return Task.FromResult(true);
            });

        using var handler = new LifecycleScriptEventHandler(customScriptService, configService);

        var torrent = new Torrent { Id = 7, Name = "MovedTorrent" };
        handler.Handle(new FileMoveCompletedEvent(torrent, "/src/path", "/dst/path"));

        await tcs.Task;

        await customScriptService.Received(1).ExecuteScriptAsync("/scripts/move.sh", torrent, "OnRename");
    }

    [Test]
    public async Task Handle_ApplicationUpdatedEvent_should_execute_upgrade_script()
    {
        var configService = Substitute.For<IConfigService>();
        configService.OnApplicationUpdatedScript.Returns("/scripts/upgrade.sh");

        var customScriptService = Substitute.For<ICustomScriptService>();
        var tcs = new TaskCompletionSource<bool>();

        customScriptService.ExecuteScriptAsync("/scripts/upgrade.sh", null, "OnUpgrade")
            .Returns(callInfo =>
            {
                tcs.TrySetResult(true);
                return Task.FromResult(true);
            });

        using var handler = new LifecycleScriptEventHandler(customScriptService, configService);

        handler.Handle(new ApplicationUpdatedEvent("1.0.0", "1.1.0"));

        await tcs.Task;

        await customScriptService.Received(1).ExecuteScriptAsync("/scripts/upgrade.sh", null, "OnUpgrade");
    }

    [Test]
    public async Task ExecuteThrottledScriptAsync_when_script_returns_false_publishes_health_issue_event()
    {
        var configService = Substitute.For<IConfigService>();
        var customScriptService = Substitute.For<ICustomScriptService>();
        var eventAggregator = Substitute.For<IEventAggregator>();

        customScriptService.ExecuteScriptAsync(Arg.Any<string>(), Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(false));

        using var handler = new LifecycleScriptEventHandler(customScriptService, configService, eventAggregator);

        var torrent = new Torrent { Id = 10, Name = "FailTorrent" };
        await handler.ExecuteThrottledScriptAsync("/scripts/failing.sh", torrent, "OnDownloadComplete");

        eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Torrent == torrent &&
            e.Source == "CustomScript" &&
            e.Message.Contains("failed or timed out") &&
            !e.IsResolved));
    }

    [Test]
    public async Task Handle_TorrentDownloadCompletedEvent_should_deduplicate_identical_script_paths()
    {
        var configService = Substitute.For<IConfigService>();
        configService.OnDownloadCompleteScript.Returns("/scripts/done.sh");
        configService.ScriptTorrentDoneFilename.Returns("/scripts/done.sh");

        var customScriptService = Substitute.For<ICustomScriptService>();
        customScriptService.ExecuteScriptAsync(Arg.Any<string>(), Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        using var handler = new LifecycleScriptEventHandler(customScriptService, configService);

        var torrent = new Torrent { Id = 1, Name = "DedupeTest" };
        handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        await Task.Delay(100);

        await customScriptService.Received(1).ExecuteScriptAsync("/scripts/done.sh", torrent, "OnDownloadComplete");
    }

    [Test]
    public async Task Handle_TorrentDownloadCompletedEvent_should_deduplicate_quoted_and_unquoted_same_script()
    {
        var configService = Substitute.For<IConfigService>();
        configService.OnDownloadCompleteScript.Returns("/scripts/done.sh");
        configService.ScriptTorrentDoneFilename.Returns("\"/scripts/done.sh\"");

        var customScriptService = Substitute.For<ICustomScriptService>();
        customScriptService.ExecuteScriptAsync(Arg.Any<string>(), Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        using var handler = new LifecycleScriptEventHandler(customScriptService, configService);

        var torrent = new Torrent { Id = 2, Name = "QuotedDedupeTest" };
        handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        await Task.Delay(100);

        await customScriptService.Received(1).ExecuteScriptAsync("/scripts/done.sh", torrent, "OnDownloadComplete");
    }

    [Test]
    public async Task Handle_TorrentDownloadCompletedEvent_should_run_both_when_paths_differ()
    {
        var configService = Substitute.For<IConfigService>();
        configService.OnDownloadCompleteScript.Returns("/scripts/done1.sh");
        configService.ScriptTorrentDoneFilename.Returns("/scripts/done2.sh");

        var customScriptService = Substitute.For<ICustomScriptService>();
        var tcs1 = new TaskCompletionSource<bool>();
        var tcs2 = new TaskCompletionSource<bool>();

        customScriptService.ExecuteScriptAsync("/scripts/done1.sh", Arg.Any<Torrent>(), "OnDownloadComplete")
            .Returns(callInfo =>
            {
                tcs1.TrySetResult(true);
                return Task.FromResult(true);
            });
        customScriptService.ExecuteScriptAsync("/scripts/done2.sh", Arg.Any<Torrent>(), "OnDownloadComplete")
            .Returns(callInfo =>
            {
                tcs2.TrySetResult(true);
                return Task.FromResult(true);
            });

        using var handler = new LifecycleScriptEventHandler(customScriptService, configService);

        var torrent = new Torrent { Id = 3, Name = "DifferentScriptsTest" };
        handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        await Task.WhenAll(tcs1.Task, tcs2.Task);

        await customScriptService.Received(1).ExecuteScriptAsync("/scripts/done1.sh", torrent, "OnDownloadComplete");
        await customScriptService.Received(1).ExecuteScriptAsync("/scripts/done2.sh", torrent, "OnDownloadComplete");
    }

    [Test]
    public async Task Handle_TorrentSeedGoalReachedEvent_should_deduplicate_identical_script_paths()
    {
        var configService = Substitute.For<IConfigService>();
        configService.OnSeedGoalReachedScript.Returns("/scripts/seeding_done.sh");
        configService.ScriptTorrentDoneSeedingFilename.Returns("/scripts/seeding_done.sh");

        var customScriptService = Substitute.For<ICustomScriptService>();
        customScriptService.ExecuteScriptAsync(Arg.Any<string>(), Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        using var handler = new LifecycleScriptEventHandler(customScriptService, configService);

        var torrent = new Torrent { Id = 4, Name = "SeedDedupeTest" };
        handler.Handle(new TorrentSeedGoalReachedEvent(torrent));

        await Task.Delay(100);

        await customScriptService.Received(1).ExecuteScriptAsync("/scripts/seeding_done.sh", torrent, "OnSeedGoalReached");
    }
}
