using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TrackerEntryServiceTest
{
    private ITrackerEntryRepository _repository;
    private TrackerEntryService _subject;

    [SetUp]
    public void Setup()
    {
        _repository = Substitute.For<ITrackerEntryRepository>();
        _subject = new TrackerEntryService(_repository);
    }

    [Test]
    public void GetByTorrentId_should_delegate_to_repository()
    {
        var expected = new List<TrackerEntry>
        {
            new TrackerEntry { Id = 1, TorrentId = 42, Url = "http://tracker.example.com/announce" }
        };
        _repository.GetByTorrentId(42).Returns(expected);

        var result = _subject.GetByTorrentId(42);

        Assert.That(result, Is.SameAs(expected));
        _repository.Received(1).GetByTorrentId(42);
    }

    [Test]
    public void GetByTorrentId_should_return_empty_list_when_none_found()
    {
        _repository.GetByTorrentId(99).Returns(new List<TrackerEntry>());

        var result = _subject.GetByTorrentId(99);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Add_should_call_repository_insert()
    {
        var entry = new TrackerEntry { TorrentId = 1, Url = "http://tracker.example.com/announce" };
        _repository.Insert(entry).Returns(entry);

        _subject.Add(entry);

        _repository.Received(1).Insert(entry);
    }

    [Test]
    public void Add_should_return_result_from_repository()
    {
        var entry = new TrackerEntry { TorrentId = 1, Url = "http://tracker.example.com/announce" };
        var inserted = new TrackerEntry { Id = 10, TorrentId = 1, Url = "http://tracker.example.com/announce" };
        _repository.Insert(entry).Returns(inserted);

        var result = _subject.Add(entry);

        Assert.That(result, Is.SameAs(inserted));
    }

    [Test]
    public void Update_should_delegate_to_repository()
    {
        var entry = new TrackerEntry { Id = 5, TorrentId = 1, Url = "http://tracker.example.com/announce" };
        _repository.Update(entry).Returns(entry);

        var result = _subject.Update(entry);

        Assert.That(result, Is.SameAs(entry));
        _repository.Received(1).Update(entry);
    }

    [Test]
    public void Delete_should_delegate_to_repository()
    {
        _subject.Delete(7);

        _repository.Received(1).Delete(7);
    }

    [Test]
    public void DeleteByTorrentId_should_delegate_to_repository()
    {
        _subject.DeleteByTorrentId(42);

        _repository.Received(1).DeleteByTorrentId(42);
    }

    [Test]
    public void Add_should_not_call_delete()
    {
        var entry = new TrackerEntry { TorrentId = 1, Url = "http://tracker.example.com/announce" };
        _repository.Insert(entry).Returns(entry);

        _subject.Add(entry);

        _repository.DidNotReceive().Delete(Arg.Any<int>());
        _repository.DidNotReceive().DeleteByTorrentId(Arg.Any<int>());
    }

    [Test]
    public void Update_should_not_call_insert_or_delete()
    {
        var entry = new TrackerEntry { Id = 5, TorrentId = 1, Url = "http://tracker.example.com/announce" };
        _repository.Update(entry).Returns(entry);

        _subject.Update(entry);

        _repository.DidNotReceive().Insert(Arg.Any<TrackerEntry>());
        _repository.DidNotReceive().Delete(Arg.Any<int>());
    }
}
