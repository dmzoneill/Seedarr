using System;
using System.Linq;
using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Dht;

namespace NzbDrone.Core.Test.Dht;

[TestFixture]
public class RoutingTableTests
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
        staleNode.FailCount = 3;
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

    [Test]
    public void CompareDistance_should_order_by_xor_distance_accurately()
    {
        var target = new byte[20];
        target[0] = 0x50;

        var close = new byte[20];
        close[0] = 0x51; // 0x50 ^ 0x51 = 0x01

        var far = new byte[20];
        far[0] = 0x5F; // 0x50 ^ 0x5F = 0x0F

        Assert.That(RoutingTable.CompareDistance(close, far, target), Is.LessThan(0));
        Assert.That(RoutingTable.CompareDistance(far, close, target), Is.GreaterThan(0));
        Assert.That(RoutingTable.CompareDistance(close, close, target), Is.EqualTo(0));
    }

    [Test]
    public void CompareDistance_should_allocate_zero_heap_memory()
    {
        var a = new byte[20];
        var b = new byte[20];
        var target = new byte[20];
        a[0] = 0x01;
        b[0] = 0x02;

        // Warm up JIT
        RoutingTable.CompareDistance(a, b, target);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            RoutingTable.CompareDistance(a, b, target);
        }

        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.That(allocatedAfter - allocatedBefore, Is.EqualTo(0));
    }

    [Test]
    public void GetClosestNodes_when_target_bucket_has_enough_nodes_should_return_nodes_from_target_bucket()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);

        // Target corresponds to bucket 0 (differs at bit 0 from local node 0x00)
        var target = new byte[20];
        target[0] = 0x80;

        // Add 5 nodes to bucket 0 (nodeId[0] == 0x80)
        for (var i = 1; i <= 5; i++)
        {
            var id = new byte[20];
            id[0] = 0x80;
            id[19] = (byte)i;
            var ip = IPAddress.Parse($"11.1.{i}.1");
            table.AddNode(CreateNode(id, ip));
        }

        // Add 3 nodes to bucket 1 (nodeId[0] == 0x40)
        for (var i = 1; i <= 3; i++)
        {
            var id = new byte[20];
            id[0] = 0x40;
            id[19] = (byte)i;
            var ip = IPAddress.Parse($"11.2.{i}.1");
            table.AddNode(CreateNode(id, ip));
        }

        // Request 4 nodes (target bucket has 5 >= 4)
        var closest = table.GetClosestNodes(target, 4);

        Assert.That(closest.Count, Is.EqualTo(4));
        Assert.That(closest.All(n => n.NodeId[0] == 0x80), Is.True);

        for (var i = 0; i < closest.Count - 1; i++)
        {
            Assert.That(RoutingTable.CompareDistance(closest[i].NodeId, closest[i + 1].NodeId, target), Is.LessThanOrEqualTo(0));
        }
    }

    [Test]
    public void GetClosestNodes_when_target_bucket_has_fewer_than_count_nodes_should_expand_outward()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);

        // Target corresponds to bucket 0 (nodeId[0] == 0x80)
        var target = new byte[20];
        target[0] = 0x80;

        // Bucket 0 has only 2 nodes
        for (var i = 1; i <= 2; i++)
        {
            var id = new byte[20];
            id[0] = 0x80;
            id[19] = (byte)i;
            table.AddNode(CreateNode(id, IPAddress.Parse($"12.1.{i}.1")));
        }

        // Bucket 1 has 2 nodes
        for (var i = 1; i <= 2; i++)
        {
            var id = new byte[20];
            id[0] = 0x40;
            id[19] = (byte)i;
            table.AddNode(CreateNode(id, IPAddress.Parse($"12.2.{i}.1")));
        }

        // Bucket 2 has 2 nodes
        for (var i = 1; i <= 2; i++)
        {
            var id = new byte[20];
            id[0] = 0x20;
            id[19] = (byte)i;
            table.AddNode(CreateNode(id, IPAddress.Parse($"12.3.{i}.1")));
        }

        // Request 5 nodes: 2 from bucket 0, 2 from bucket 1, 1 from bucket 2
        var closest = table.GetClosestNodes(target, 5);

        Assert.That(closest.Count, Is.EqualTo(5));

        // The 2 nodes from bucket 0 must be closest
        Assert.That(closest[0].NodeId[0], Is.EqualTo(0x80));
        Assert.That(closest[1].NodeId[0], Is.EqualTo(0x80));

        // Next 2 nodes (0x80 ^ 0x20 = 160 < 0x80 ^ 0x40 = 192)
        Assert.That(closest[2].NodeId[0], Is.EqualTo(0x20));
        Assert.That(closest[3].NodeId[0], Is.EqualTo(0x20));

        // 5th node
        Assert.That(closest[4].NodeId[0], Is.EqualTo(0x40));

        // Strictly ordered by distance
        for (var i = 0; i < closest.Count - 1; i++)
        {
            Assert.That(RoutingTable.CompareDistance(closest[i].NodeId, closest[i + 1].NodeId, target), Is.LessThanOrEqualTo(0));
        }
    }

    [Test]
    public void GetClosestNodes_should_return_strictly_ordered_nodes_across_multiple_buckets()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);
        var target = new byte[20];
        target[0] = 0x37;
        target[1] = 0xAB;

        for (var i = 1; i <= 30; i++)
        {
            var id = new byte[20];
            id[0] = (byte)((i * 37) % 256);
            id[1] = (byte)((i * 59) % 256);
            id[19] = (byte)i;
            table.AddNode(CreateNode(id, IPAddress.Parse($"13.{(i >> 8) & 0xFF}.{i & 0xFF}.1")));
        }

        var closest = table.GetClosestNodes(target, 8);

        Assert.That(closest.Count, Is.GreaterThan(0));
        Assert.That(closest.Count, Is.LessThanOrEqualTo(8));

        for (var i = 0; i < closest.Count - 1; i++)
        {
            Assert.That(RoutingTable.CompareDistance(closest[i].NodeId, closest[i + 1].NodeId, target), Is.LessThanOrEqualTo(0));
        }
    }

    // ── Replacement Cache & BEP 5 Enhancements ──────────────────────

    [Test]
    public void DhtNode_node_states_should_distinguish_good_questionable_and_bad()
    {
        var node = new DhtNode
        {
            NodeId = new byte[20],
            EndPoint = new IPEndPoint(IPAddress.Loopback, 6881),
            LastSeen = DateTime.UtcNow,
            FailCount = 0
        };

        Assert.That(node.IsGood, Is.True);
        Assert.That(node.IsQuestionable, Is.False);
        Assert.That(node.IsBad, Is.False);

        // Seen 20 minutes ago -> Questionable
        node.LastSeen = DateTime.UtcNow.AddMinutes(-20);
        Assert.That(node.IsGood, Is.False);
        Assert.That(node.IsQuestionable, Is.True);
        Assert.That(node.IsBad, Is.False);

        // FailCount >= 3 -> Bad
        node.FailCount = 3;
        Assert.That(node.IsGood, Is.False);
        Assert.That(node.IsQuestionable, Is.False);
        Assert.That(node.IsBad, Is.True);
    }

    [Test]
    public void AddNode_should_add_to_replacement_cache_when_bucket_is_full_of_good_nodes()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 2, replacementBucketSize: 2);
        var baseId = new byte[20];
        baseId[0] = 0x80;

        var node1 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.1.1"));
        node1.NodeId[19] = 1;
        var node2 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.2.2"));
        node2.NodeId[19] = 2;
        var node3 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.3.3"));
        node3.NodeId[19] = 3;

        table.AddNode(node1);
        table.AddNode(node2);
        table.AddNode(node3);

        Assert.That(table.NodeCount, Is.EqualTo(2));
        Assert.That(table.ReplacementNodeCount, Is.EqualTo(1));

        var bucketIndex = table.GetBucketIndex(node3.NodeId);
        var replacements = table.GetReplacementNodes(bucketIndex);
        Assert.That(replacements.Count, Is.EqualTo(1));
        Assert.That(replacements[0].NodeId[19], Is.EqualTo(3));
    }

    [Test]
    public void AddNode_should_trigger_PingBeforeEvict_when_bucket_is_full()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 2);
        var baseId = new byte[20];
        baseId[0] = 0x80;

        var node1 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.1.1"));
        node1.NodeId[19] = 1;
        node1.LastSeen = DateTime.UtcNow.AddMinutes(-10);

        var node2 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.2.2"));
        node2.NodeId[19] = 2;
        node2.LastSeen = DateTime.UtcNow.AddMinutes(-5);

        table.AddNode(node1);
        table.AddNode(node2);

        DhtNode pingedNode = null;
        var pingedBucket = -1;
        table.PingBeforeEvict += (bucketIndex, node) =>
        {
            pingedBucket = bucketIndex;
            pingedNode = node;
        };

        var node3 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.3.3"));
        node3.NodeId[19] = 3;
        table.AddNode(node3);

        Assert.That(pingedNode, Is.Not.Null);
        Assert.That(pingedNode.NodeId[19], Is.EqualTo(1));
        Assert.That(pingedBucket, Is.EqualTo(table.GetBucketIndex(node1.NodeId)));
    }

    [Test]
    public void EvictBadNodes_should_evict_bad_node_and_promote_replacement_candidate()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 2, replacementBucketSize: 2);
        var baseId = new byte[20];
        baseId[0] = 0x80;

        var node1 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.1.1"));
        node1.NodeId[19] = 1;
        var node2 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.2.2"));
        node2.NodeId[19] = 2;
        var node3 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.3.3"));
        node3.NodeId[19] = 3;

        table.AddNode(node1);
        table.AddNode(node2);
        table.AddNode(node3); // goes to replacement

        Assert.That(table.NodeCount, Is.EqualTo(2));
        Assert.That(table.ReplacementNodeCount, Is.EqualTo(1));

        // Node 1 becomes bad
        node1.FailCount = 3;
        var evicted = table.EvictBadNodes();

        Assert.That(evicted, Is.EqualTo(1));
        Assert.That(table.NodeCount, Is.EqualTo(2));
        Assert.That(table.ReplacementNodeCount, Is.EqualTo(0));

        var allNodes = table.GetAllNodes();
        Assert.That(allNodes.Any(n => n.NodeId[19] == 1), Is.False);
        Assert.That(allNodes.Any(n => n.NodeId[19] == 3), Is.True);
    }

    [Test]
    public void RecordFailure_should_increment_FailCount_and_evict_when_bad()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 2, replacementBucketSize: 2);
        var baseId = new byte[20];
        baseId[0] = 0x80;

        var node1 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.1.1"));
        node1.NodeId[19] = 1;
        var node2 = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.2.2"));
        node2.NodeId[19] = 2;
        var replacement = CreateNode((byte[])baseId.Clone(), IPAddress.Parse("8.1.3.3"));
        replacement.NodeId[19] = 3;

        table.AddNode(node1);
        table.AddNode(node2);
        table.AddNode(replacement);

        // Record 2 failures -> not evicted yet
        table.RecordFailure(node1.EndPoint);
        table.RecordFailure(node1.EndPoint);
        Assert.That(node1.FailCount, Is.EqualTo(2));
        Assert.That(table.NodeCount, Is.EqualTo(2));
        Assert.That(table.ReplacementNodeCount, Is.EqualTo(1));

        // 3rd failure -> evicted and replacement promoted
        table.RecordFailure(node1.EndPoint);
        Assert.That(table.NodeCount, Is.EqualTo(2));
        Assert.That(table.ReplacementNodeCount, Is.EqualTo(0));

        var allNodes = table.GetAllNodes();
        Assert.That(allNodes.Any(n => n.NodeId[19] == 3), Is.True);
        Assert.That(allNodes.Any(n => n.NodeId[19] == 1), Is.False);
    }

    [Test]
    public void GetClosestNodes_should_include_questionable_nodes()
    {
        var table = new RoutingTable(_localNodeId, bucketSize: 8);
        var target = new byte[20];
        target[0] = 0x01;

        var questionableId = new byte[20];
        questionableId[0] = 0x80;
        var node = CreateNode(questionableId);
        node.LastSeen = DateTime.UtcNow.AddMinutes(-30); // Questionable (> 15 min)
        table.AddNode(node);

        var closest = table.GetClosestNodes(target);

        Assert.That(closest.Count, Is.EqualTo(1));
        Assert.That(closest[0].IsQuestionable, Is.True);
        Assert.That(closest[0].IsGood, Is.False);
    }

    [Test]
    public void GenerateRandomIdForBucket_should_produce_id_in_expected_bucket_prefix_range()
    {
        var table = new RoutingTable(_localNodeId);

        for (var bucketIndex = 0; bucketIndex < 160; bucketIndex++)
        {
            var randomId = table.GenerateRandomIdForBucket(bucketIndex);
            Assert.That(randomId.Length, Is.EqualTo(20));
            Assert.That(table.GetBucketIndex(randomId), Is.EqualTo(bucketIndex));
        }
    }

    [Test]
    public void GetStaleBucketIndices_and_TouchBucket_should_track_bucket_staleness()
    {
        var table = new RoutingTable(_localNodeId);

        // Initially all buckets last changed are UtcNow, so threshold of 10s should return empty
        var stale = table.GetStaleBucketIndices(TimeSpan.FromSeconds(10));
        Assert.That(stale.Count, Is.EqualTo(0));

        // Zero threshold should consider all buckets stale
        stale = table.GetStaleBucketIndices(TimeSpan.Zero);
        Assert.That(stale.Count, Is.EqualTo(160));

        // Touch bucket 5
        table.TouchBucket(5);
        var lastChanged = table.GetBucketLastChanged(5);
        Assert.That((DateTime.UtcNow - lastChanged).TotalSeconds, Is.LessThan(2));
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
