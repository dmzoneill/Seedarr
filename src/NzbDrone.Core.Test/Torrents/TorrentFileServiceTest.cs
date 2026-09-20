using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentFileServiceTest
{
    private ITorrentFileRepository _repository;
    private TorrentFileService _subject;

    [SetUp]
    public void Setup()
    {
        _repository = Substitute.For<ITorrentFileRepository>();
        _subject = new TorrentFileService(_repository);
    }

    [Test]
    public void GetByTorrentId_should_delegate_to_repository()
    {
        var expected = new List<TorrentFile>
        {
            new TorrentFile { Id = 1, TorrentId = 42, Path = "sample.mkv", Size = 1024 }
        };
        _repository.GetByTorrentId(42).Returns(expected);

        var result = _subject.GetByTorrentId(42);

        Assert.That(result, Is.SameAs(expected));
        _repository.Received(1).GetByTorrentId(42);
    }

    [Test]
    public void GetByTorrentId_should_return_empty_list_when_none_found()
    {
        _repository.GetByTorrentId(99).Returns(new List<TorrentFile>());

        var result = _subject.GetByTorrentId(99);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Add_should_call_repository_insert()
    {
        var file = new TorrentFile { TorrentId = 1, Path = "video.mp4", Size = 2048 };
        _repository.Insert(file).Returns(file);

        var result = _subject.Add(file);

        Assert.That(result, Is.SameAs(file));
        _repository.Received(1).Insert(file);
    }

    [Test]
    public void AddMany_should_delegate_to_repository()
    {
        var files = new List<TorrentFile>
        {
            new() { TorrentId = 1, Path = "file1.txt", Size = 100 },
            new() { TorrentId = 1, Path = "file2.txt", Size = 200 }
        };

        _subject.AddMany(files);

        _repository.Received(1).InsertMany(files);
    }

    [Test]
    public void AddMany_should_handle_empty_or_null_gracefully()
    {
        Assert.DoesNotThrow(() => _subject.AddMany((IList<TorrentFile>)null));
        Assert.DoesNotThrow(() => _subject.AddMany((IEnumerable<TorrentFile>)null));
        Assert.DoesNotThrow(() => _subject.AddMany(new List<TorrentFile>()));
        Assert.DoesNotThrow(() => _subject.AddMany(Array.Empty<TorrentFile>()));

        _repository.DidNotReceive().InsertMany(Arg.Any<IList<TorrentFile>>());
    }

    [Test]
    public void DeleteByTorrentId_should_delegate_to_repository()
    {
        _subject.DeleteByTorrentId(42);

        _repository.Received(1).DeleteByTorrentId(42);
    }

    [Test]
    public void Update_should_delegate_to_repository()
    {
        var file = new TorrentFile { Id = 5, TorrentId = 1, Path = "updated.mkv", Size = 4096 };

        _subject.Update(file);

        _repository.Received(1).Update(file);
    }
}
