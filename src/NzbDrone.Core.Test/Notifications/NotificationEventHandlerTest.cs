using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Notifications;

[TestFixture]
public class NotificationEventHandlerTest
{
    private INotificationRepository _notificationRepository;
    private IWebhookDispatcher _webhookDispatcher;
    private ICustomScriptService _customScriptService;
    private NotificationEventHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _notificationRepository = Substitute.For<INotificationRepository>();
        _webhookDispatcher = Substitute.For<IWebhookDispatcher>();
        _customScriptService = Substitute.For<ICustomScriptService>();
        _handler = new NotificationEventHandler(
            _notificationRepository,
            _webhookDispatcher,
            _customScriptService);
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_dispatches_when_torrent_tags_match()
    {
        var dispatchedSignal = new TaskCompletionSource<bool>();
        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => dispatchedSignal.TrySetResult(true));

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Tag Match Notif",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Tags = new List<int> { 1, 2 },
            Settings = "{\"url\":\"http://test/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        var torrent = new Torrent
        {
            Id = 10,
            Name = "My Torrent",
            TagIds = new List<int> { 2, 5 }
        };

        _handler.Handle(new TorrentAddedEvent(torrent));

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(300));
        Assert.That(completed, Is.EqualTo(dispatchedSignal.Task), "Notification should be dispatched for matching tag");
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_skips_when_torrent_tags_do_not_match()
    {
        var dispatchedSignal = new TaskCompletionSource<bool>();
        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => dispatchedSignal.TrySetResult(true));

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Tag Filter Notif",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Tags = new List<int> { 1, 2 },
            Settings = "{\"url\":\"http://test/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        var torrent = new Torrent
        {
            Id = 10,
            Name = "My Torrent",
            TagIds = new List<int> { 3, 4 }
        };

        _handler.Handle(new TorrentAddedEvent(torrent));

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(150));
        Assert.That(completed, Is.Not.EqualTo(dispatchedSignal.Task), "Notification should not be dispatched for non-matching tags");
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_dispatches_when_torrent_category_matches()
    {
        var dispatchedSignal = new TaskCompletionSource<bool>();
        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => dispatchedSignal.TrySetResult(true));

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Category Filter Notif",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Categories = new List<string> { "Movies", "tv" },
            Settings = "{\"url\":\"http://test/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        var torrent = new Torrent
        {
            Id = 10,
            Name = "My Movie",
            Category = "TV" // Case-insensitive match against "tv"
        };

        _handler.Handle(new TorrentAddedEvent(torrent));

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(300));
        Assert.That(completed, Is.EqualTo(dispatchedSignal.Task), "Notification should be dispatched for matching category");
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_dispatches_when_torrent_label_matches_category_filter()
    {
        var dispatchedSignal = new TaskCompletionSource<bool>();
        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => dispatchedSignal.TrySetResult(true));

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Label Category Notif",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Categories = new List<string> { "Anime" },
            Settings = "{\"url\":\"http://test/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        var torrent = new Torrent
        {
            Id = 10,
            Name = "My Anime",
            Category = null,
            Label = "Anime"
        };

        _handler.Handle(new TorrentAddedEvent(torrent));

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(300));
        Assert.That(completed, Is.EqualTo(dispatchedSignal.Task), "Notification should be dispatched when torrent label matches category filter");
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_skips_when_torrent_category_does_not_match()
    {
        var dispatchedSignal = new TaskCompletionSource<bool>();
        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => dispatchedSignal.TrySetResult(true));

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Category Filter Notif",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Categories = new List<string> { "Movies" },
            Settings = "{\"url\":\"http://test/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        var torrent = new Torrent
        {
            Id = 10,
            Name = "My TV Show",
            Category = "TV"
        };

        _handler.Handle(new TorrentAddedEvent(torrent));

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(150));
        Assert.That(completed, Is.Not.EqualTo(dispatchedSignal.Task), "Notification should not be dispatched for non-matching category");
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_skips_when_both_tag_and_category_required_but_only_tag_matches()
    {
        var dispatchedSignal = new TaskCompletionSource<bool>();
        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => dispatchedSignal.TrySetResult(true));

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Tag and Category Filter Notif",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Tags = new List<int> { 1 },
            Categories = new List<string> { "Movies" },
            Settings = "{\"url\":\"http://test/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        var torrent = new Torrent
        {
            Id = 10,
            Name = "My Music",
            TagIds = new List<int> { 1 },
            Category = "Music" // Tag matches, but category does not
        };

        _handler.Handle(new TorrentAddedEvent(torrent));

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(150));
        Assert.That(completed, Is.Not.EqualTo(dispatchedSignal.Task), "Notification should not dispatch if category fails even if tag matches");
    }

    [Test]
    public async Task Handle_ApplicationUpdatedEvent_dispatches_only_to_channels_without_tag_restrictions()
    {
        var dispatchedUrls = new List<string>();
        var lockObj = new object();
        var dispatchedSignal = new TaskCompletionSource<bool>();

        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(callInfo =>
            {
                lock (lockObj)
                {
                    dispatchedUrls.Add(callInfo.Arg<string>());
                }

                dispatchedSignal.TrySetResult(true);
            });

        var untaggedNotif = new NotificationDefinition
        {
            Id = 1,
            Name = "Untagged Notif",
            Implementation = "Webhook",
            Enable = true,
            OnApplicationUpdate = true,
            Tags = new List<int>(), // No tag restrictions
            Settings = "{\"url\":\"http://test/untagged\"}"
        };

        var taggedNotif = new NotificationDefinition
        {
            Id = 2,
            Name = "Tagged Notif",
            Implementation = "Webhook",
            Enable = true,
            OnApplicationUpdate = true,
            Tags = new List<int> { 99 }, // Has tag restriction
            Settings = "{\"url\":\"http://test/tagged\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { untaggedNotif, taggedNotif });

        _handler.Handle(new ApplicationUpdatedEvent("1.0.0", "1.1.0"));

        await Task.WhenAny(dispatchedSignal.Task, Task.Delay(300));

        lock (lockObj)
        {
            Assert.That(dispatchedUrls, Contains.Item("http://test/untagged"));
            Assert.That(dispatchedUrls, Does.Not.Contain("http://test/tagged"));
        }
    }

    [Test]
    public async Task Handle_ArchiveExtractionFailedEvent_dispatches_single_notification_when_both_health_and_manual_enabled()
    {
        var dispatchCount = 0;
        var signal = new TaskCompletionSource<bool>();

        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ =>
            {
                Interlocked.Increment(ref dispatchCount);
                signal.TrySetResult(true);
            });

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Error Notif",
            Implementation = "Webhook",
            Enable = true,
            OnHealthIssue = true,
            OnManualInteractionRequired = true,
            Settings = "{\"url\":\"http://test/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        var torrent = new Torrent { Id = 42, Name = "Extracted File Torrent" };
        _handler.Handle(new ArchiveExtractionFailedEvent(torrent, "Archive checksum mismatch"));

        await Task.WhenAny(signal.Task, Task.Delay(300));
        await Task.Delay(50);

        Assert.That(dispatchCount, Is.EqualTo(1), "Extraction failed event must dispatch exactly one notification");
    }

    [Test]
    public async Task Handle_ArchiveExtractionFailedEvent_includes_error_message_in_payload()
    {
        object capturedPayload = null;
        var signal = new TaskCompletionSource<bool>();

        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(callInfo =>
            {
                capturedPayload = callInfo.Arg<object>();
                signal.TrySetResult(true);
            });

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Error Notif",
            Implementation = "Webhook",
            Enable = true,
            OnHealthIssue = true,
            Settings = "{\"url\":\"http://test/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        var torrent = new Torrent { Id = 42, Name = "Extracted File Torrent" };
        var errorMessage = "Archive checksum mismatch in file part 2";
        _handler.Handle(new ArchiveExtractionFailedEvent(torrent, errorMessage));

        var completed = await Task.WhenAny(signal.Task, Task.Delay(3000));
        Assert.That(completed, Is.EqualTo(signal.Task), "Notification should be dispatched");
        Assert.That(capturedPayload, Is.Not.Null);

        var extractedError = NotificationPayloadBuilder.ExtractErrorMessage(capturedPayload);
        Assert.That(extractedError, Is.EqualTo(errorMessage), "Payload must preserve the ArchiveExtractionFailedEvent ErrorMessage");
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_Error_dispatches_single_notification_when_both_health_and_manual_enabled()
    {
        var dispatchCount = 0;
        var signal = new TaskCompletionSource<bool>();

        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ =>
            {
                Interlocked.Increment(ref dispatchCount);
                signal.TrySetResult(true);
            });

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Error Notif",
            Implementation = "Webhook",
            Enable = true,
            OnHealthIssue = true,
            OnManualInteractionRequired = true,
            Settings = "{\"url\":\"http://test/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        var torrent = new Torrent { Id = 42, Name = "Error Torrent" };
        _handler.Handle(new TorrentStatusChangedEvent(torrent, TorrentStatus.Downloading, TorrentStatus.Error));

        await Task.WhenAny(signal.Task, Task.Delay(300));
        await Task.Delay(50);

        Assert.That(dispatchCount, Is.EqualTo(1), "Torrent error status event must dispatch exactly one notification");
    }

    [Test]
    public async Task Handle_HealthIssueEvent_without_torrent_dispatches_only_to_channels_matching_tags_or_untagged()
    {
        var dispatchedUrls = new List<string>();
        var lockObj = new object();
        var signal = new TaskCompletionSource<bool>();

        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(callInfo =>
            {
                lock (lockObj)
                {
                    dispatchedUrls.Add(callInfo.Arg<string>());
                }

                signal.TrySetResult(true);
            });

        var untaggedNotif = new NotificationDefinition
        {
            Id = 1,
            Name = "Untagged Notif",
            Implementation = "Webhook",
            Enable = true,
            OnHealthIssue = true,
            Tags = new List<int>(),
            Settings = "{\"url\":\"http://test/untagged\"}"
        };

        var taggedNotif = new NotificationDefinition
        {
            Id = 2,
            Name = "Tagged Notif",
            Implementation = "Webhook",
            Enable = true,
            OnHealthIssue = true,
            Tags = new List<int> { 50 },
            Settings = "{\"url\":\"http://test/tagged\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { untaggedNotif, taggedNotif });

        _handler.Handle(new HealthIssueEvent(null, "System", "Disk space critical", isResolved: false));

        await Task.WhenAny(signal.Task, Task.Delay(300));

        lock (lockObj)
        {
            Assert.That(dispatchedUrls, Contains.Item("http://test/untagged"));
            Assert.That(dispatchedUrls, Does.Not.Contain("http://test/tagged"));
        }
    }

    [Test]
    public async Task Dispatch_throttles_concurrency_at_configured_limit()
    {
        var currentConcurrency = 0;
        var maxObservedConcurrency = 0;
        var concurrencyLock = new object();

        using var handlerWithLimit = new NotificationEventHandler(
            _notificationRepository,
            _webhookDispatcher,
            _customScriptService,
            maxConcurrentDispatches: 2);

        var dispatchCount = 5;
        var completedTcs = new TaskCompletionSource<bool>();
        var completedCount = 0;

        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                lock (concurrencyLock)
                {
                    currentConcurrency++;
                    if (currentConcurrency > maxObservedConcurrency)
                    {
                        maxObservedConcurrency = currentConcurrency;
                    }
                }

                await Task.Delay(50);

                lock (concurrencyLock)
                {
                    currentConcurrency--;
                    completedCount++;
                    if (completedCount == dispatchCount)
                    {
                        completedTcs.TrySetResult(true);
                    }
                }

                return true;
            });

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Limit Notif",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Settings = "{\"url\":\"http://test/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        for (var i = 0; i < dispatchCount; i++)
        {
            handlerWithLimit.Handle(new TorrentAddedEvent(new Torrent { Id = i + 1, Name = $"Torrent {i}" }));
        }

        var completed = await Task.WhenAny(completedTcs.Task, Task.Delay(2000));
        Assert.That(completed, Is.EqualTo(completedTcs.Task), "All throttled dispatches should complete");
        Assert.That(maxObservedConcurrency, Is.LessThanOrEqualTo(2), "Observed concurrency should never exceed configured limit of 2");
    }

    [Test]
    public async Task Handle_BackupCreatedEvent_dispatches_when_OnBackupComplete_is_enabled()
    {
        var dispatchedSignal = new TaskCompletionSource<bool>();
        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => dispatchedSignal.TrySetResult(true));

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Backup Complete Notif",
            Implementation = "Webhook",
            Enable = true,
            OnBackupComplete = true,
            Settings = "{\"url\":\"http://test/backup-complete\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        _handler.Handle(new BackupCreatedEvent("/path/to/backup.zip", "backup.zip", BackupType.Scheduled, 1024));

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(3000));
        Assert.That(completed, Is.EqualTo(dispatchedSignal.Task), "Notification should be dispatched for BackupCreatedEvent");
    }

    [Test]
    public async Task Handle_BackupCreatedEvent_skips_when_OnBackupComplete_is_disabled()
    {
        var dispatchedSignal = new TaskCompletionSource<bool>();
        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => dispatchedSignal.TrySetResult(true));

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "No Backup Complete Notif",
            Implementation = "Webhook",
            Enable = true,
            OnBackupComplete = false,
            Settings = "{\"url\":\"http://test/backup-complete\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        _handler.Handle(new BackupCreatedEvent("/path/to/backup.zip", "backup.zip", BackupType.Manual, 1024));

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(150));
        Assert.That(completed, Is.Not.EqualTo(dispatchedSignal.Task), "Notification should not be dispatched when OnBackupComplete is false");
    }

    [Test]
    public async Task Handle_BackupFailedEvent_dispatches_when_OnBackupFailed_is_enabled()
    {
        var dispatchedSignal = new TaskCompletionSource<bool>();
        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => dispatchedSignal.TrySetResult(true));

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Backup Failed Notif",
            Implementation = "Webhook",
            Enable = true,
            OnBackupFailed = true,
            OnHealthIssue = false,
            Settings = "{\"url\":\"http://test/backup-failed\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        _handler.Handle(new BackupFailedEvent(BackupType.Scheduled, "Disk full"));

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(3000));
        Assert.That(completed, Is.EqualTo(dispatchedSignal.Task), "Notification should be dispatched for BackupFailedEvent");
    }

    [Test]
    public async Task Handle_BackupFailedEvent_dispatches_when_OnHealthIssue_is_enabled()
    {
        var dispatchedSignal = new TaskCompletionSource<bool>();
        _webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => dispatchedSignal.TrySetResult(true));

        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Health Issue Notif",
            Implementation = "Webhook",
            Enable = true,
            OnBackupFailed = false,
            OnHealthIssue = true,
            Settings = "{\"url\":\"http://test/health-issue\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notif });

        _handler.Handle(new BackupFailedEvent(BackupType.Manual, "Database locked"));

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(3000));
        Assert.That(completed, Is.EqualTo(dispatchedSignal.Task), "Notification should be dispatched for BackupFailedEvent via OnHealthIssue");
    }

    [Test]
    public async Task Dispatch_triggers_fallback_when_primary_dispatch_returns_false()
    {
        var fallbackSignal = new TaskCompletionSource<bool>();

        var primaryNotif = new NotificationDefinition
        {
            Id = 1,
            Name = "Primary Webhook",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Settings = "{\"url\":\"http://primary.local/hook\"}",
            FallbackNotificationId = 2
        };

        var fallbackNotif = new NotificationDefinition
        {
            Id = 2,
            Name = "Fallback Webhook",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Settings = "{\"url\":\"http://fallback.local/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { primaryNotif });
        _notificationRepository.Get(2).Returns(fallbackNotif);

        _webhookDispatcher.DispatchAsync(Arg.Is<string>(u => u.Contains("primary")), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        _webhookDispatcher.DispatchAsync(Arg.Is<string>(u => u.Contains("fallback")), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => fallbackSignal.TrySetResult(true));

        var torrent = new Torrent
        {
            Id = 10,
            Name = "Fallback Test Torrent",
        };

        _handler.Handle(new TorrentAddedEvent(torrent));

        var completed = await Task.WhenAny(fallbackSignal.Task, Task.Delay(500));
        Assert.That(completed, Is.EqualTo(fallbackSignal.Task), "Fallback notification should be dispatched when primary returns false");
    }

    [Test]
    public async Task Dispatch_triggers_fallback_when_primary_dispatch_throws()
    {
        var fallbackSignal = new TaskCompletionSource<bool>();

        var primaryNotif = new NotificationDefinition
        {
            Id = 10,
            Name = "Faulty Webhook",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Settings = "{\"url\":\"http://faulty.local/hook\"}",
            FallbackNotificationId = 20
        };

        var fallbackNotif = new NotificationDefinition
        {
            Id = 20,
            Name = "Fallback Webhook",
            Implementation = "Webhook",
            Enable = true,
            OnGrab = true,
            Settings = "{\"url\":\"http://fallback.local/hook\"}"
        };

        _notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { primaryNotif });
        _notificationRepository.Get(20).Returns(fallbackNotif);

        _webhookDispatcher.DispatchAsync(Arg.Is<string>(u => u.Contains("faulty")), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new System.Net.Http.HttpRequestException("Connection refused"));

        _webhookDispatcher.DispatchAsync(Arg.Is<string>(u => u.Contains("fallback")), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => fallbackSignal.TrySetResult(true));

        var torrent = new Torrent
        {
            Id = 10,
            Name = "Faulty Primary Torrent",
        };

        _handler.Handle(new TorrentAddedEvent(torrent));

        var completed = await Task.WhenAny(fallbackSignal.Task, Task.Delay(500));
        Assert.That(completed, Is.EqualTo(fallbackSignal.Task), "Fallback notification should be dispatched when primary throws exception");
    }
}
