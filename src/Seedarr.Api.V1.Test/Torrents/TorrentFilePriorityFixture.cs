using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Torrents;

namespace Seedarr.Api.V1.Test.Torrents;

[TestFixture]
public class TorrentFilePriorityFixture
{
    private ITorrentFileService _torrentFileService;
    private TorrentController _controller;

    [SetUp]
    public void SetUp()
    {
        _torrentFileService = Substitute.For<ITorrentFileService>();

        _controller = new TorrentController(
            Substitute.For<ITorrentService>(),
            _torrentFileService,
            Substitute.For<ITrackerEntryService>(),
            Substitute.For<ITorrentImportService>(),
            Substitute.For<IConnectionManager>(),
            Substitute.For<ITorrentEventLogService>(),
            Substitute.For<IConfigService>(),
            Substitute.For<IBroadcastSignalRMessage>(),
            new TorrentResourceValidator())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }

    [Test]
    public void SetFilePriorities_returns_Ok_when_all_files_updated()
    {
        const int torrentId = 42;
        var request = new SetFilePrioritiesRequest
        {
            Files = new List<SetFilePriorityItem>
            {
                new() { FileId = 1, Priority = 7 },
                new() { FileId = 2, Priority = 0 },
            },
        };

        _torrentFileService
            .SetPriorities(torrentId, Arg.Any<IEnumerable<(int FileId, int Priority)>>())
            .Returns(true);

        var result = _controller.SetFilePriorities(torrentId, request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result;
        Assert.That(ok.Value, Is.Not.Null);
        dynamic body = ok.Value!;
        Assert.That(body.success, Is.True);
    }

    [Test]
    public void SetFilePriorities_returns_NotFound_when_any_file_is_invalid()
    {
        const int torrentId = 42;
        var request = new SetFilePrioritiesRequest
        {
            Files = new List<SetFilePriorityItem>
            {
                new() { FileId = 1, Priority = 7 },
                new() { FileId = 9999, Priority = 1 },
            },
        };

        _torrentFileService
            .SetPriorities(torrentId, Arg.Any<IEnumerable<(int FileId, int Priority)>>())
            .Returns(false);

        var result = _controller.SetFilePriorities(torrentId, request);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void SetFilePriorities_returns_BadRequest_when_request_is_empty()
    {
        var result = _controller.SetFilePriorities(1, new SetFilePrioritiesRequest());

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        _torrentFileService.DidNotReceive().SetPriorities(Arg.Any<int>(), Arg.Any<IEnumerable<(int, int)>>());
    }
}
