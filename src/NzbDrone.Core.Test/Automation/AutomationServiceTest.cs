using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class AutomationServiceTest
{
    private IAutomationScriptRepository _scriptRepository;
    private ITorrentRepository _torrentRepository;
    private ITagService _tagService;
    private IEventAggregator _eventAggregator;
    private AutomationService _subject;

    [SetUp]
    public void SetUp()
    {
        _scriptRepository = Substitute.For<IAutomationScriptRepository>();
        _torrentRepository = Substitute.For<ITorrentRepository>();
        _tagService = Substitute.For<ITagService>();
        _eventAggregator = Substitute.For<IEventAggregator>();

        _subject = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagService,
            _eventAggregator);
    }

    [Test]
    public void TruncateExecutionLog_should_return_null_when_null()
    {
        Assert.That(AutomationService.TruncateExecutionLog(null), Is.Null);
    }

    [Test]
    public void TruncateExecutionLog_should_return_empty_when_empty()
    {
        Assert.That(AutomationService.TruncateExecutionLog(string.Empty), Is.EqualTo(string.Empty));
    }

    [Test]
    public void TruncateExecutionLog_should_return_original_when_within_limit()
    {
        var log = "Standard execution log line 1\nStandard execution log line 2";
        var result = AutomationService.TruncateExecutionLog(log);

        Assert.That(result, Is.EqualTo(log));
    }

    [Test]
    public void TruncateExecutionLog_should_truncate_to_max_chars_when_exceeding_limit()
    {
        var largeLog = new string('x', 60000);
        var result = AutomationService.TruncateExecutionLog(largeLog, AutomationService.MaxPersistedLogCharacters);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Length, Is.EqualTo(AutomationService.MaxPersistedLogCharacters));
        Assert.That(result, Does.Contain("... [OUTPUT TRUNCATED FOR DATABASE STORAGE] ..."));
    }

    [Test]
    public void ExecuteScript_should_truncate_LastExecutionLog_before_persisting()
    {
        var script = new AutomationScript
        {
            Id = 1,
            Name = "Noisy Script",
            Language = AutomationLanguage.JavaScript,
            Code = @"
for (var i = 0; i < 2000; i++) {
    console.log('Repeated log entry ' + i + ' padding with extra text 0123456789abcdef');
}
",
        };

        var result = _subject.ExecuteScript(script);

        Assert.That(result.Success, Is.True);
        Assert.That(script.LastExecutionLog, Is.Not.Null);
        Assert.That(script.LastExecutionLog!.Length, Is.LessThanOrEqualTo(AutomationService.MaxPersistedLogCharacters));
        _scriptRepository.Received(1).Update(Arg.Is<AutomationScript>(s =>
            s.Id == 1 &&
            s.LastExecutionLog != null &&
            s.LastExecutionLog.Length <= AutomationService.MaxPersistedLogCharacters));
    }

    [Test]
    public void ExecuteScript_with_tags_mutation_updates_torrent_TagIds_and_Label()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "My Torrent",
            TagIds = new List<int> { 1 },
            Label = "Action",
        };

        var script = new AutomationScript
        {
            Id = 1,
            Name = "Tag Script",
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.addTag('Comedy');",
        };

        _tagService.SyncTagsFromLabels(Arg.Is<IEnumerable<string>>(labels => labels.Contains("Comedy")))
            .Returns(new List<int> { 2 });
        _tagService.GetLabelsForTagIds(Arg.Is<IEnumerable<int>>(ids => ids.Contains(1) && ids.Contains(2)))
            .Returns(new List<string> { "Action", "Comedy" });

        var result = _subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(torrent.TagIds, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(torrent.Label, Is.EqualTo("Action, Comedy"));
        _torrentRepository.Received(1).Update(torrent);
    }

    [Test]
    public void ExecuteScript_should_abort_when_recursion_depth_limit_exceeded()
    {
        var torrent = new Torrent { Id = 10, Name = "Recursive Torrent" };
        var script2 = new AutomationScript { Id = 2, Name = "Script 2", Language = AutomationLanguage.Yaml, Code = "steps:\n  - name: S2\n    actions:\n      - pause: true\n" };
        var script3 = new AutomationScript { Id = 3, Name = "Script 3", Language = AutomationLanguage.Yaml, Code = "steps:\n  - name: S3\n    actions:\n      - pause: true\n" };
        var script4 = new AutomationScript { Id = 4, Name = "Script 4", Language = AutomationLanguage.Yaml, Code = "steps:\n  - name: S4\n    actions:\n      - pause: true\n" };

        AutomationExecutionResult level4Result = null;

        _eventAggregator.When(e => e.PublishEvent(Arg.Any<TorrentPausedEvent>())).Do(_ =>
        {
            if (AutomationService.CurrentExecutionDepth == 1)
            {
                _subject.ExecuteScript(script2, torrent);
            }
            else if (AutomationService.CurrentExecutionDepth == 2)
            {
                _subject.ExecuteScript(script3, torrent);
            }
            else if (AutomationService.CurrentExecutionDepth == 3)
            {
                level4Result = _subject.ExecuteScript(script4, torrent);
            }
        });

        var pausingScript = new AutomationScript
        {
            Id = 1,
            Name = "Pauser",
            Language = AutomationLanguage.Yaml,
            Code = "steps:\n  - name: Pause\n    actions:\n      - pause: true\n",
        };

        var result = _subject.ExecuteScript(pausingScript, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(level4Result, Is.Not.Null);
        Assert.That(level4Result.Success, Is.False);
        Assert.That(level4Result.Error, Does.Contain("Recursion depth limit"));
    }

    [Test]
    public void ExecuteScript_should_abort_when_reentrancy_detected_for_same_entity()
    {
        var torrent = new Torrent { Id = 20, Name = "Reentrant Torrent" };
        var script = new AutomationScript
        {
            Id = 42,
            Name = "Self Triggering Script",
            Language = AutomationLanguage.Yaml,
            Code = "steps:\n  - name: Pause\n    actions:\n      - pause: true\n",
        };

        AutomationExecutionResult reentrantResult = null;

        _eventAggregator.When(e => e.PublishEvent(Arg.Any<TorrentPausedEvent>())).Do(_ =>
        {
            // Simulate AutomationEventService re-dispatching the exact same script on the same torrent
            reentrantResult = _subject.ExecuteScript(script, torrent);
        });

        var result = _subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(reentrantResult, Is.Not.Null);
        Assert.That(reentrantResult.Success, Is.False);
        Assert.That(reentrantResult.Error, Does.Contain("Reentrancy detected"));
    }

    [Test]
    public void ExecuteScript_should_trigger_archive_extraction_when_script_requests_it()
    {
        var extractor = Substitute.For<IArchiveExtractorService>();
        var subject = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagService,
            _eventAggregator,
            archiveExtractorService: extractor);

        var torrent = new Torrent { Id = 123, Name = "Scene.Release" };
        var script = new AutomationScript
        {
            Id = 99,
            Name = "Extractor Script",
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.extractArchive('/dest', true);",
        };

        var result = subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ShouldExtractArchive, Is.True);
        Assert.That(result.ExtractDestination, Is.EqualTo("/dest"));
        Assert.That(result.DeleteArchiveOnExtract, Is.True);

        Task.Delay(100).Wait();
        extractor.Received(1).ExtractTorrentArchiveAsync(torrent, "/dest", true);
    }

    [Test]
    public void ExecuteScript_should_recheck_torrent_and_publish_status_changed_event()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "Recheck Torrent",
            Status = TorrentStatus.Downloading,
            Progress = 0.85,
        };

        var script = new AutomationScript
        {
            Id = 1,
            Name = "Recheck Script",
            Language = AutomationLanguage.Yaml,
            Code = "steps:\n  - name: Recheck\n    actions:\n      - recheck: true\n",
        };

        var result = _subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Checking));
        Assert.That(torrent.Progress, Is.EqualTo(0));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStatusChangedEvent>(e => e.Torrent == torrent && e.NewStatus == TorrentStatus.Checking));
        _torrentRepository.Received(1).Update(torrent);
    }

    [Test]
    public void ExecuteScript_should_remove_matching_tracker_from_torrent()
    {
        var torrent = new Torrent
        {
            Id = 11,
            Name = "Tracker Remove Torrent",
            TrackerUrl = "http://tracker.dead.org:6969/announce",
        };

        var script = new AutomationScript
        {
            Id = 2,
            Name = "Remove Tracker Script",
            Language = AutomationLanguage.Yaml,
            Code = "steps:\n  - name: Remove Tracker\n    actions:\n      - removeTracker: 'tracker.dead.org'\n",
        };

        var result = _subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(torrent.TrackerUrl, Is.Empty);
        _torrentRepository.Received(1).Update(torrent);
    }

    [Test]
    public void ExecuteScript_should_dispatch_direct_webhooks()
    {
        var webhookDispatcher = Substitute.For<IWebhookDispatcher>();
        var subject = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagService,
            _eventAggregator,
            webhookDispatcher: webhookDispatcher);

        var torrent = new Torrent { Id = 12, Name = "Webhook Torrent" };
        var script = new AutomationScript
        {
            Id = 3,
            Name = "Webhook Script",
            Language = AutomationLanguage.Yaml,
            Code = "steps:\n" +
                "  - name: Send Webhooks\n" +
                "    actions:\n" +
                "      - sendDiscordWebhook:\n" +
                "          url: 'https://discord.com/api/webhooks/123/token'\n" +
                "          title: 'Discord Title'\n" +
                "      - sendTelegramMessage:\n" +
                "          token: '12345:bottoken'\n" +
                "          chatId: '987654'\n" +
                "          message: 'Telegram Message'\n" +
                "      - sendNtfy:\n" +
                "          server: 'https://ntfy.sh'\n" +
                "          topic: 'my-topic'\n" +
                "          message: 'Ntfy Message'\n" +
                "      - sendPushover:\n" +
                "          token: 'apptoken'\n" +
                "          user: 'userkey'\n" +
                "          message: 'Pushover Message'\n",
        };

        var result = subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        Task.Delay(100).Wait();

        webhookDispatcher.Received(1).DispatchAsync(
            Arg.Is<string>(u => u == "https://discord.com/api/webhooks/123/token"),
            Arg.Any<object>(),
            Arg.Any<string>(),
            Arg.Any<System.Threading.CancellationToken>());

        webhookDispatcher.Received(1).DispatchAsync(
            Arg.Is<string>(u => u == "https://api.telegram.org/bot12345:bottoken/sendMessage"),
            Arg.Any<object>(),
            Arg.Any<string>(),
            Arg.Any<System.Threading.CancellationToken>());

        webhookDispatcher.Received(1).DispatchAsync(
            Arg.Is<string>(u => u == "https://ntfy.sh/my-topic"),
            Arg.Any<object>(),
            Arg.Any<string>(),
            Arg.Any<System.Threading.CancellationToken>());

        webhookDispatcher.Received(1).DispatchAsync(
            Arg.Is<string>(u => u == "https://api.pushover.net/1/messages.json"),
            Arg.Any<object>(),
            Arg.Any<string>(),
            Arg.Any<System.Threading.CancellationToken>());
    }

    [Test]
    public void ExecuteScript_should_boost_tracker_when_requested()
    {
        var trackerBoost = Substitute.For<ITrackerBoostService>();
        var subject = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagService,
            _eventAggregator,
            trackerBoostService: trackerBoost);

        var torrent = new Torrent { Id = 15, Name = "Boost Torrent" };
        var script = new AutomationScript
        {
            Id = 4,
            Name = "Boost Script",
            Language = AutomationLanguage.Yaml,
            Code = "steps:\n  - name: Boost\n    actions:\n      - boostTracker: true\n",
        };

        var result = subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        Task.Delay(100).Wait();
        trackerBoost.Received(1).BoostTorrentAsync(15);
    }

    [Test]
    public void ExecuteScript_should_reannounce_when_requested()
    {
        var trackerAnnounce = Substitute.For<ITrackerAnnounceService>();
        var trackerBoost = Substitute.For<ITrackerBoostService>();
        var subject = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagService,
            _eventAggregator,
            trackerAnnounceService: trackerAnnounce,
            trackerBoostService: trackerBoost);

        var torrent = new Torrent { Id = 16, Name = "Reannounce Torrent", InfoHash = "abc123hash" };
        var script = new AutomationScript
        {
            Id = 5,
            Name = "Reannounce Script",
            Language = AutomationLanguage.Yaml,
            Code = "steps:\n  - name: Reannounce\n    actions:\n      - reannounce: true\n",
        };

        var result = subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        Task.Delay(100).Wait();
        trackerAnnounce.Received(1).AnnounceTorrent(torrent, force: true);
        trackerBoost.Received(1).ReannounceDownloadClients("abc123hash");
    }

    [Test]
    public void ExecuteScript_should_ban_peer_and_publish_event()
    {
        var torrent = new Torrent { Id = 17, Name = "Ban Peer Torrent", InfoHash = "peerhash" };
        var script = new AutomationScript
        {
            Id = 6,
            Name = "Ban Peer Script",
            Language = AutomationLanguage.Yaml,
            Code = "steps:\n  - name: Ban\n    actions:\n      - banPeer: '192.168.1.100'\n",
        };

        var result = _subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<PeerBannedEvent>(e => e.PeerIp == "192.168.1.100" && e.InfoHash == "peerhash"));
    }

    [Test]
    public void ExecuteScript_should_ban_peer_disconnect_active_connections_and_publish_event()
    {
        var connectionManager = Substitute.For<IConnectionManager>();
        var subject = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagService,
            _eventAggregator,
            connectionManager: connectionManager);

        var activeConnection = new PeerConnection(new MemoryStream(), "192.168.1.100", 6881);
        connectionManager.GetAllConnections().Returns(new List<PeerConnection> { activeConnection });

        var torrent = new Torrent { Id = 17, Name = "Ban Peer Torrent", InfoHash = "peerhash" };
        var script = new AutomationScript
        {
            Id = 6,
            Name = "Ban Peer Script",
            Language = AutomationLanguage.Yaml,
            Code = "steps:\n  - name: Ban\n    actions:\n      - banPeer: '192.168.1.100'\n",
        };

        var result = subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        connectionManager.Received(1).BanPeer("192.168.1.100", Arg.Any<TimeSpan?>());
        connectionManager.Received(1).Remove(activeConnection);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<PeerBannedEvent>(e => e.PeerIp == "192.168.1.100" && e.InfoHash == "peerhash"));
    }

    [Test]
    public void ExecuteScript_should_route_torrent_removal_through_torrent_service_with_DeleteDataOnRemove_true()
    {
        var torrentService = Substitute.For<ITorrentService>();
        var subject = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagService,
            _eventAggregator,
            torrentService: torrentService);

        var torrent = new Torrent { Id = 42, Name = "Remove Torrent" };
        var script = new AutomationScript
        {
            Id = 10,
            Name = "Remove With Data Script",
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.remove(true);",
        };

        var result = subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ShouldRemove, Is.True);
        Assert.That(result.DeleteDataOnRemove, Is.True);
        torrentService.Received(1).Delete(42, true);
    }

    [Test]
    public void ExecuteScript_should_route_torrent_removal_through_torrent_service_with_DeleteDataOnRemove_false()
    {
        var torrentService = Substitute.For<ITorrentService>();
        var subject = new AutomationService(
            _scriptRepository,
            _torrentRepository,
            _tagService,
            _eventAggregator,
            torrentService: torrentService);

        var torrent = new Torrent { Id = 42, Name = "Remove Torrent" };
        var script = new AutomationScript
        {
            Id = 11,
            Name = "Remove Without Data Script",
            Language = AutomationLanguage.JavaScript,
            Code = "torrent.remove(false);",
        };

        var result = subject.ExecuteScript(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ShouldRemove, Is.True);
        Assert.That(result.DeleteDataOnRemove, Is.False);
        torrentService.Received(1).Delete(42, false);
    }
}
