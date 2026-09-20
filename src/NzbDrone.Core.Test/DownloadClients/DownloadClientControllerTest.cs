using System.Collections.Generic;
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

    [Test]
    public void GetItems_returns_not_found_when_client_does_not_exist()
    {
        _syncService.GetClientItems(99).Returns(x => throw new System.ArgumentException("Client not found"));

        var result = _controller.GetItems(99);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void GetItems_returns_ok_when_sync_service_returns_items()
    {
        _syncService.GetClientItems(1).Returns(new List<DownloadClientRemoteItem>
        {
            new() { Title = "Torrent 1", InfoHash = "HASH1" },
            new() { Title = "Torrent 2", InfoHash = "HASH2" }
        });

        var result = _controller.GetItems(1);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var items = (List<DownloadClientRemoteItem>)okResult.Value;
        Assert.That(items, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetItems_returns_unauthorized_when_auth_fails()
    {
        _syncService.GetClientItems(1).Returns(x => throw new DownloadClientAuthenticationException("Invalid credentials"));

        var result = _controller.GetItems(1);

        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var objResult = (ObjectResult)result.Result;
        Assert.That(objResult.StatusCode, Is.EqualTo(401));
    }

    [Test]
    public void GetItems_returns_service_unavailable_when_client_unavailable()
    {
        _syncService.GetClientItems(1).Returns(x => throw new DownloadClientUnavailableException("Connection refused"));

        var result = _controller.GetItems(1);

        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var objResult = (ObjectResult)result.Result;
        Assert.That(objResult.StatusCode, Is.EqualTo(503));
    }

    [Test]
    public void GetItems_returns_service_unavailable_when_http_request_exception_thrown()
    {
        _syncService.GetClientItems(1).Returns(x => throw new System.Net.Http.HttpRequestException("Network timeout"));

        var result = _controller.GetItems(1);

        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var objResult = (ObjectResult)result.Result;
        Assert.That(objResult.StatusCode, Is.EqualTo(503));
    }

    [Test]
    public void GetAll_enriches_clients_with_sync_status()
    {
        var def = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent" };
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition> { def });

        var status = new DownloadClientStatus
        {
            IsOnline = true,
            Version = "4.6.0",
            LastSyncTime = new System.DateTime(2026, 1, 1, 12, 0, 0, System.DateTimeKind.Utc),
            LastErrorMessage = null,
            ConsecutiveFailures = 0,
            BackoffUntil = null
        };
        _syncService.GetClientStatus(1).Returns(status);

        var result = _controller.GetAll();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var clients = (List<DownloadClientDefinition>)okResult.Value;
        Assert.That(clients, Has.Count.EqualTo(1));
        Assert.That(clients[0].IsOnline, Is.True);
        Assert.That(clients[0].Version, Is.EqualTo("4.6.0"));
        Assert.That(clients[0].ConsecutiveFailures, Is.EqualTo(0));
    }

    [Test]
    public void Get_enriches_client_with_sync_status()
    {
        var def = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent" };
        _downloadClientFactory.Get(1).Returns(def);

        var status = new DownloadClientStatus
        {
            IsOnline = false,
            Version = null,
            LastSyncTime = null,
            LastErrorMessage = "Connection timed out",
            ConsecutiveFailures = 2,
            BackoffUntil = new System.DateTime(2026, 1, 1, 12, 5, 0, System.DateTimeKind.Utc)
        };
        _syncService.GetClientStatus(1).Returns(status);

        var result = _controller.Get(1);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var client = (DownloadClientDefinition)okResult.Value;
        Assert.That(client.IsOnline, Is.False);
        Assert.That(client.LastErrorMessage, Is.EqualTo("Connection timed out"));
        Assert.That(client.ConsecutiveFailures, Is.EqualTo(2));
        Assert.That(client.BackoffUntil, Is.Not.Null);
    }

    [Test]
    public void GetAllItems_returns_aggregated_items_from_sync_service()
    {
        var items = new List<DownloadClientRemoteItem>
        {
            new() { DownloadId = "1", Title = "Torrent 1", InfoHash = "hash1", ClientId = 1, ClientName = "qBittorrent" },
            new() { DownloadId = "2", Title = "Torrent 2", InfoHash = "hash2", ClientId = 2, ClientName = "Transmission" },
        };
        _syncService.GetAllClientItems().Returns(items);

        var result = _controller.GetAllItems();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var returnedItems = (List<DownloadClientRemoteItem>)okResult.Value;
        Assert.That(returnedItems, Has.Count.EqualTo(2));
        Assert.That(returnedItems[0].ClientName, Is.EqualTo("qBittorrent"));
        Assert.That(returnedItems[1].ClientName, Is.EqualTo("Transmission"));
    }

    [Test]
    public void PauseTorrent_returns_not_found_when_client_does_not_exist()
    {
        _downloadClientFactory.Get(999).Returns((DownloadClientDefinition)null);

        var result = _controller.PauseTorrent(999, "hash123");

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void PauseTorrent_returns_ok_when_client_pauses_successfully()
    {
        var def = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent" };
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.PauseTorrent("hash123").Returns(true);

        _downloadClientFactory.Get(1).Returns(def);
        _downloadClientFactory.CreateClient(def).Returns(mockClient);

        var result = _controller.PauseTorrent(1, "hash123");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public void PauseTorrent_returns_bad_request_when_client_pause_fails()
    {
        var def = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent" };
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.PauseTorrent("hash123").Returns(false);

        _downloadClientFactory.Get(1).Returns(def);
        _downloadClientFactory.CreateClient(def).Returns(mockClient);

        var result = _controller.PauseTorrent(1, "hash123");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void ResumeTorrent_returns_not_found_when_client_does_not_exist()
    {
        _downloadClientFactory.Get(999).Returns((DownloadClientDefinition)null);

        var result = _controller.ResumeTorrent(999, "hash123");

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void ResumeTorrent_returns_ok_when_client_resumes_successfully()
    {
        var def = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent" };
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.ResumeTorrent("hash123").Returns(true);

        _downloadClientFactory.Get(1).Returns(def);
        _downloadClientFactory.CreateClient(def).Returns(mockClient);

        var result = _controller.ResumeTorrent(1, "hash123");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public void ResumeTorrent_returns_bad_request_when_client_resume_fails()
    {
        var def = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent" };
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.ResumeTorrent("hash123").Returns(false);

        _downloadClientFactory.Get(1).Returns(def);
        _downloadClientFactory.CreateClient(def).Returns(mockClient);

        var result = _controller.ResumeTorrent(1, "hash123");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void DeleteTorrent_returns_not_found_when_client_does_not_exist()
    {
        _downloadClientFactory.Get(999).Returns((DownloadClientDefinition)null);

        var result = _controller.DeleteTorrent(999, "hash123", deleteData: true);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void DeleteTorrent_returns_ok_when_client_deletes_successfully()
    {
        var def = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent" };
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.DeleteTorrent("hash123", true).Returns(true);

        _downloadClientFactory.Get(1).Returns(def);
        _downloadClientFactory.CreateClient(def).Returns(mockClient);

        var result = _controller.DeleteTorrent(1, "hash123", deleteData: true);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public void DeleteTorrent_returns_bad_request_when_client_delete_fails()
    {
        var def = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", ClientType = "QBitTorrent" };
        var mockClient = Substitute.For<IDownloadClient>();
        mockClient.DeleteTorrent("hash123", false).Returns(false);

        _downloadClientFactory.Get(1).Returns(def);
        _downloadClientFactory.CreateClient(def).Returns(mockClient);

        var result = _controller.DeleteTorrent(1, "hash123", deleteData: false);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }
}
