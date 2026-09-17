using System;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.DownloadClients.Deluge;
using NzbDrone.Core.DownloadClients.QBitTorrent;
using NzbDrone.Core.DownloadClients.Transmission;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Test.DownloadClients;

[TestFixture]
public class DownloadClientFactoryTest
{
    private IDownloadClientRepository _repo;
    private IServiceFactory _serviceFactory;
    private IRemotePathMappingService _remotePathMappingService;
    private DownloadClientFactory _factory;

    [SetUp]
    public void SetUp()
    {
        _repo = Substitute.For<IDownloadClientRepository>();
        _serviceFactory = Substitute.For<IServiceFactory>();
        _remotePathMappingService = Substitute.For<IRemotePathMappingService>();

        _factory = new DownloadClientFactory(_repo, _serviceFactory, _remotePathMappingService);
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public void CreateClient_should_return_null_when_definition_is_null()
    {
        Assert.That(_factory.CreateClient(null), Is.Null);
    }

    [Test]
    public void CreateClient_should_return_null_for_unknown_client_type()
    {
        var def = new DownloadClientDefinition
        {
            Id = 1,
            ClientType = "UnknownClient",
            Host = "localhost",
            Port = 1234,
        };

        Assert.That(_factory.CreateClient(def), Is.Null);
    }

    [Test]
    public void CreateClient_should_reuse_cached_instance_for_same_persisted_definition()
    {
        var def = new DownloadClientDefinition
        {
            Id = 1,
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Username = "admin",
            Password = "adminadmin",
        };

        var client1 = _factory.CreateClient(def);
        var client2 = _factory.CreateClient(def);

        Assert.That(client1, Is.Not.Null);
        Assert.That(client2, Is.SameAs(client1));
    }

    [Test]
    public void CreateClient_should_reuse_cached_instance_for_identical_unpersisted_definition()
    {
        var def1 = new DownloadClientDefinition
        {
            Id = 0,
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Username = "admin",
            Password = "adminadmin",
        };

        var def2 = new DownloadClientDefinition
        {
            Id = 0,
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Username = "admin",
            Password = "adminadmin",
        };

        var client1 = _factory.CreateClient(def1);
        var client2 = _factory.CreateClient(def2);

        Assert.That(client1, Is.Not.Null);
        Assert.That(client2, Is.SameAs(client1));
    }

    [Test]
    public void CreateClient_should_recreate_instance_when_persisted_definition_configuration_changes()
    {
        var def = new DownloadClientDefinition
        {
            Id = 1,
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Username = "admin",
            Password = "adminadmin",
        };

        var client1 = _factory.CreateClient(def);

        var changedDef = new DownloadClientDefinition
        {
            Id = 1,
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 9090,
            Username = "admin",
            Password = "adminadmin",
        };

        var client2 = _factory.CreateClient(changedDef);

        Assert.That(client1, Is.Not.Null);
        Assert.That(client2, Is.Not.Null);
        Assert.That(client2, Is.Not.SameAs(client1));
    }

    [Test]
    public void Update_should_invalidate_cached_instance()
    {
        var def = new DownloadClientDefinition
        {
            Id = 1,
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Username = "admin",
            Password = "adminadmin",
        };

        var client1 = _factory.CreateClient(def);
        _factory.Update(def);
        var client2 = _factory.CreateClient(def);

        Assert.That(client2, Is.Not.SameAs(client1));
    }

    [Test]
    public void Delete_should_invalidate_cached_instance()
    {
        var def = new DownloadClientDefinition
        {
            Id = 1,
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
            Username = "admin",
            Password = "adminadmin",
        };

        var client1 = _factory.CreateClient(def);
        _factory.Delete(def.Id);
        var client2 = _factory.CreateClient(def);

        Assert.That(client2, Is.Not.SameAs(client1));
    }

    [Test]
    public void CreateClient_should_set_remote_path_mapping_service_on_transmission_and_deluge()
    {
        var transmissionDef = new DownloadClientDefinition
        {
            Id = 10,
            ClientType = "Transmission",
            Host = "remote-transmission",
            Port = 9091,
        };

        var delugeDef = new DownloadClientDefinition
        {
            Id = 11,
            ClientType = "Deluge",
            Host = "remote-deluge",
            Port = 8112,
        };

        var transmissionClient = _factory.CreateClient(transmissionDef) as TransmissionClient;
        var delugeClient = _factory.CreateClient(delugeDef) as DelugeClient;

        Assert.That(transmissionClient, Is.Not.Null);
        Assert.That(transmissionClient.RemotePathMappingService, Is.SameAs(_remotePathMappingService));

        Assert.That(delugeClient, Is.Not.Null);
        Assert.That(delugeClient.RemotePathMappingService, Is.SameAs(_remotePathMappingService));
    }

    [Test]
    public void ClearCache_should_clear_all_cached_instances()
    {
        var def1 = new DownloadClientDefinition
        {
            Id = 1,
            ClientType = "QBitTorrent",
            Host = "localhost",
            Port = 8080,
        };

        var def2 = new DownloadClientDefinition
        {
            Id = 0,
            ClientType = "Transmission",
            Host = "localhost",
            Port = 9091,
        };

        var client1 = _factory.CreateClient(def1);
        var client2 = _factory.CreateClient(def2);

        _factory.ClearCache();

        var client1Again = _factory.CreateClient(def1);
        var client2Again = _factory.CreateClient(def2);

        Assert.That(client1Again, Is.Not.SameAs(client1));
        Assert.That(client2Again, Is.Not.SameAs(client2));
    }
}
