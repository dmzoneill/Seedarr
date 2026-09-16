using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentEventLogServiceTest
{
    private ITorrentEventLogRepository _repository;
    private TorrentEventLogService _subject;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<ITorrentEventLogRepository>();
        _subject = new TorrentEventLogService(_repository);
    }

    [TearDown]
    public void TearDown()
    {
        _subject?.Dispose();
    }

    [Test]
    public async Task Channel_enqueuing_and_batch_draining_flushes_to_repository()
    {
        _subject.Debug(1, "Tracker", "Debug event");
        _subject.Info(1, "Tracker", "Info event");
        _subject.Warn(1, "Tracker", "Warn event");
        _subject.Error(1, "Tracker", "Error event");

        await _subject.FlushAsync();

        _repository.Received(1).InsertMany(Arg.Is<IEnumerable<TorrentEventLog>>(logs =>
            logs.Count() == 4 &&
            logs.Any(l => l.Level == "Debug" && l.Message == "Debug event") &&
            logs.Any(l => l.Level == "Info" && l.Message == "Info event") &&
            logs.Any(l => l.Level == "Warn" && l.Message == "Warn event") &&
            logs.Any(l => l.Level == "Error" && l.Message == "Error event")));
    }

    [Test]
    public async Task Batch_draining_chunks_large_volume_into_batches_of_100()
    {
        var capturedBatches = new List<List<TorrentEventLog>>();
        _repository.When(r => r.InsertMany(Arg.Any<IEnumerable<TorrentEventLog>>()))
            .Do(callInfo =>
            {
                var batch = callInfo.Arg<IEnumerable<TorrentEventLog>>().ToList();
                capturedBatches.Add(batch);
            });

        for (var i = 1; i <= 150; i++)
        {
            _subject.Info(1, "Loop", $"Message {i}");
        }

        await _subject.FlushAsync();

        Assert.That(capturedBatches.Sum(b => b.Count), Is.EqualTo(150));
        Assert.That(capturedBatches.All(b => b.Count <= 100), Is.True);
    }

    [Test]
    public void Graceful_flush_on_disposal_persists_pending_logs()
    {
        var capturedBatches = new List<List<TorrentEventLog>>();
        _repository.When(r => r.InsertMany(Arg.Any<IEnumerable<TorrentEventLog>>()))
            .Do(callInfo =>
            {
                capturedBatches.Add(callInfo.Arg<IEnumerable<TorrentEventLog>>().ToList());
            });

        for (var i = 1; i <= 5; i++)
        {
            _subject.Info(1, "Worker", $"Pending {i}");
        }

        _subject.Dispose();

        Assert.That(capturedBatches.Sum(b => b.Count), Is.EqualTo(5));
    }

    [Test]
    public async Task Graceful_flush_on_async_disposal_persists_pending_logs()
    {
        var capturedBatches = new List<List<TorrentEventLog>>();
        _repository.When(r => r.InsertMany(Arg.Any<IEnumerable<TorrentEventLog>>()))
            .Do(callInfo =>
            {
                capturedBatches.Add(callInfo.Arg<IEnumerable<TorrentEventLog>>().ToList());
            });

        for (var i = 1; i <= 10; i++)
        {
            _subject.Info(2, "Worker", $"Async pending {i}");
        }

        await _subject.DisposeAsync();

        Assert.That(capturedBatches.Sum(b => b.Count), Is.EqualTo(10));
    }

    [Test]
    public void Purge_delegates_to_repository_with_default_retention()
    {
        var date = DateTime.UtcNow.AddDays(-7);
        _subject.Purge(date);

        _repository.Received(1).Purge(date, 1000);
    }

    [Test]
    public void Purge_delegates_to_repository_with_custom_retention()
    {
        var date = DateTime.UtcNow.AddDays(-7);
        _subject.Purge(date, 500);

        _repository.Received(1).Purge(date, 500);
    }

    [Test]
    public void GetByTorrentId_delegates_to_repository()
    {
        var expected = new List<TorrentEventLog>
        {
            new() { Id = 1, TorrentId = 10, Level = "Info", Message = "Log 1" }
        };
        _repository.GetByTorrentId(10, 50).Returns(expected);

        var result = _subject.GetByTorrentId(10, 50);

        Assert.That(result, Is.SameAs(expected));
        _repository.Received(1).GetByTorrentId(10, 50);
    }

    [Test]
    public async Task Invalid_inputs_are_ignored_and_not_enqueued()
    {
        _subject.Info(0, "System", "Invalid torrent ID");
        _subject.Info(-1, "System", "Negative torrent ID");
        _subject.Info(1, "System", null);
        _subject.Info(1, "System", "");

        await _subject.FlushAsync();

        _repository.DidNotReceive().InsertMany(Arg.Any<IEnumerable<TorrentEventLog>>());
    }
}
