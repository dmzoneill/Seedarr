using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
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
}
