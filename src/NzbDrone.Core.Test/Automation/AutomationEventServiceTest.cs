using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
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
}
