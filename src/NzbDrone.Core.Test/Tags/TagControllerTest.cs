using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Tags;

namespace NzbDrone.Core.Test.Tags;

[TestFixture]
public class TagControllerTest
{
    private ITagService _tagService;
    private ITorrentService _torrentService;
    private IBroadcastSignalRMessage _signalRBroadcaster;
    private TagResourceValidator _validator;
    private TagController _controller;

    [SetUp]
    public void SetUp()
    {
        _tagService = Substitute.For<ITagService>();
        _torrentService = Substitute.For<ITorrentService>();
        _signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        _validator = new TagResourceValidator();
        _controller = new TagController(_tagService, _validator, _signalRBroadcaster, _torrentService);
    }

    [Test]
    public void GetAll_calculates_torrent_counts_server_side()
    {
        var tag1 = new Tag { Id = 1, Label = "Tag1", Color = "#ff0000" };
        var tag2 = new Tag { Id = 2, Label = "Tag2", Color = "#00ff00" };
        _tagService.GetAll().Returns(new List<Tag> { tag1, tag2 });

        var torrent1 = new Torrent { Id = 10, TagIds = new List<int> { 1, 2 } };
        var torrent2 = new Torrent { Id = 20, TagIds = new List<int> { 1 } };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = _controller.GetAll();

        Assert.That(result.Value, Has.Count.EqualTo(2));
        var res1 = result.Value.Find(t => t.Id == 1);
        var res2 = result.Value.Find(t => t.Id == 2);

        Assert.That(res1, Is.Not.Null);
        Assert.That(res1.TorrentCount, Is.EqualTo(2));
        Assert.That(res1.Color, Is.EqualTo("#ff0000"));

        Assert.That(res2, Is.Not.Null);
        Assert.That(res2.TorrentCount, Is.EqualTo(1));
        Assert.That(res2.Color, Is.EqualTo("#00ff00"));
    }

    [Test]
    public void Get_by_id_calculates_torrent_count_server_side()
    {
        var tag = new Tag { Id = 1, Label = "TestTag", Color = "#123456", MinSeedRatio = 2.0 };
        _tagService.Get(1).Returns(tag);

        var torrent1 = new Torrent { Id = 10, TagIds = new List<int> { 1 } };
        var torrent2 = new Torrent { Id = 20, TagIds = new List<int> { 2 } };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent1, torrent2 });

        var result = _controller.Get(1);

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.Id, Is.EqualTo(1));
        Assert.That(result.Value.TorrentCount, Is.EqualTo(1));
        Assert.That(result.Value.Color, Is.EqualTo("#123456"));
        Assert.That(result.Value.MinSeedRatio, Is.EqualTo(2.0));
    }

    [Test]
    public void BulkAssign_assigns_tags_to_torrents()
    {
        var torrent1 = new Torrent { Id = 10, TagIds = new List<int>() };
        var torrent2 = new Torrent { Id = 20, TagIds = new List<int> { 1 } };

        _torrentService.Get(10).Returns(torrent1);
        _torrentService.Get(20).Returns(torrent2);

        var request = new BulkTagRequest
        {
            TagIds = new List<int> { 1, 2 },
            TorrentIds = new List<int> { 10, 20 }
        };

        var result = _controller.BulkAssign(request);

        Assert.That(result, Is.InstanceOf<OkResult>());
        Assert.That(torrent1.TagIds, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(torrent2.TagIds, Is.EquivalentTo(new[] { 1, 2 }));

        _torrentService.Received(1).UpdateUserFields(torrent1);
        _torrentService.Received(1).UpdateUserFields(torrent2);
    }

    [Test]
    public void BulkAssign_does_not_update_if_tags_already_assigned()
    {
        var torrent = new Torrent { Id = 10, TagIds = new List<int> { 1, 2 } };
        _torrentService.Get(10).Returns(torrent);

        var request = new BulkTagRequest
        {
            TagIds = new List<int> { 1 },
            TorrentIds = new List<int> { 10 }
        };

        var result = _controller.BulkAssign(request);

        Assert.That(result, Is.InstanceOf<OkResult>());
        _torrentService.DidNotReceive().UpdateUserFields(torrent);
    }

    [Test]
    public void BulkAssign_returns_bad_request_on_null_input()
    {
        var result1 = _controller.BulkAssign(null);
        var result2 = _controller.BulkAssign(new BulkTagRequest { TagIds = null, TorrentIds = new List<int> { 1 } });

        Assert.That(result1, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(result2, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void BulkRemove_removes_specified_tags_from_torrents()
    {
        var torrent1 = new Torrent { Id = 10, TagIds = new List<int> { 1, 2, 3 } };
        var torrent2 = new Torrent { Id = 20, TagIds = new List<int> { 2, 4 } };

        _torrentService.Get(10).Returns(torrent1);
        _torrentService.Get(20).Returns(torrent2);

        var request = new BulkTagRequest
        {
            TagIds = new List<int> { 2, 3 },
            TorrentIds = new List<int> { 10, 20 }
        };

        var result = _controller.BulkRemove(request);

        Assert.That(result, Is.InstanceOf<OkResult>());
        Assert.That(torrent1.TagIds, Is.EquivalentTo(new[] { 1 }));
        Assert.That(torrent2.TagIds, Is.EquivalentTo(new[] { 4 }));

        _torrentService.Received(1).UpdateUserFields(torrent1);
        _torrentService.Received(1).UpdateUserFields(torrent2);
    }

    [Test]
    public void BulkRemove_does_not_update_if_no_tags_removed()
    {
        var torrent = new Torrent { Id = 10, TagIds = new List<int> { 1 } };
        _torrentService.Get(10).Returns(torrent);

        var request = new BulkTagRequest
        {
            TagIds = new List<int> { 5 },
            TorrentIds = new List<int> { 10 }
        };

        var result = _controller.BulkRemove(request);

        Assert.That(result, Is.InstanceOf<OkResult>());
        _torrentService.DidNotReceive().UpdateUserFields(torrent);
    }

    [Test]
    public void BulkRemove_returns_bad_request_on_null_input()
    {
        var result1 = _controller.BulkRemove(null);
        var result2 = _controller.BulkRemove(new BulkTagRequest { TagIds = new List<int> { 1 }, TorrentIds = null });

        Assert.That(result1, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(result2, Is.InstanceOf<BadRequestObjectResult>());
    }
}
