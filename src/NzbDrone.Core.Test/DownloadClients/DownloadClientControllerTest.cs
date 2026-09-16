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
            Password = "OldPassword",
        };
        _downloadClientFactory.Get(1).Returns(existing);

        var updated = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
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
            Password = "OriginalPassword",
        };
        _downloadClientFactory.Get(1).Returns(existing);

        var updated = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
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
            Password = "OriginalPassword",
        };
        _downloadClientFactory.Get(1).Returns(existing);

        var updated = new DownloadClientDefinition
        {
            Id = 1,
            Name = "Existing Client",
            ClientType = "QBitTorrent",
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
}
