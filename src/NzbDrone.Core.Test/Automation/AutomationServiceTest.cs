using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

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
}
