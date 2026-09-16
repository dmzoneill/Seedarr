using System;
using System.Linq;
using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Dht;

namespace NzbDrone.Core.Test.Dht;

[TestFixture]
public class RoutingTableTest
{
    private byte[] _localNodeId;

    [SetUp]
    public void Setup()
    {
        _localNodeId = new byte[20];
    }

    [Test]
    public void Constructor_should_throw_when_localNodeId_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new RoutingTable(null));
    }

    [Test]
    public void NodeCount_should_be_zero_for_new_table()
    {
        var table = new RoutingTable(_localNodeId);

        Assert.That(table.NodeCount, Is.EqualTo(0));
    }

    [Test]
    public void AddNode_should_add_node_to_table()
    {
        var table = new RoutingTable(_localNodeId);
        var nodeId = new byte[20];
        nodeId[0] = 0x80;

        table.AddNode(CreateNode(nodeId));

        Assert.That(table.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void AddNode_should_update_existing_node_last_seen()
    {
        var table = new RoutingTable(_localNodeId);
        var nodeId = new byte[20];
        nodeId[0] = 0x80;

        var node = CreateNode(nodeId);
        node.LastSeen = DateTime.UtcNow.AddMinutes(-10);
        table.AddNode(node);

        var before = node.LastSeen;
        table.AddNode(CreateNode(nodeId));

        Assert.That(table.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void AddNode_should_reset_fail_count_on_existing_node()
    {
        var table = new RoutingTable(_localNodeId);
        var nodeId = new byte[20];
        nodeId[0] = 0x80;

        var node = CreateNode(nodeId);
        node.FailCount = 5;
        table.AddNode(node);

        table.AddNode(CreateNode(nodeId));

        Assert.That(node.FailCount, Is.EqualTo(0));
    }

    [Test]
    public void AddNode_should_respect_max_nodes_cap()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8, maxNodes: 2);

        for (var i = 1; i <= 3; i++)
        {
            var nodeId = new byte[20];
            nodeId[0] = (byte)(i * 0x10);
            table.AddNode(CreateNode(nodeId));
        }

        Assert.That(table.NodeCount, Is.EqualTo(2));
    }

    [Test]
    public void AddNode_should_not_exceed_bucket_size()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 2);
        var baseId = new byte[20];
        baseId[0] = 0x80;

        for (var i = 0; i < 3; i++)
        {
            var nodeId = (byte[])baseId.Clone();
            nodeId[19] = (byte)i;
            table.AddNode(CreateNode(nodeId));
        }

        Assert.That(table.NodeCount, Is.LessThanOrEqualTo(3));
    }

    [Test]
    public void AddNode_should_evict_bad_node_when_bucket_full()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 2);

        var nodeId1 = new byte[20];
        nodeId1[0] = 0x80;
        nodeId1[19] = 1;
        var badNode = CreateNode(nodeId1);
        badNode.FailCount = 5;
        table.AddNode(badNode);

        var nodeId2 = new byte[20];
        nodeId2[0] = 0x80;
        nodeId2[19] = 2;
        table.AddNode(CreateNode(nodeId2));

        var nodeId3 = new byte[20];
        nodeId3[0] = 0x80;
        nodeId3[19] = 3;
        table.AddNode(CreateNode(nodeId3));

        Assert.That(table.NodeCount, Is.EqualTo(2));
    }

    [Test]
    public void AddNode_should_not_add_when_bucket_full_and_all_good()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 2);

        var nodeId1 = new byte[20];
        nodeId1[0] = 0x80;
        nodeId1[19] = 1;
        table.AddNode(CreateNode(nodeId1));

        var nodeId2 = new byte[20];
        nodeId2[0] = 0x80;
        nodeId2[19] = 2;
        table.AddNode(CreateNode(nodeId2));

        var nodeId3 = new byte[20];
        nodeId3[0] = 0x80;
        nodeId3[19] = 3;
        table.AddNode(CreateNode(nodeId3));

        Assert.That(table.NodeCount, Is.EqualTo(2));
    }

    [Test]
    public void GetClosestNodes_should_return_nodes_ordered_by_xor_distance()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);
        var target = new byte[20];
        target[0] = 0x01;

        var farId = new byte[20];
        farId[0] = 0xFF;
        table.AddNode(CreateNode(farId));

        var closeId = new byte[20];
        closeId[0] = 0x02;
        table.AddNode(CreateNode(closeId));

        var closest = table.GetClosestNodes(target, 2);

        Assert.That(closest.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(closest[0].NodeId[0], Is.EqualTo(0x02));
    }

    [Test]
    public void GetClosestNodes_should_filter_out_bad_nodes()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);
        var target = new byte[20];
        target[0] = 0x01;

        var goodId = new byte[20];
        goodId[0] = 0x80;
        table.AddNode(CreateNode(goodId));

        var badId = new byte[20];
        badId[0] = 0x40;
        var badNode = CreateNode(badId);
        badNode.FailCount = 5;
        table.AddNode(badNode);

        var closest = table.GetClosestNodes(target);

        Assert.That(closest.All(n => n.IsGood), Is.True);
    }

    [Test]
    public void GetClosestNodes_should_default_to_bucket_size_count()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 3);

        for (var i = 1; i <= 5; i++)
        {
            var nodeId = new byte[20];
            nodeId[0] = (byte)(i * 0x10);
            table.AddNode(CreateNode(nodeId));
        }

        var closest = table.GetClosestNodes(new byte[20]);

        Assert.That(closest.Count, Is.LessThanOrEqualTo(3));
    }

    [Test]
    public void GetClosestNodes_should_return_empty_for_empty_table()
    {
        var table = new RoutingTable(_localNodeId);

        var closest = table.GetClosestNodes(new byte[20]);

        Assert.That(closest, Is.Empty);
    }

    [Test]
    public void GetClosestNodes_should_respect_custom_count()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);

        for (var i = 1; i <= 5; i++)
        {
            var nodeId = new byte[20];
            nodeId[0] = (byte)(i * 0x10);
            table.AddNode(CreateNode(nodeId));
        }

        var closest = table.GetClosestNodes(new byte[20], 2);

        Assert.That(closest.Count, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public void AddNode_should_place_identical_node_id_in_bucket_zero()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);

        table.AddNode(CreateNode(new byte[20]));

        Assert.That(table.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void AddNode_should_distribute_nodes_across_buckets()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 1);

        var nodeId1 = new byte[20];
        nodeId1[0] = 0x80;
        table.AddNode(CreateNode(nodeId1));

        var nodeId2 = new byte[20];
        nodeId2[0] = 0x40;
        table.AddNode(CreateNode(nodeId2));

        Assert.That(table.NodeCount, Is.EqualTo(2));
    }

    [Test]
    public void GetClosestNodes_should_exclude_stale_nodes()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);

        var nodeId = new byte[20];
        nodeId[0] = 0x80;
        var staleNode = CreateNode(nodeId);
        staleNode.LastSeen = DateTime.UtcNow.AddMinutes(-20);
        table.AddNode(staleNode);

        var closest = table.GetClosestNodes(new byte[20]);

        Assert.That(closest, Is.Empty);
    }

    [Test]
    public void AddNode_should_reject_duplicate_ip_with_different_node_id_in_same_bucket()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);

        var nodeId1 = new byte[20];
        nodeId1[0] = 0x80;
        nodeId1[19] = 1;
        var node1 = new DhtNode
        {
            NodeId = nodeId1,
            EndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.1"), 6881),
            LastSeen = DateTime.UtcNow
        };
        table.AddNode(node1);

        var nodeId2 = new byte[20];
        nodeId2[0] = 0x80;
        nodeId2[19] = 2;
        var node2 = new DhtNode
        {
            NodeId = nodeId2,
            EndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.1"), 6882), // Same IP, different port & NodeId
            LastSeen = DateTime.UtcNow
        };
        table.AddNode(node2);

        Assert.That(table.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void AddNode_should_reject_third_node_from_same_ipv4_24_subnet_in_same_bucket()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);

        // First node in 198.51.100.0/24
        var nodeId1 = new byte[20];
        nodeId1[0] = 0x80;
        nodeId1[19] = 1;
        table.AddNode(new DhtNode
        {
            NodeId = nodeId1,
            EndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.10"), 6881),
            LastSeen = DateTime.UtcNow
        });

        // Second node in same /24 subnet
        var nodeId2 = new byte[20];
        nodeId2[0] = 0x80;
        nodeId2[19] = 2;
        table.AddNode(new DhtNode
        {
            NodeId = nodeId2,
            EndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.20"), 6881),
            LastSeen = DateTime.UtcNow
        });

        Assert.That(table.NodeCount, Is.EqualTo(2));

        // Third node in same /24 subnet should be rejected
        var nodeId3 = new byte[20];
        nodeId3[0] = 0x80;
        nodeId3[19] = 3;
        table.AddNode(new DhtNode
        {
            NodeId = nodeId3,
            EndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.30"), 6881),
            LastSeen = DateTime.UtcNow
        });

        Assert.That(table.NodeCount, Is.EqualTo(2));
    }

    [Test]
    public void AddNode_should_accept_nodes_from_different_subnets_up_to_bucket_capacity()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 4);

        for (var i = 1; i <= 4; i++)
        {
            var nodeId = new byte[20];
            nodeId[0] = 0x80;
            nodeId[19] = (byte)i;

            // Each node in a different /24 subnet: 198.51.1.0/24, 198.51.2.0/24, etc.
            table.AddNode(new DhtNode
            {
                NodeId = nodeId,
                EndPoint = new IPEndPoint(IPAddress.Parse($"198.51.{i}.1"), 6881),
                LastSeen = DateTime.UtcNow
            });
        }

        Assert.That(table.NodeCount, Is.EqualTo(4));
    }

    [Test]
    public void AddNode_should_reject_loopback_and_link_local_when_allow_local_is_false()
    {
        var table = new RoutingTable(_localNodeId, allowLocal: false);

        var nodeId1 = new byte[20];
        nodeId1[0] = 0x80;
        table.AddNode(new DhtNode
        {
            NodeId = nodeId1,
            EndPoint = new IPEndPoint(IPAddress.Loopback, 6881),
            LastSeen = DateTime.UtcNow
        });

        var nodeId2 = new byte[20];
        nodeId2[0] = 0x40;
        table.AddNode(new DhtNode
        {
            NodeId = nodeId2,
            EndPoint = new IPEndPoint(IPAddress.Parse("169.254.1.1"), 6881),
            LastSeen = DateTime.UtcNow
        });

        Assert.That(table.NodeCount, Is.EqualTo(0));
    }

    [Test]
    public void AddNode_should_allow_loopback_when_allow_local_is_true()
    {
        var table = new RoutingTable(_localNodeId, allowLocal: true);

        var nodeId = new byte[20];
        nodeId[0] = 0x80;
        table.AddNode(new DhtNode
        {
            NodeId = nodeId,
            EndPoint = new IPEndPoint(IPAddress.Loopback, 6881),
            LastSeen = DateTime.UtcNow
        });

        Assert.That(table.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void AddNode_should_enforce_ipv6_48_subnet_diversity()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);

        var nodeId1 = new byte[20];
        nodeId1[0] = 0x80;
        nodeId1[19] = 1;
        table.AddNode(new DhtNode
        {
            NodeId = nodeId1,
            EndPoint = new IPEndPoint(IPAddress.Parse("2001:db8:abcd:1::1"), 6881),
            LastSeen = DateTime.UtcNow
        });

        var nodeId2 = new byte[20];
        nodeId2[0] = 0x80;
        nodeId2[19] = 2;
        table.AddNode(new DhtNode
        {
            NodeId = nodeId2,
            EndPoint = new IPEndPoint(IPAddress.Parse("2001:db8:abcd:2::1"), 6881),
            LastSeen = DateTime.UtcNow
        });

        Assert.That(table.NodeCount, Is.EqualTo(2));

        // 3rd node with same /48 prefix (2001:0db8:abcd) should be rejected
        var nodeId3 = new byte[20];
        nodeId3[0] = 0x80;
        nodeId3[19] = 3;
        table.AddNode(new DhtNode
        {
            NodeId = nodeId3,
            EndPoint = new IPEndPoint(IPAddress.Parse("2001:db8:abcd:3::1"), 6881),
            LastSeen = DateTime.UtcNow
        });

        Assert.That(table.NodeCount, Is.EqualTo(2));
    }

    private static DhtNode CreateNode(byte[] nodeId, IPAddress ip = null)
    {
        if (ip == null)
        {
            var b0 = nodeId.Length > 0 ? nodeId[0] : (byte)1;
            var b19 = nodeId.Length > 19 ? nodeId[19] : (byte)1;
            ip = new IPAddress(new byte[] { 8, b0, b19, 1 });
        }

        return new DhtNode
        {
            NodeId = nodeId,
            EndPoint = new IPEndPoint(ip, 6881),
            LastSeen = DateTime.UtcNow,
            FailCount = 0
        };
    }
}
