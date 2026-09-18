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

    [TestCase("169.254.169.254", 80)]
    [TestCase("127.0.0.1", 8080)]
    [TestCase("10.0.0.1", 9091)]
    [TestCase("192.168.1.1", 8080)]
    public void TestDirect_with_unsafe_host_returns_bad_request(string host, int port)
    {
        var definition = new DownloadClientDefinition
        {
            Name = "SSRF Probe",
            ClientType = "QBitTorrent",
            Host = host,
            Port = port,
        };

        var result = _controller.TestDirect(definition);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Target host/URL is not permitted."));
        _downloadClientFactory.DidNotReceive().CreateClient(Arg.Any<DownloadClientDefinition>());
    }

    [Test]
    public void TestDirect_with_safe_host_invokes_factory_and_returns_result()
    {
        var definition = new DownloadClientDefinition
        {
            Name = "Safe Client",
            ClientType = "QBitTorrent",
            Host = "8.8.8.8",
            Port = 8080,
        };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.TestConnectionDetailed().Returns(DownloadClientTestResult.Ok("Connected successfully"));
        _downloadClientFactory.CreateClient(definition).Returns(mockClient);

        var result = _controller.TestDirect(definition);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result.Result;
        var testResult = (DownloadClientTestResult)ok.Value;
        Assert.That(testResult.Success, Is.True);
        Assert.That(testResult.Message, Is.EqualTo("Connected successfully"));
    }

    [Test]
    public void Update_preserves_new_password_when_password_contains_asterisk()
    {
        var existing = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Password = "OldPassword",
        };
        _downloadClientFactory.Get(1).Returns(existing);

        var updated = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Password = "Secret*Pass123",
        };

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _downloadClientFactory.Received().Update(Arg.Is<DownloadClientDefinition>(d => d.Password == "Secret*Pass123"));
    }

    [Test]
    public void Update_restores_existing_password_when_password_is_mask()
    {
        var existing = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Password = "OriginalPassword",
        };
        _downloadClientFactory.Get(1).Returns(existing);

        var updated = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Password = "********",
        };

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _downloadClientFactory.Received().Update(Arg.Is<DownloadClientDefinition>(d => d.Password == "OriginalPassword"));
    }

    [Test]
    public void Update_restores_existing_password_when_password_is_null_or_whitespace()
    {
        var existing = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Password = "OriginalPassword",
        };
        _downloadClientFactory.Get(1).Returns(existing);

        var updated = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Password = "   ",
        };

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _downloadClientFactory.Received().Update(Arg.Is<DownloadClientDefinition>(d => d.Password == "OriginalPassword"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("********")]
    public void TestDirect_restores_existing_password_when_password_is_empty_or_mask(string incomingPassword)
    {
        var existing = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
            Host = "8.8.8.8",
            Port = 8080,
            Password = "StoredSecretPassword",
        };
        _downloadClientFactory.Get(1).Returns(existing);

        var toTest = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
            Host = "8.8.8.8",
            Port = 8080,
            Password = incomingPassword,
        };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.TestConnectionDetailed().Returns(DownloadClientTestResult.Ok("Success"));
        _downloadClientFactory.CreateClient(Arg.Any<DownloadClientDefinition>()).Returns(mockClient);

        var result = _controller.TestDirect(toTest);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _downloadClientFactory.Received().CreateClient(Arg.Is<DownloadClientDefinition>(d => d.Password == "StoredSecretPassword"));
    }

    [Test]
    public void TestConnection_with_unsafe_host_returns_bad_request()
    {
        var definition = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Cloud Metadata Client",
            ClientType = "QBitTorrent",
            Host = "169.254.169.254",
            Port = 80,
        };
        _downloadClientFactory.Get(1).Returns(definition);

        var result = _controller.TestConnection(1);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Target host/URL is not permitted."));
        _downloadClientFactory.DidNotReceive().CreateClient(Arg.Any<DownloadClientDefinition>());
    }

    [TestCase("qbittorrent", "QBitTorrent", "QBitTorrentClient", "QBitTorrentSettings")]
    [TestCase("transmission", "Transmission", "TransmissionClient", "TransmissionSettings")]
    [TestCase("deluge", "Deluge", "DelugeClient", "DelugeSettings")]
    public void Create_should_normalize_client_type_and_implementation(string inputType, string expectedType, string expectedImpl, string expectedContract)
    {
        var def = new DownloadClientDefinition
        {
            Name = "Normalized Client",
            Host = "localhost",
            Port = 8080,
            ClientType = inputType,
        };

        _downloadClientFactory.Create(Arg.Any<DownloadClientDefinition>()).Returns(callInfo => callInfo.Arg<DownloadClientDefinition>());

        var result = _controller.Create(def);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)result.Result;
        var created = (DownloadClientDefinition)ok.Value;
        Assert.That(created.ClientType, Is.EqualTo(expectedType));
        Assert.That(created.Implementation, Is.EqualTo(expectedImpl));
        Assert.That(created.ConfigContract, Is.EqualTo(expectedContract));
    }

    [TestCase("qbittorrent")]
    [TestCase("qBittorrent")]
    [TestCase("transmission")]
    [TestCase("deluge")]
    public void TestDirect_should_delegate_to_factory_for_various_client_type_casings(string clientType)
    {
        var def = new DownloadClientDefinition
        {
            Name = "Direct Client",
            Host = "8.8.8.8",
            Port = 8080,
            ClientType = clientType,
        };

        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.TestConnectionDetailed().Returns(DownloadClientTestResult.Ok("Connected"));
        _downloadClientFactory.CreateClient(Arg.Is<DownloadClientDefinition>(d => d.ClientType == clientType)).Returns(mockClient);

        var result = _controller.TestDirect(def);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _downloadClientFactory.Received(1).CreateClient(Arg.Is<DownloadClientDefinition>(d => d.ClientType == clientType));
    }
}
