using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.Blocklist;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Test.Blocklist;

[TestFixture]
public class BlocklistUpdateTaskTests
{
    private IPeerBlocklistSyncService _syncService;
    private IConfigService _configService;
    private BlocklistUpdateTask _task;

    [SetUp]
    public void SetUp()
    {
        _syncService = Substitute.For<IPeerBlocklistSyncService>();
        _configService = Substitute.For<IConfigService>();
        _task = new BlocklistUpdateTask(_syncService, _configService);
    }

    [Test]
    public void DefaultInterval_should_default_to_1440_minutes_when_interval_is_one_day()
    {
        _configService.BlocklistAutoUpdateIntervalDays.Returns(1);

        Assert.That(_task.DefaultInterval, Is.EqualTo(1440));
    }

    [Test]
    public void DefaultInterval_should_scale_with_configured_days()
    {
        _configService.BlocklistAutoUpdateIntervalDays.Returns(7);

        Assert.That(_task.DefaultInterval, Is.EqualTo(10080));
    }

    [Test]
    public void DefaultInterval_should_fallback_to_BlocklistUpdateIntervalDays_if_AutoUpdateIntervalDays_is_zero()
    {
        _configService.BlocklistAutoUpdateIntervalDays.Returns(0);
        _configService.BlocklistUpdateIntervalDays.Returns(3);

        Assert.That(_task.DefaultInterval, Is.EqualTo(4320));
    }

    [Test]
    public void DefaultInterval_should_handle_null_config_service()
    {
        var task = new BlocklistUpdateTask(_syncService, null);

        Assert.That(task.DefaultInterval, Is.EqualTo(1440));
    }

    [Test]
    public void Execute_when_BlocklistEnabled_is_true_should_call_SyncAsync()
    {
        _configService.BlocklistEnabled.Returns(true);
        _syncService.SyncAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BlocklistSyncResult { Success = true }));

        _task.Execute();

        _syncService.Received(1).SyncAsync(null, false, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Execute_when_BlocklistEnabled_is_false_should_not_call_SyncAsync()
    {
        _configService.BlocklistEnabled.Returns(false);

        _task.Execute();

        _syncService.DidNotReceiveWithAnyArgs().SyncAsync(default, default, default);
    }

    [Test]
    public void Execute_should_catch_exceptions_without_crashing()
    {
        _configService.BlocklistEnabled.Returns(true);
        _syncService.SyncAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Sync error"));

        Assert.DoesNotThrow(() => _task.Execute());
    }

    [Test]
    public void Execute_command_with_Force_true_should_call_SyncAsync_even_if_blocklist_disabled()
    {
        _configService.BlocklistEnabled.Returns(false);
        _syncService.SyncAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BlocklistSyncResult { Success = true }));

        var command = new BlocklistUpdateCommand { Force = true };
        _task.Execute(command);

        _syncService.Received(1).SyncAsync(null, true, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Execute_command_with_Force_false_when_enabled_should_call_SyncAsync()
    {
        _configService.BlocklistEnabled.Returns(true);
        _syncService.SyncAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BlocklistSyncResult { Success = true }));

        var command = new BlocklistUpdateCommand { Force = false };
        _task.Execute(command);

        _syncService.Received(1).SyncAsync(null, false, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Execute_command_with_Force_false_when_disabled_should_not_call_SyncAsync()
    {
        _configService.BlocklistEnabled.Returns(false);

        var command = new BlocklistUpdateCommand { Force = false };
        _task.Execute(command);

        _syncService.DidNotReceiveWithAnyArgs().SyncAsync(default, default, default);
    }

    [Test]
    public void Execute_command_should_rethrow_exception_on_failure()
    {
        _configService.BlocklistEnabled.Returns(true);
        _syncService.SyncAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network failure"));

        var command = new BlocklistUpdateCommand { Force = true };

        Assert.Throws<HttpRequestException>(() => _task.Execute(command));
    }
}
