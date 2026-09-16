using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.DownloadClients.Sync;
using Seedarr.Api.V1.DownloadClients;

namespace NzbDrone.Core.Test.DownloadClients;

[TestFixture]
public class DownloadClientControllerTest
{
    private IDownloadClientFactory _downloadClientFactory;
    private IDownloadClientSyncService _syncService;
    private DownloadClientController _controller;

    [SetUp]
    public void SetUp()
    {
        _downloadClientFactory = Substitute.For<IDownloadClientFactory>();
        _syncService = Substitute.For<IDownloadClientSyncService>();
        _controller = new DownloadClientController(_downloadClientFactory, _syncService);
    }

    [Test]
    public void Create_with_null_body_returns_bad_request()
    {
        var result = _controller.Create(null);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Create_with_empty_name_returns_bad_request(string name)
    {
        var def = new DownloadClientDefinition
        {
            Name = name,
            Host = "localhost",
            Port = 8080,
            ClientType = "QBitTorrent"
        };

        var result = _controller.Create(def);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Name is required"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Create_with_empty_host_returns_bad_request(string host)
    {
        var def = new DownloadClientDefinition
        {
            Name = "Valid Client",
            Host = host,
            Port = 8080,
            ClientType = "QBitTorrent"
        };

        var result = _controller.Create(def);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Host is required"));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    [TestCase(100000)]
    public void Create_with_invalid_port_returns_bad_request(int port)
    {
        var def = new DownloadClientDefinition
        {
            Name = "Valid Client",
            Host = "localhost",
            Port = port,
            ClientType = "QBitTorrent"
        };

        var result = _controller.Create(def);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Port must be between 1 and 65535"));
    }

    [Test]
    public void Create_with_valid_definition_returns_ok()
    {
        var def = new DownloadClientDefinition
        {
            Name = "My Client",
            Host = "localhost",
            Port = 8080,
            ClientType = "QBitTorrent",
            Password = "secret"
        };

        _downloadClientFactory.Create(def).Returns(callInfo =>
        {
            var created = callInfo.Arg<DownloadClientDefinition>();
            created.Id = 10;
            return created;
        });

        var result = _controller.Create(def);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var value = (DownloadClientDefinition)okResult.Value;
        Assert.That(value.Id, Is.EqualTo(10));
        Assert.That(value.Password, Is.EqualTo("********"));
    }

    [Test]
    public void Update_with_null_body_returns_bad_request()
    {
        var result = _controller.Update(1, null);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Update_with_empty_name_returns_bad_request(string name)
    {
        var def = new DownloadClientDefinition
        {
            Name = name,
            Host = "localhost",
            Port = 8080,
            ClientType = "QBitTorrent"
        };

        var result = _controller.Update(1, def);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result;
        Assert.That(badRequest.Value, Is.EqualTo("Name is required"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Update_with_empty_host_returns_bad_request(string host)
    {
        var def = new DownloadClientDefinition
        {
            Name = "Valid Client",
            Host = host,
            Port = 8080,
            ClientType = "QBitTorrent"
        };

        var result = _controller.Update(1, def);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result;
        Assert.That(badRequest.Value, Is.EqualTo("Host is required"));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    public void Update_with_invalid_port_returns_bad_request(int port)
    {
        var def = new DownloadClientDefinition
        {
            Name = "Valid Client",
            Host = "localhost",
            Port = port,
            ClientType = "QBitTorrent"
        };

        var result = _controller.Update(1, def);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result;
        Assert.That(badRequest.Value, Is.EqualTo("Port must be between 1 and 65535"));
    }

    [Test]
    public void Update_with_nonexistent_id_returns_not_found()
    {
        var def = new DownloadClientDefinition
        {
            Name = "Valid Client",
            Host = "localhost",
            Port = 8080,
            ClientType = "QBitTorrent"
        };

        _downloadClientFactory.Get(999).Returns((DownloadClientDefinition)null);

        var result = _controller.Update(999, def);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Update_with_valid_definition_returns_ok()
    {
        var existing = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Old Client",
            Host = "localhost",
            Port = 8080,
            ClientType = "QBitTorrent",
            Password = "existingPassword"
        };

        var updated = new DownloadClientDefinition
        {
            Id = 1,
            Name = "New Client",
            Host = "127.0.0.1",
            Port = 8085,
            ClientType = "QBitTorrent",
            Password = "********"
        };

        _downloadClientFactory.Get(1).Returns(existing);

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _downloadClientFactory.Received(1).Update(Arg.Is<DownloadClientDefinition>(d => d.Password == "existingPassword" && d.Name == "New Client"));
    }
}
