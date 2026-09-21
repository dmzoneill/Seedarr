using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class AutomationEventServiceTest
{
    private IAutomationService _automationService;
    private AutomationEventService _subject;

    [SetUp]
    public void SetUp()
    {
        _automationService = Substitute.For<IAutomationService>();
        _subject = new AutomationEventService(_automationService);
    }

    [Test]
    public void Handle_TorrentStallResolvedEvent_should_dispatch_TorrentStallResolved_trigger()
    {
        var script = new AutomationScript
        {
            Id = 1,
            Name = "On Stall Resolved",
            Trigger = AutomationTrigger.TorrentStallResolved,
            IsEnabled = true
        };

        _automationService.GetAll()
            .Returns(new List<AutomationScript> { script });

        var torrent = new Torrent
        {
            Id = 42,
            Name = "Sample Torrent",
            Status = TorrentStatus.Downloading
        };

        _subject.Handle(new TorrentStallResolvedEvent(torrent));

        _automationService.Received(1).ExecuteScript(script, torrent);
    }

    [Test]
    public void Handle_MediaEnrichedEvent_should_resolve_torrent_from_repository_and_dispatch_MediaEnriched_trigger()
    {
        var torrentRepository = Substitute.For<ITorrentRepository>();
        var subject = new AutomationEventService(_automationService, torrentRepository);

        var script = new AutomationScript
        {
            Id = 5,
            Name = "On Media Enriched",
            Trigger = AutomationTrigger.MediaEnriched,
            IsEnabled = true,
        };

        _automationService.GetAll()
            .Returns(new List<AutomationScript> { script });

        var torrent = new Torrent
        {
            Id = 10,
            Name = "Media.Torrent.1080p",
            Status = TorrentStatus.Downloading,
        };

        torrentRepository.Get(10).Returns(torrent);

        subject.Handle(new MediaEnrichedEvent { TorrentId = 10 });

        torrentRepository.Received(1).Get(10);
        _automationService.Received(1).ExecuteScript(script, torrent);
    }

    [Test]
    public void Handle_PeerBannedEvent_should_resolve_torrent_and_dispatch_PeerBanned_trigger()
    {
        var torrentRepository = Substitute.For<ITorrentRepository>();
        var subject = new AutomationEventService(_automationService, torrentRepository);

        var script = new AutomationScript
        {
            Id = 7,
            Name = "On Peer Banned",
            Trigger = AutomationTrigger.PeerBanned,
            IsEnabled = true,
        };

        _automationService.GetAll()
            .Returns(new List<AutomationScript> { script });

        var torrent = new Torrent
        {
            Id = 25,
            InfoHash = "peerbanhash",
            Name = "Peer Ban Torrent",
        };

        torrentRepository.FindByInfoHash("peerbanhash").Returns(torrent);

        subject.Handle(new PeerBannedEvent("192.168.1.100", "Automation test", "peerbanhash"));

        torrentRepository.Received(1).FindByInfoHash("peerbanhash");
        _automationService.Received(1).ExecuteScript(script, torrent);
    }

    [Test]
    public void Handle_PeerBannedEvent_without_torrent_should_dispatch_PeerBanned_trigger_with_null()
    {
        var script = new AutomationScript
        {
            Id = 8,
            Name = "On Peer Banned Global",
            Trigger = AutomationTrigger.PeerBanned,
            IsEnabled = true,
        };

        _automationService.GetAll()
            .Returns(new List<AutomationScript> { script });

        _subject.Handle(new PeerBannedEvent("192.168.1.100", "Automation test"));

        _automationService.Received(1).ExecuteScript(script, null);
    }

    [Test]
    public void DispatchTrigger_should_query_scriptRepository_GetByTrigger_and_not_call_GetAll()
    {
        var scriptRepo = Substitute.For<IAutomationScriptRepository>();
        var subject = new AutomationEventService(_automationService, scriptRepository: scriptRepo);

        var script = new AutomationScript
        {
            Id = 1,
            Name = "On Download Completed",
            Trigger = AutomationTrigger.TorrentCompleted,
            IsEnabled = true
        };

        scriptRepo.GetByTrigger(AutomationTrigger.TorrentCompleted)
            .Returns(new List<AutomationScript> { script });

        var torrent = new Torrent { Id = 10, Name = "Test Torrent" };

        subject.Handle(new TorrentDownloadCompletedEvent(torrent));

        scriptRepo.Received(1).GetByTrigger(AutomationTrigger.TorrentCompleted);
        _automationService.DidNotReceive().GetAll();
        _automationService.Received(1).ExecuteScript(script, torrent);
    }

    [Test]
    public void DispatchTrigger_should_filter_scripts_by_target_categories()
    {
        var scriptRepo = Substitute.For<IAutomationScriptRepository>();
        var subject = new AutomationEventService(_automationService, scriptRepository: scriptRepo);

        var matchingScript = new AutomationScript
        {
            Id = 1,
            Name = "Matches Category",
            Trigger = AutomationTrigger.TorrentAdded,
            IsEnabled = true,
            TargetCategories = new List<string> { "Movies", "Anime" }
        };

        var nonMatchingScript = new AutomationScript
        {
            Id = 2,
            Name = "Different Category",
            Trigger = AutomationTrigger.TorrentAdded,
            IsEnabled = true,
            TargetCategories = new List<string> { "TV" }
        };

        scriptRepo.GetByTrigger(AutomationTrigger.TorrentAdded)
            .Returns(new List<AutomationScript> { matchingScript, nonMatchingScript });

        var torrent = new Torrent { Id = 10, Name = "Test Torrent", Category = "movies" };

        subject.Handle(new TorrentAddedEvent(torrent));

        _automationService.Received(1).ExecuteScript(matchingScript, torrent);
        _automationService.DidNotReceive().ExecuteScript(nonMatchingScript, torrent);
    }

    [Test]
    public void DispatchTrigger_should_filter_scripts_by_target_tags()
    {
        var scriptRepo = Substitute.For<IAutomationScriptRepository>();
        var subject = new AutomationEventService(_automationService, scriptRepository: scriptRepo);

        var matchingScript = new AutomationScript
        {
            Id = 1,
            Name = "Matches Tag",
            Trigger = AutomationTrigger.TorrentAdded,
            IsEnabled = true,
            TargetTagIds = new List<int> { 5, 10 }
        };

        var nonMatchingScript = new AutomationScript
        {
            Id = 2,
            Name = "Different Tag",
            Trigger = AutomationTrigger.TorrentAdded,
            IsEnabled = true,
            TargetTagIds = new List<int> { 20 }
        };

        scriptRepo.GetByTrigger(AutomationTrigger.TorrentAdded)
            .Returns(new List<AutomationScript> { matchingScript, nonMatchingScript });

        var torrent = new Torrent { Id = 10, Name = "Test Torrent", TagIds = new List<int> { 5, 99 } };

        subject.Handle(new TorrentAddedEvent(torrent));

        _automationService.Received(1).ExecuteScript(matchingScript, torrent);
        _automationService.DidNotReceive().ExecuteScript(nonMatchingScript, torrent);
    }
}
