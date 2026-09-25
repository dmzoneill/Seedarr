using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Dht;

namespace NzbDrone.Core.Test.Dht;

[TestFixture]
public class DhtStateServiceTests
{
    private string _tempDir;
    private string _dhtFilePath;
    private IAppFolderInfo _appFolderInfo;
    private DhtStateService _subject;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_dht_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dhtFilePath = Path.Combine(_tempDir, "dht.dat");

        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempDir);

        _subject = new DhtStateService(_appFolderInfo, _dhtFilePath);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best-effort test cleanup
        }
    }

    [Test]
    public void SaveRoutingTable_and_LoadRoutingTable_should_persist_and_restore_nodes()
    {
        var nodeId1 = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        var nodeId2 = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);

        var nodes = new List<DhtNode>
        {
            new()
            {
                NodeId = nodeId1,
                EndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 6881),
                LastSeen = DateTime.UtcNow,
                FailCount = 0
            },
            new()
            {
                NodeId = nodeId2,
                EndPoint = new IPEndPoint(IPAddress.Parse("10.0.0.50"), 8999),
                LastSeen = DateTime.UtcNow,
                FailCount = 0
            }
        };

        _subject.SaveRoutingTable(nodes);

        Assert.That(File.Exists(_dhtFilePath), Is.True);

        var loaded = _subject.LoadRoutingTable();

        Assert.That(loaded, Has.Count.EqualTo(2));
        Assert.That(loaded[0].NodeId, Is.EqualTo(nodeId1));
        Assert.That(loaded[0].EndPoint.Address.ToString(), Is.EqualTo("192.168.1.100"));
        Assert.That(loaded[0].EndPoint.Port, Is.EqualTo(6881));
        Assert.That(loaded[1].NodeId, Is.EqualTo(nodeId2));
        Assert.That(loaded[1].EndPoint.Address.ToString(), Is.EqualTo("10.0.0.50"));
        Assert.That(loaded[1].EndPoint.Port, Is.EqualTo(8999));
    }

    [Test]
    public void SaveRoutingTable_with_Node_models_should_persist_and_load_correctly()
    {
        var nodeId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        var hexNodeId = Convert.ToHexString(nodeId);

        var nodes = new List<Node>
        {
            new(hexNodeId, "1.2.3.4", 5678)
        };

        _subject.SaveRoutingTable(nodes);

        var loaded = _subject.LoadRoutingTableNodes();

        Assert.That(loaded, Has.Count.EqualTo(1));
        Assert.That(loaded[0].NodeId, Is.EqualTo(hexNodeId));
        Assert.That(loaded[0].Ip, Is.EqualTo("1.2.3.4"));
        Assert.That(loaded[0].Port, Is.EqualTo(5678));
    }

    [Test]
    public void LoadRoutingTable_should_return_empty_list_when_file_does_not_exist()
    {
        var nonExistentPath = Path.Combine(_tempDir, "does_not_exist.dat");
        var service = new DhtStateService(_appFolderInfo, nonExistentPath);

        var loaded = service.LoadRoutingTable();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded, Is.Empty);
    }

    [Test]
    public void LoadRoutingTable_should_handle_corrupted_file_gracefully()
    {
        File.WriteAllText(_dhtFilePath, "{ this is not valid json ][!@#$%^&*() }");

        var loaded = _subject.LoadRoutingTable();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded, Is.Empty);
    }

    [Test]
    public void LoadRoutingTable_should_handle_empty_file_gracefully()
    {
        File.WriteAllBytes(_dhtFilePath, Array.Empty<byte>());

        var loaded = _subject.LoadRoutingTable();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded, Is.Empty);
    }

    [Test]
    public void LoadRoutingTable_should_handle_binary_compact_nodes()
    {
        var nodeId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        var compact = new byte[26];
        Array.Copy(nodeId, 0, compact, 0, 20);
        compact[20] = 8;
        compact[21] = 8;
        compact[22] = 8;
        compact[23] = 8;
        compact[24] = 0x1A;
        compact[25] = 0xE1; // port 6881

        File.WriteAllBytes(_dhtFilePath, compact);

        var loaded = _subject.LoadRoutingTable();

        Assert.That(loaded, Has.Count.EqualTo(1));
        Assert.That(loaded[0].NodeId, Is.EqualTo(nodeId));
        Assert.That(loaded[0].EndPoint.Address.ToString(), Is.EqualTo("8.8.8.8"));
        Assert.That(loaded[0].EndPoint.Port, Is.EqualTo(6881));
    }

    [Test]
    public void Restoring_nodes_into_DHT_routing_table_populates_table()
    {
        var localNodeId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        var routingTable = new RoutingTable(localNodeId, allowLocal: true);

        var nodeId1 = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        var nodeId2 = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);

        var nodesToSave = new List<DhtNode>
        {
            new()
            {
                NodeId = nodeId1,
                EndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881),
                LastSeen = DateTime.UtcNow,
                FailCount = 0
            },
            new()
            {
                NodeId = nodeId2,
                EndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.2"), 6882),
                LastSeen = DateTime.UtcNow,
                FailCount = 0
            }
        };

        _subject.SaveRoutingTable(nodesToSave);

        var restoredNodes = _subject.LoadRoutingTable();
        foreach (var node in restoredNodes)
        {
            routingTable.AddNode(node);
        }

        Assert.That(routingTable.NodeCount, Is.EqualTo(2));

        var closest = routingTable.GetClosestNodes(nodeId1, 1);
        Assert.That(closest, Has.Count.EqualTo(1));
        Assert.That(closest[0].NodeId, Is.EqualTo(nodeId1));
    }

    [Test]
    public void DhtService_LoadRoutingTableState_restores_cached_nodes()
    {
        var configService = Substitute.For<IConfigService>();
        configService.DhtBucketSize.Returns(8);
        configService.DhtRoutingTableSize.Returns(160);
        configService.DhtMaxNodes.Returns(1000);
        configService.DhtConcurrentQueries.Returns(3);
        configService.EnableDht.Returns(false);

        var stateService = Substitute.For<IDhtStateService>();
        var cachedNodeId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        var cachedNodes = new List<DhtNode>
        {
            new()
            {
                NodeId = cachedNodeId,
                EndPoint = new IPEndPoint(IPAddress.Parse("8.8.4.4"), 6881),
                LastSeen = DateTime.UtcNow,
                FailCount = 0
            }
        };

        stateService.LoadRoutingTable().Returns(cachedNodes);

        using var service = new DhtService(configService, dhtStateService: stateService);
        service.RoutingTable.AllowLocal = true;
        service.LoadRoutingTableState();

        Assert.That(service.RoutingTable.NodeCount, Is.EqualTo(1));
        var closest = service.RoutingTable.GetClosestNodes(cachedNodeId, 1);
        Assert.That(closest[0].NodeId, Is.EqualTo(cachedNodeId));
    }

    [Test]
    public void DhtService_SaveRoutingTableState_invokes_state_service()
    {
        var configService = Substitute.For<IConfigService>();
        configService.DhtBucketSize.Returns(8);
        configService.DhtRoutingTableSize.Returns(160);
        configService.DhtMaxNodes.Returns(1000);
        configService.DhtConcurrentQueries.Returns(3);
        configService.EnableDht.Returns(false);

        var stateService = Substitute.For<IDhtStateService>();

        using var service = new DhtService(configService, dhtStateService: stateService);
        service.RoutingTable.AllowLocal = true;
        service.RoutingTable.AddNode(new DhtNode
        {
            NodeId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20),
            EndPoint = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 6881),
            LastSeen = DateTime.UtcNow,
            FailCount = 0
        });

        service.SaveRoutingTableState();

        stateService.Received(1).SaveRoutingTable(Arg.Is<IEnumerable<DhtNode>>(list => list != null));
    }

    [Test]
    public void GetBootstrapNodes_returns_default_routers_when_no_custom_nodes_configured()
    {
        var configService = Substitute.For<IConfigService>();
        configService.DhtBucketSize.Returns(8);
        configService.DhtRoutingTableSize.Returns(160);
        configService.DhtMaxNodes.Returns(1000);
        configService.DhtConcurrentQueries.Returns(3);
        configService.DhtBootstrapNodes.Returns((string)null);

        using var service = new DhtService(configService);

        var nodes = new List<string>(service.GetBootstrapNodes());

        Assert.That(nodes, Has.Count.EqualTo(5));
        Assert.That(nodes, Contains.Item("router.bittorrent.com:6881"));
        Assert.That(nodes, Contains.Item("router.utorrent.com:6881"));
        Assert.That(nodes, Contains.Item("dht.transmissionbt.com:6881"));
        Assert.That(nodes, Contains.Item("dht.aelitis.com:6881"));
        Assert.That(nodes, Contains.Item("dht.libtorrent.org:25401"));
    }

    [Test]
    public void GetBootstrapNodes_includes_user_configured_nodes_and_default_fallback()
    {
        var configService = Substitute.For<IConfigService>();
        configService.DhtBucketSize.Returns(8);
        configService.DhtRoutingTableSize.Returns(160);
        configService.DhtMaxNodes.Returns(1000);
        configService.DhtConcurrentQueries.Returns(3);
        configService.DhtBootstrapNodes.Returns("custom.router.org:6881, another.router.org:25401");

        using var service = new DhtService(configService);

        var nodes = new List<string>(service.GetBootstrapNodes());

        Assert.That(nodes, Contains.Item("custom.router.org:6881"));
        Assert.That(nodes, Contains.Item("another.router.org:25401"));
        Assert.That(nodes, Contains.Item("router.bittorrent.com:6881"));
        Assert.That(nodes.Count, Is.EqualTo(7));
    }
}
