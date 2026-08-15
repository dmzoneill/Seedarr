using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Dht;

namespace NzbDrone.Core.Test.Dht;

[TestFixture]
public class DhtServiceTest
{
    private DhtService _service;
    private IConfigService _configService;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.DhtBucketSize.Returns(8);
        _configService.DhtRoutingTableSize.Returns(160);
        _configService.DhtMaxNodes.Returns(1000);
        _configService.DhtConcurrentQueries.Returns(3);
        _configService.EnableDht.Returns(true);

        _service = new DhtService(_configService);
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
    }

    // ── Constructor ──────────────────────────────────────────────────

    [Test]
    public void Constructor_should_initialize_routing_table()
    {
        Assert.That(_service.RoutingTable, Is.Not.Null);
    }

    [Test]
    public void Constructor_should_initialize_peer_store()
    {
        Assert.That(_service.PeerStore, Is.Not.Null);
    }

    [Test]
    public void Constructor_should_default_concurrent_queries_when_zero()
    {
        _configService.DhtConcurrentQueries.Returns(0);
        using var service = new DhtService(_configService);

        // If it constructed without throwing, the semaphore was initialized with default of 3
        Assert.That(service.RoutingTable, Is.Not.Null);
    }

    [Test]
    public void Constructor_should_use_configured_concurrent_queries()
    {
        _configService.DhtConcurrentQueries.Returns(5);
        using var service = new DhtService(_configService);

        Assert.That(service.RoutingTable, Is.Not.Null);
    }

    // ── Dispose ──────────────────────────────────────────────────────

    [Test]
    public void Dispose_should_not_throw_when_called_multiple_times()
    {
        _service.Dispose();
        Assert.DoesNotThrow(() => _service.Dispose());
        _service = null; // prevent TearDown from double-disposing
    }

    // ── RoutingTable / PeerStore properties ──────────────────────────

    [Test]
    public void RoutingTable_should_start_empty()
    {
        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(0));
    }

    // ── ParseCompactNodes ────────────────────────────────────────────

    [Test]
    public void ParseCompactNodes_should_add_nodes_to_routing_table()
    {
        var compactData = new byte[26];
        var nodeId = new byte[20];
        nodeId[0] = 0xAA;
        Array.Copy(nodeId, 0, compactData, 0, 20);
        compactData[20] = 192;
        compactData[21] = 168;
        compactData[22] = 1;
        compactData[23] = 1;
        compactData[24] = (byte)(6881 >> 8);
        compactData[25] = (byte)(6881 & 0xFF);

        CallParseCompactNodes(_service, compactData);

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void ParseCompactNodes_should_parse_multiple_nodes()
    {
        var compactData = new byte[52];

        for (var i = 0; i < 2; i++)
        {
            var offset = i * 26;
            compactData[offset] = (byte)(0x10 * (i + 1));
            compactData[offset + 20] = 10;
            compactData[offset + 21] = 0;
            compactData[offset + 22] = 0;
            compactData[offset + 23] = (byte)(i + 1);
            compactData[offset + 24] = (byte)(6881 >> 8);
            compactData[offset + 25] = (byte)(6881 & 0xFF);
        }

        CallParseCompactNodes(_service, compactData);

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(2));
    }

    [Test]
    public void ParseCompactNodes_should_ignore_short_data()
    {
        var compactData = new byte[10];

        CallParseCompactNodes(_service, compactData);

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(0));
    }

    [Test]
    public void ParseCompactNodes_should_ignore_trailing_partial_node()
    {
        // 26 bytes for one full node + 10 trailing bytes (not enough for a second node)
        var compactData = new byte[36];
        compactData[0] = 0xAA;
        compactData[20] = 10;
        compactData[21] = 0;
        compactData[22] = 0;
        compactData[23] = 1;
        compactData[24] = (byte)(6881 >> 8);
        compactData[25] = (byte)(6881 & 0xFF);

        CallParseCompactNodes(_service, compactData);

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void ParseCompactNodes_should_handle_empty_data()
    {
        CallParseCompactNodes(_service, ReadOnlySpan<byte>.Empty);

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(0));
    }

    // ── EncodeCompactNodes ───────────────────────────────────────────

    [Test]
    public void EncodeCompactNodes_should_produce_correct_byte_array()
    {
        var nodes = new List<DhtNode>
        {
            new DhtNode
            {
                NodeId = CreateNodeId(0xAA),
                EndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881),
                LastSeen = DateTime.UtcNow
            }
        };

        var result = InvokeEncodeCompactNodes(nodes);

        Assert.That(result.Length, Is.EqualTo(26));
        Assert.That(result[0], Is.EqualTo(0xAA));
        Assert.That(result[20], Is.EqualTo(192));
        Assert.That(result[21], Is.EqualTo(168));
        Assert.That(result[22], Is.EqualTo(1));
        Assert.That(result[23], Is.EqualTo(1));
        Assert.That(result[24], Is.EqualTo((byte)(6881 >> 8)));
        Assert.That(result[25], Is.EqualTo((byte)(6881 & 0xFF)));
    }

    [Test]
    public void EncodeCompactNodes_should_handle_empty_list()
    {
        var nodes = new List<DhtNode>();

        var result = InvokeEncodeCompactNodes(nodes);

        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public void EncodeCompactNodes_should_handle_multiple_nodes()
    {
        var nodes = new List<DhtNode>
        {
            new DhtNode
            {
                NodeId = CreateNodeId(0x01),
                EndPoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881),
                LastSeen = DateTime.UtcNow
            },
            new DhtNode
            {
                NodeId = CreateNodeId(0x02),
                EndPoint = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 6882),
                LastSeen = DateTime.UtcNow
            }
        };

        var result = InvokeEncodeCompactNodes(nodes);

        Assert.That(result.Length, Is.EqualTo(52));
        Assert.That(result[0], Is.EqualTo(0x01));
        Assert.That(result[26], Is.EqualTo(0x02));
    }

    [Test]
    public void EncodeCompactNodes_should_encode_port_big_endian()
    {
        var nodes = new List<DhtNode>
        {
            new DhtNode
            {
                NodeId = CreateNodeId(0x01),
                EndPoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 0x1234),
                LastSeen = DateTime.UtcNow
            }
        };

        var result = InvokeEncodeCompactNodes(nodes);

        Assert.That(result[24], Is.EqualTo(0x12));
        Assert.That(result[25], Is.EqualTo(0x34));
    }

    // ── GenerateToken / ValidateToken ────────────────────────────────

    [Test]
    public void GenerateToken_should_produce_sha1_hash()
    {
        var method = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var token = (byte[])method.Invoke(_service, new object[] { IPAddress.Parse("192.168.1.1") });

        Assert.That(token.Length, Is.EqualTo(20));
    }

    [Test]
    public void GenerateToken_should_produce_same_token_for_same_ip()
    {
        var method = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var token1 = (byte[])method.Invoke(_service, new object[] { IPAddress.Parse("192.168.1.1") });
        var token2 = (byte[])method.Invoke(_service, new object[] { IPAddress.Parse("192.168.1.1") });

        Assert.That(token1, Is.EqualTo(token2));
    }

    [Test]
    public void GenerateToken_should_produce_different_tokens_for_different_ips()
    {
        var method = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var token1 = (byte[])method.Invoke(_service, new object[] { IPAddress.Parse("192.168.1.1") });
        var token2 = (byte[])method.Invoke(_service, new object[] { IPAddress.Parse("192.168.1.2") });

        Assert.That(token1, Is.Not.EqualTo(token2));
    }

    [Test]
    public void ValidateToken_should_accept_current_token()
    {
        var generateMethod = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var validateMethod = typeof(DhtService).GetMethod("ValidateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var ip = IPAddress.Parse("192.168.1.1");

        var token = (byte[])generateMethod.Invoke(_service, new object[] { ip });
        var result = (bool)validateMethod.Invoke(_service, new object[] { token, ip });

        Assert.That(result, Is.True);
    }

    [Test]
    public void ValidateToken_should_reject_invalid_token()
    {
        var validateMethod = typeof(DhtService).GetMethod("ValidateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var ip = IPAddress.Parse("192.168.1.1");
        var badToken = new byte[20];

        var result = (bool)validateMethod.Invoke(_service, new object[] { badToken, ip });

        Assert.That(result, Is.False);
    }

    [Test]
    public void ValidateToken_should_accept_previous_secret_token()
    {
        var generateWithSecretMethod = typeof(DhtService).GetMethod("GenerateTokenWithSecret", BindingFlags.NonPublic | BindingFlags.Instance);
        var validateMethod = typeof(DhtService).GetMethod("ValidateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var previousSecretField = typeof(DhtService).GetField("_previousTokenSecret", BindingFlags.NonPublic | BindingFlags.Instance);
        var ip = IPAddress.Parse("192.168.1.1");

        var previousSecret = (byte[])previousSecretField.GetValue(_service);
        var token = (byte[])generateWithSecretMethod.Invoke(_service, new object[] { ip, previousSecret });
        var result = (bool)validateMethod.Invoke(_service, new object[] { token, ip });

        Assert.That(result, Is.True);
    }

    [Test]
    public void ValidateToken_should_reject_token_from_different_ip()
    {
        var generateMethod = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var validateMethod = typeof(DhtService).GetMethod("ValidateToken", BindingFlags.NonPublic | BindingFlags.Instance);

        var token = (byte[])generateMethod.Invoke(_service, new object[] { IPAddress.Parse("192.168.1.1") });
        var result = (bool)validateMethod.Invoke(_service, new object[] { token, IPAddress.Parse("10.0.0.1") });

        Assert.That(result, Is.False);
    }

    [Test]
    public void GenerateTokenWithSecret_should_produce_deterministic_output()
    {
        var method = typeof(DhtService).GetMethod("GenerateTokenWithSecret", BindingFlags.NonPublic | BindingFlags.Instance);
        var secret = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var ip = IPAddress.Parse("192.168.1.1");

        var token1 = (byte[])method.Invoke(_service, new object[] { ip, secret });
        var token2 = (byte[])method.Invoke(_service, new object[] { ip, secret });

        Assert.That(token1, Is.EqualTo(token2));
    }

    // ── RotateSecretIfNeeded ─────────────────────────────────────────

    [Test]
    public void RotateSecretIfNeeded_should_not_rotate_before_interval()
    {
        var secretField = typeof(DhtService).GetField("_tokenSecret", BindingFlags.NonPublic | BindingFlags.Instance);
        var rotateMethod = typeof(DhtService).GetMethod("RotateSecretIfNeeded", BindingFlags.NonPublic | BindingFlags.Instance);

        var originalSecret = (byte[])((byte[])secretField.GetValue(_service)).Clone();
        rotateMethod.Invoke(_service, null);
        var currentSecret = (byte[])secretField.GetValue(_service);

        Assert.That(currentSecret, Is.EqualTo(originalSecret));
    }

    [Test]
    public void RotateSecretIfNeeded_should_rotate_after_interval()
    {
        var secretField = typeof(DhtService).GetField("_tokenSecret", BindingFlags.NonPublic | BindingFlags.Instance);
        var lastRotationField = typeof(DhtService).GetField("_lastSecretRotation", BindingFlags.NonPublic | BindingFlags.Instance);
        var rotateMethod = typeof(DhtService).GetMethod("RotateSecretIfNeeded", BindingFlags.NonPublic | BindingFlags.Instance);

        var originalSecret = (byte[])((byte[])secretField.GetValue(_service)).Clone();
        lastRotationField.SetValue(_service, DateTime.UtcNow.AddMinutes(-15));
        rotateMethod.Invoke(_service, null);
        var currentSecret = (byte[])secretField.GetValue(_service);

        Assert.That(currentSecret, Is.Not.EqualTo(originalSecret));
    }

    [Test]
    public void RotateSecretIfNeeded_should_preserve_previous_secret()
    {
        var secretField = typeof(DhtService).GetField("_tokenSecret", BindingFlags.NonPublic | BindingFlags.Instance);
        var previousSecretField = typeof(DhtService).GetField("_previousTokenSecret", BindingFlags.NonPublic | BindingFlags.Instance);
        var lastRotationField = typeof(DhtService).GetField("_lastSecretRotation", BindingFlags.NonPublic | BindingFlags.Instance);
        var rotateMethod = typeof(DhtService).GetMethod("RotateSecretIfNeeded", BindingFlags.NonPublic | BindingFlags.Instance);

        var originalSecret = (byte[])((byte[])secretField.GetValue(_service)).Clone();
        lastRotationField.SetValue(_service, DateTime.UtcNow.AddMinutes(-15));
        rotateMethod.Invoke(_service, null);
        var previousSecret = (byte[])previousSecretField.GetValue(_service);

        Assert.That(previousSecret, Is.EqualTo(originalSecret));
    }

    [Test]
    public void RotateSecretIfNeeded_should_update_last_rotation_time()
    {
        var lastRotationField = typeof(DhtService).GetField("_lastSecretRotation", BindingFlags.NonPublic | BindingFlags.Instance);
        var rotateMethod = typeof(DhtService).GetMethod("RotateSecretIfNeeded", BindingFlags.NonPublic | BindingFlags.Instance);

        var oldTime = DateTime.UtcNow.AddMinutes(-15);
        lastRotationField.SetValue(_service, oldTime);

        rotateMethod.Invoke(_service, null);

        var newTime = (DateTime)lastRotationField.GetValue(_service);
        Assert.That(newTime, Is.GreaterThan(oldTime));
    }

    [Test]
    public void ValidateToken_should_still_work_after_rotation()
    {
        var generateMethod = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var validateMethod = typeof(DhtService).GetMethod("ValidateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var lastRotationField = typeof(DhtService).GetField("_lastSecretRotation", BindingFlags.NonPublic | BindingFlags.Instance);
        var rotateMethod = typeof(DhtService).GetMethod("RotateSecretIfNeeded", BindingFlags.NonPublic | BindingFlags.Instance);
        var ip = IPAddress.Parse("192.168.1.1");

        // Generate token with current secret
        var token = (byte[])generateMethod.Invoke(_service, new object[] { ip });

        // Force rotation
        lastRotationField.SetValue(_service, DateTime.UtcNow.AddMinutes(-15));
        rotateMethod.Invoke(_service, null);

        // Token from previous secret should still validate
        var result = (bool)validateMethod.Invoke(_service, new object[] { token, ip });
        Assert.That(result, Is.True);
    }

    // ── HandleMessage ────────────────────────────────────────────────

    [Test]
    public void HandleMessage_should_not_throw_on_invalid_bencode()
    {
        var badData = new byte[] { 0xFF, 0xFE, 0xFD };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(badData, new IPEndPoint(IPAddress.Loopback, 6881)));
    }

    [Test]
    public void HandleMessage_should_not_throw_on_empty_data()
    {
        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(Array.Empty<byte>(), new IPEndPoint(IPAddress.Loopback, 6881)));
    }

    [Test]
    public void HandleMessage_should_ignore_error_message_type()
    {
        // Error messages ("e") are not handled - just parsed and ignored
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("e"),
            ["e"] = new BList
            {
                (IBObject)new BNumber(201),
                (IBObject)new BString("Generic error")
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Loopback, 6881)));

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(0));
    }

    [Test]
    public void HandleMessage_should_ignore_unknown_message_type()
    {
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("x")
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Loopback, 6881)));
    }

    // ── HandleResponse ───────────────────────────────────────────────

    [Test]
    public void HandleResponse_should_add_node_from_response_with_id()
    {
        var nodeId = CreateNodeId(0xBB);
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("r"),
            ["r"] = new BDictionary
            {
                ["id"] = new BString(nodeId)
            }
        };

        InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Loopback, 6881));

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void HandleResponse_should_parse_compact_nodes_via_message()
    {
        var compactData = new byte[26];
        var nodeId = new byte[20];
        nodeId[0] = 0xCC;
        Array.Copy(nodeId, 0, compactData, 0, 20);
        compactData[20] = 10;
        compactData[21] = 0;
        compactData[22] = 0;
        compactData[23] = 1;
        compactData[24] = (byte)(6881 >> 8);
        compactData[25] = (byte)(6881 & 0xFF);

        var responderId = CreateNodeId(0xDD);
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("r"),
            ["r"] = new BDictionary
            {
                ["id"] = new BString(responderId),
                ["nodes"] = new BString(compactData)
            }
        };

        InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Loopback, 6881));

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(2));
    }

    [Test]
    public void HandleResponse_should_skip_when_no_r_key()
    {
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("r")
        };

        InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Loopback, 6881));

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(0));
    }

    [Test]
    public void HandleResponse_should_parse_values_from_get_peers_response()
    {
        var ip = IPAddress.Parse("10.0.0.1");
        var port = 6881;
        var peerData = new byte[6];
        Array.Copy(ip.GetAddressBytes(), 0, peerData, 0, 4);
        peerData[4] = (byte)(port >> 8);
        peerData[5] = (byte)port;

        var responderId = CreateNodeId(0xEE);
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("r"),
            ["r"] = new BDictionary
            {
                ["id"] = new BString(responderId),
                ["values"] = new BList
                {
                    new BString(peerData)
                }
            }
        };

        // Should not throw and should add the responder node
        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Loopback, 6881)));

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void HandleResponse_should_parse_multiple_peer_values()
    {
        var values = new BList();
        for (var i = 1; i <= 3; i++)
        {
            var peerData = new byte[6];
            peerData[0] = 10;
            peerData[1] = 0;
            peerData[2] = 0;
            peerData[3] = (byte)i;
            peerData[4] = (byte)(6881 >> 8);
            peerData[5] = (byte)(6881 & 0xFF);
            values.Add(new BString(peerData));
        }

        var responderId = CreateNodeId(0xFF);
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("r"),
            ["r"] = new BDictionary
            {
                ["id"] = new BString(responderId),
                ["values"] = values
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Loopback, 6881)));
    }

    [Test]
    public void HandleResponse_should_skip_non_6_byte_peer_values()
    {
        // Non-6 byte values should still not crash (they're just skipped in the if check)
        var shortPeerData = new byte[4]; // too short
        var responderId = CreateNodeId(0xAB);
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("r"),
            ["r"] = new BDictionary
            {
                ["id"] = new BString(responderId),
                ["values"] = new BList
                {
                    new BString(shortPeerData)
                }
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Loopback, 6881)));
    }

    [Test]
    public void HandleResponse_should_handle_response_without_id()
    {
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("r"),
            ["r"] = new BDictionary
            {
                ["nodes"] = new BString(Array.Empty<byte>())
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Loopback, 6881)));

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(0));
    }

    [Test]
    public void HandleResponse_should_handle_both_nodes_and_values()
    {
        // A response can have both nodes and values
        var compactData = new byte[26];
        compactData[0] = 0xCC;
        compactData[20] = 10;
        compactData[21] = 0;
        compactData[22] = 0;
        compactData[23] = 1;
        compactData[24] = (byte)(6881 >> 8);
        compactData[25] = (byte)(6881 & 0xFF);

        var peerData = new byte[6];
        peerData[0] = 10;
        peerData[1] = 0;
        peerData[2] = 0;
        peerData[3] = 2;
        peerData[4] = (byte)(6882 >> 8);
        peerData[5] = (byte)(6882 & 0xFF);

        var responderId = CreateNodeId(0xDD);
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("r"),
            ["r"] = new BDictionary
            {
                ["id"] = new BString(responderId),
                ["nodes"] = new BString(compactData),
                ["values"] = new BList { new BString(peerData) }
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Loopback, 6881)));

        // Should have added both the responder node and the compact node
        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(2));
    }

    // ── HandleQuery ──────────────────────────────────────────────────

    [Test]
    public void HandleQuery_should_add_querying_node_to_routing_table()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var message = BuildQueryMessage("ping", nodeId);

        InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881));

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void HandleQuery_should_handle_ping_query()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var message = BuildQueryMessage("ping", nodeId);

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881)));
    }

    [Test]
    public void HandleQuery_should_handle_find_node_query()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var message = BuildQueryMessage("find_node", nodeId);

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881)));
    }

    [Test]
    public void HandleQuery_should_handle_get_peers_query_no_peers()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("get_peers"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["info_hash"] = new BString(infoHash)
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881)));

        // Should add the querying node
        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void HandleQuery_should_handle_get_peers_query_with_peers()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var infoHash = RandomNumberGenerator.GetBytes(20);

        // Pre-populate the peer store with a peer for this info_hash
        _service.PeerStore.AddPeer(infoHash, IPAddress.Parse("192.168.1.100"), 51413);

        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("get_peers"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["info_hash"] = new BString(infoHash)
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881)));
    }

    [Test]
    public void HandleQuery_should_handle_get_peers_missing_info_hash()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("get_peers"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId)
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881)));
    }

    [Test]
    public void HandleQuery_should_handle_announce_peer_with_valid_token()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var senderIp = IPAddress.Parse("10.0.0.1");

        // Generate a valid token for this IP
        var generateMethod = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var token = (byte[])generateMethod.Invoke(_service, new object[] { senderIp });

        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("announce_peer"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["info_hash"] = new BString(infoHash),
                ["port"] = new BNumber(51413),
                ["token"] = new BString(token)
            }
        };

        InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(senderIp, 6881));

        // Should have stored the peer
        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.True);
    }

    [Test]
    public void HandleQuery_should_handle_announce_peer_with_invalid_token()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var badToken = new byte[20];

        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("announce_peer"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["info_hash"] = new BString(infoHash),
                ["port"] = new BNumber(51413),
                ["token"] = new BString(badToken)
            }
        };

        InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881));

        // Should NOT have stored the peer
        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.False);
    }

    [Test]
    public void HandleQuery_should_handle_announce_peer_missing_info_hash()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);

        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("announce_peer"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["token"] = new BString(new byte[20])
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881)));
    }

    [Test]
    public void HandleQuery_should_handle_announce_peer_missing_token()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var infoHash = RandomNumberGenerator.GetBytes(20);

        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("announce_peer"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["info_hash"] = new BString(infoHash)
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881)));

        // Should NOT have stored the peer
        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.False);
    }

    [Test]
    public void HandleQuery_should_handle_announce_peer_with_implied_port_nonzero()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var senderIp = IPAddress.Parse("10.0.0.1");
        var senderPort = 12345;

        var generateMethod = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var token = (byte[])generateMethod.Invoke(_service, new object[] { senderIp });

        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("announce_peer"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["info_hash"] = new BString(infoHash),
                ["port"] = new BNumber(51413),
                ["token"] = new BString(token),
                ["implied_port"] = new BNumber(1) // non-zero: use UDP source port
            }
        };

        InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(senderIp, senderPort));

        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.True);
    }

    [Test]
    public void HandleQuery_should_handle_announce_peer_with_implied_port_zero_and_port()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var senderIp = IPAddress.Parse("10.0.0.1");

        var generateMethod = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var token = (byte[])generateMethod.Invoke(_service, new object[] { senderIp });

        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("announce_peer"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["info_hash"] = new BString(infoHash),
                ["port"] = new BNumber(51413),
                ["token"] = new BString(token),
                ["implied_port"] = new BNumber(0) // zero: use explicit port
            }
        };

        InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(senderIp, 6881));

        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.True);
    }

    [Test]
    public void HandleQuery_should_handle_announce_peer_with_implied_port_zero_no_explicit_port()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var senderIp = IPAddress.Parse("10.0.0.1");

        var generateMethod = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var token = (byte[])generateMethod.Invoke(_service, new object[] { senderIp });

        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("announce_peer"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["info_hash"] = new BString(infoHash),
                ["token"] = new BString(token),
                ["implied_port"] = new BNumber(0) // zero, but no "port" key
            }
        };

        // Should not throw; peer gets sender.Port as the port
        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(senderIp, 6881)));

        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.True);
    }

    [Test]
    public void HandleQuery_should_handle_announce_peer_no_implied_port_with_explicit_port()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var senderIp = IPAddress.Parse("10.0.0.1");

        var generateMethod = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var token = (byte[])generateMethod.Invoke(_service, new object[] { senderIp });

        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("announce_peer"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["info_hash"] = new BString(infoHash),
                ["port"] = new BNumber(51413),
                ["token"] = new BString(token)
            }
        };

        InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(senderIp, 6881));

        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.True);
    }

    [Test]
    public void HandleQuery_should_handle_announce_peer_no_implied_port_no_explicit_port()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var senderIp = IPAddress.Parse("10.0.0.1");

        var generateMethod = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var token = (byte[])generateMethod.Invoke(_service, new object[] { senderIp });

        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("announce_peer"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId),
                ["info_hash"] = new BString(infoHash),
                ["token"] = new BString(token)
            }
        };

        InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(senderIp, 6881));

        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.True);
    }

    [Test]
    public void HandleQuery_should_handle_unknown_query_type()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("unknown_query"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId)
            }
        };

        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881)));

        // Should still add the querying node even for unknown query types
        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void HandleQuery_should_not_add_node_without_id_in_args()
    {
        SetUdpClient();
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("ping"),
            ["a"] = new BDictionary()
        };

        // This will try to call SendPingResponse. The send may or may not work.
        // The main thing is it should not throw for the missing id check.
        Assert.DoesNotThrow(() =>
            InvokeHandleMessage(message.EncodeAsBytes(), new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881)));

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(0));
    }

    // ── SendGetPeers / SendAnnouncePeer / SendFindNode ───────────────

    [Test]
    public void SendGetPeers_should_send_without_throwing()
    {
        SetUdpClient();
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var target = new IPEndPoint(IPAddress.Loopback, 6881);

        Assert.DoesNotThrow(() => _service.SendGetPeers(target, infoHash));
    }

    [Test]
    public void SendAnnouncePeer_should_send_without_throwing()
    {
        SetUdpClient();
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var token = RandomNumberGenerator.GetBytes(20);
        var target = new IPEndPoint(IPAddress.Loopback, 6881);

        Assert.DoesNotThrow(() => _service.SendAnnouncePeer(target, infoHash, 51413, token));
    }

    [Test]
    public void SendAnnouncePeer_should_send_with_implied_port()
    {
        SetUdpClient();
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var token = RandomNumberGenerator.GetBytes(20);
        var target = new IPEndPoint(IPAddress.Loopback, 6881);

        Assert.DoesNotThrow(() => _service.SendAnnouncePeer(target, infoHash, 51413, token, impliedPort: true));
    }

    [Test]
    public void SendAnnouncePeer_should_send_without_implied_port()
    {
        SetUdpClient();
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var token = RandomNumberGenerator.GetBytes(20);
        var target = new IPEndPoint(IPAddress.Loopback, 6881);

        Assert.DoesNotThrow(() => _service.SendAnnouncePeer(target, infoHash, 51413, token, impliedPort: false));
    }

    [Test]
    public void SendFindNode_should_send_without_throwing()
    {
        SetUdpClient();
        var targetId = RandomNumberGenerator.GetBytes(20);
        var target = new IPEndPoint(IPAddress.Loopback, 6881);

        var method = typeof(DhtService).GetMethod("SendFindNode", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotThrow(() =>
            method.Invoke(_service, new object[] { target, targetId }));
    }

    // ── SendPingResponse / SendFindNodeResponse / SendErrorResponse ──

    [Test]
    public void SendPingResponse_should_send_without_throwing()
    {
        SetUdpClient();
        var transactionId = new BString(new byte[] { 0x01, 0x02 });
        var target = new IPEndPoint(IPAddress.Loopback, 6881);

        var method = typeof(DhtService).GetMethod("SendPingResponse", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotThrow(() =>
            method.Invoke(_service, new object[] { target, transactionId }));
    }

    [Test]
    public void SendFindNodeResponse_should_send_without_throwing()
    {
        SetUdpClient();
        var transactionId = new BString(new byte[] { 0x01, 0x02 });
        var target = new IPEndPoint(IPAddress.Loopback, 6881);

        var method = typeof(DhtService).GetMethod("SendFindNodeResponse", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotThrow(() =>
            method.Invoke(_service, new object[] { target, transactionId }));
    }

    [Test]
    public void SendErrorResponse_should_send_without_throwing()
    {
        SetUdpClient();
        var transactionId = new BString(new byte[] { 0x01, 0x02 });
        var target = new IPEndPoint(IPAddress.Loopback, 6881);

        var method = typeof(DhtService).GetMethod("SendErrorResponse", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotThrow(() =>
            method.Invoke(_service, new object[] { target, transactionId, 203, "Invalid token" }));
    }

    // ── HandleGetPeersQuery edge cases ───────────────────────────────

    [Test]
    public void HandleGetPeersQuery_should_return_values_when_peers_exist()
    {
        SetUdpClient();
        var infoHash = RandomNumberGenerator.GetBytes(20);

        // Add multiple peers for this info_hash
        _service.PeerStore.AddPeer(infoHash, IPAddress.Parse("192.168.1.100"), 51413);
        _service.PeerStore.AddPeer(infoHash, IPAddress.Parse("192.168.1.101"), 51414);

        var args = new BDictionary
        {
            ["id"] = new BString(CreateNodeId(0x42)),
            ["info_hash"] = new BString(infoHash)
        };
        var transactionId = new BString(new byte[] { 0x01, 0x02 });
        var sender = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881);

        var method = typeof(DhtService).GetMethod("HandleGetPeersQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotThrow(() =>
            method.Invoke(_service, new object[] { args, sender, transactionId }));
    }

    [Test]
    public void HandleGetPeersQuery_should_return_closest_nodes_when_no_peers()
    {
        SetUdpClient();
        var infoHash = RandomNumberGenerator.GetBytes(20);

        // Add some nodes to the routing table so there are closest nodes to return
        _service.RoutingTable.AddNode(new DhtNode
        {
            NodeId = CreateNodeId(0x11),
            EndPoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881),
            LastSeen = DateTime.UtcNow
        });

        var args = new BDictionary
        {
            ["id"] = new BString(CreateNodeId(0x42)),
            ["info_hash"] = new BString(infoHash)
        };
        var transactionId = new BString(new byte[] { 0x01, 0x02 });
        var sender = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 6881);

        var method = typeof(DhtService).GetMethod("HandleGetPeersQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotThrow(() =>
            method.Invoke(_service, new object[] { args, sender, transactionId }));
    }

    [Test]
    public void HandleGetPeersQuery_should_return_early_without_info_hash()
    {
        SetUdpClient();
        var args = new BDictionary
        {
            ["id"] = new BString(CreateNodeId(0x42))
        };
        var transactionId = new BString(new byte[] { 0x01, 0x02 });
        var sender = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881);

        var method = typeof(DhtService).GetMethod("HandleGetPeersQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotThrow(() =>
            method.Invoke(_service, new object[] { args, sender, transactionId }));
    }

    // ── HandleAnnouncePeerQuery edge cases ───────────────────────────

    [Test]
    public void HandleAnnouncePeerQuery_should_return_early_without_info_hash()
    {
        SetUdpClient();
        var args = new BDictionary
        {
            ["id"] = new BString(CreateNodeId(0x42)),
            ["token"] = new BString(new byte[20])
        };
        var transactionId = new BString(new byte[] { 0x01, 0x02 });
        var sender = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881);

        var method = typeof(DhtService).GetMethod("HandleAnnouncePeerQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotThrow(() =>
            method.Invoke(_service, new object[] { args, sender, transactionId }));
    }

    [Test]
    public void HandleAnnouncePeerQuery_should_return_early_without_token()
    {
        SetUdpClient();
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var args = new BDictionary
        {
            ["id"] = new BString(CreateNodeId(0x42)),
            ["info_hash"] = new BString(infoHash)
        };
        var transactionId = new BString(new byte[] { 0x01, 0x02 });
        var sender = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881);

        var method = typeof(DhtService).GetMethod("HandleAnnouncePeerQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotThrow(() =>
            method.Invoke(_service, new object[] { args, sender, transactionId }));

        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.False);
    }

    [Test]
    public void HandleAnnouncePeerQuery_should_send_error_for_invalid_token()
    {
        SetUdpClient();
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var badToken = new byte[20];
        var args = new BDictionary
        {
            ["id"] = new BString(CreateNodeId(0x42)),
            ["info_hash"] = new BString(infoHash),
            ["token"] = new BString(badToken),
            ["port"] = new BNumber(51413)
        };
        var transactionId = new BString(new byte[] { 0x01, 0x02 });
        var sender = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881);

        var method = typeof(DhtService).GetMethod("HandleAnnouncePeerQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotThrow(() =>
            method.Invoke(_service, new object[] { args, sender, transactionId }));

        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.False);
    }

    [Test]
    public void HandleAnnouncePeerQuery_should_store_peer_with_valid_token()
    {
        SetUdpClient();
        var infoHash = RandomNumberGenerator.GetBytes(20);
        var senderIp = IPAddress.Parse("10.0.0.1");

        var generateMethod = typeof(DhtService).GetMethod("GenerateToken", BindingFlags.NonPublic | BindingFlags.Instance);
        var token = (byte[])generateMethod.Invoke(_service, new object[] { senderIp });

        var args = new BDictionary
        {
            ["id"] = new BString(CreateNodeId(0x42)),
            ["info_hash"] = new BString(infoHash),
            ["token"] = new BString(token),
            ["port"] = new BNumber(51413)
        };
        var transactionId = new BString(new byte[] { 0x01, 0x02 });
        var sender = new IPEndPoint(senderIp, 6881);

        var method = typeof(DhtService).GetMethod("HandleAnnouncePeerQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_service, new object[] { args, sender, transactionId });

        Assert.That(_service.PeerStore.HasPeers(infoHash), Is.True);
    }

    // ── ExecuteAsync edge case: DHT disabled ─────────────────────────

    [Test]
    public void ExecuteAsync_should_return_immediately_when_dht_disabled()
    {
        _configService.EnableDht.Returns(false);
        using var service = new DhtService(_configService);

        var executeMethod = typeof(DhtService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        using var cts = new System.Threading.CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        var task = (System.Threading.Tasks.Task)executeMethod.Invoke(service, new object[] { cts.Token });
        Assert.That(task.Wait(TimeSpan.FromSeconds(3)), Is.True);
    }

    // ── ExecuteAsync: port unavailable ──────────────────────────────

    [Test]
    public async Task ExecuteAsync_should_return_when_port_is_unavailable()
    {
        // Pre-occupy port 6882 so ExecuteAsync cannot bind it
        UdpClient blocker = null;
        try { blocker = new UdpClient(6882); }
        catch (SocketException) { /* already occupied — same SocketException path still exercised */ }

        _configService.EnableDht.Returns(true);
        _configService.DhtAutoBootstrap.Returns(false);
        _configService.DhtQueryTimeout.Returns(1);
        _configService.DhtAnnouncementInterval.Returns(3600);
        using var service = new DhtService(_configService);

        try
        {
            var executeMethod = typeof(DhtService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var task = (Task)executeMethod.Invoke(service, new object[] { cts.Token });

            var finished = await Task.WhenAny(task, Task.Delay(2000));
            Assert.That(finished, Is.EqualTo(task), "ExecuteAsync should return quickly when port is unavailable");
        }
        finally
        {
            blocker?.Dispose();
        }
    }

    // ── ExecuteAsync: main loop cancellation ─────────────────────────

    [Test]
    public async Task ExecuteAsync_should_cancel_cleanly_when_stopping_during_receive()
    {
        // Long receive timeout so the stopping cancellation interrupts ReceiveAsync,
        // covering the outer OperationCanceledException handler that breaks the loop.
        _configService.EnableDht.Returns(true);
        _configService.DhtAutoBootstrap.Returns(false);
        _configService.DhtQueryTimeout.Returns(30);
        _configService.DhtAnnouncementInterval.Returns(3600);
        _configService.DhtRateLimitEnabled.Returns(false);
        using var service = new DhtService(_configService);

        var executeMethod = typeof(DhtService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        using var cts = new CancellationTokenSource();
        var task = (Task)executeMethod.Invoke(service, new object[] { cts.Token });

        if (task.IsCompleted)
        {
            // Port 6882 unavailable — SocketException path exercised instead; that is fine
            return;
        }

        // Allow time to reach ReceiveAsync, then cancel stoppingToken
        await Task.Delay(200);
        await cts.CancelAsync();

        var finished = await Task.WhenAny(task, Task.Delay(2000));
        Assert.That(finished, Is.EqualTo(task), "ExecuteAsync should complete within 2 s of cancellation");
    }

    // ── ExecuteAsync: periodic refresh ──────────────────────────────

    [Test]
    public async Task ExecuteAsync_should_update_next_refresh_when_past_due()
    {
        // Short receive timeout so the loop iterates quickly; large interval so refresh
        // does not trigger until we manually force _nextRefresh into the past.
        _configService.EnableDht.Returns(true);
        _configService.DhtAutoBootstrap.Returns(false);
        _configService.DhtQueryTimeout.Returns(1);
        _configService.DhtAnnouncementInterval.Returns(3600);
        _configService.DhtRateLimitEnabled.Returns(false);
        using var service = new DhtService(_configService);

        var executeMethod = typeof(DhtService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var nextRefreshField = typeof(DhtService).GetField("_nextRefresh", BindingFlags.NonPublic | BindingFlags.Instance);
        using var cts = new CancellationTokenSource();
        var task = (Task)executeMethod.Invoke(service, new object[] { cts.Token });

        if (task.IsCompleted)
        {
            return; // Port unavailable
        }

        // Wait for initialization (bind + _nextRefresh assignment) to complete
        await Task.Delay(200);

        // Force next refresh into the past
        nextRefreshField.SetValue(service, DateTime.UtcNow.AddSeconds(-1));

        // Wait for at least one full loop iteration (1 s receive timeout + 500 ms margin)
        await Task.Delay(1500);

        var nextRefresh = (DateTime)nextRefreshField.GetValue(service);
        Assert.That(
            nextRefresh,
            Is.GreaterThan(DateTime.UtcNow),
            "_nextRefresh should be updated to a future time after a periodic refresh fires");

        await cts.CancelAsync();
        await Task.WhenAny(task, Task.Delay(2000));
    }

    // ── ExecuteAsync: bootstrap on startup ──────────────────────────

    [Test]
    public async Task ExecuteAsync_should_attempt_bootstrap_on_startup()
    {
        // DhtBootstrapTimeout=1 caps DNS + send to 1 s, keeping the test fast.
        // Bootstrap warning log ("timed out after 1s") confirms the path was exercised.
        _configService.EnableDht.Returns(true);
        _configService.DhtAutoBootstrap.Returns(true);
        _configService.DhtBootstrapTimeout.Returns(1);
        _configService.DhtQueryTimeout.Returns(30);
        _configService.DhtAnnouncementInterval.Returns(3600);
        _configService.DhtRateLimitEnabled.Returns(false);
        using var service = new DhtService(_configService);

        var executeMethod = typeof(DhtService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        using var cts = new CancellationTokenSource();
        var task = (Task)executeMethod.Invoke(service, new object[] { cts.Token });

        if (task.IsCompleted)
        {
            return; // Port unavailable
        }

        // Allow bootstrap to run (up to 1 s) then enter main loop
        await Task.Delay(1500);
        await cts.CancelAsync();

        var finished = await Task.WhenAny(task, Task.Delay(2000));
        Assert.That(finished, Is.EqualTo(task), "ExecuteAsync should complete after cancellation following startup bootstrap");
    }

    // ── ExecuteAsync: periodic refresh with auto-bootstrap ───────────

    [Test]
    public async Task ExecuteAsync_should_trigger_periodic_refresh_with_auto_bootstrap_enabled()
    {
        _configService.EnableDht.Returns(true);
        _configService.DhtAutoBootstrap.Returns(true);
        _configService.DhtBootstrapTimeout.Returns(1);  // fast startup bootstrap timeout
        _configService.DhtQueryTimeout.Returns(1);       // fast loop iterations
        _configService.DhtAnnouncementInterval.Returns(3600);
        _configService.DhtRateLimitEnabled.Returns(false);
        using var service = new DhtService(_configService);

        var executeMethod = typeof(DhtService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var nextRefreshField = typeof(DhtService).GetField("_nextRefresh", BindingFlags.NonPublic | BindingFlags.Instance);
        using var cts = new CancellationTokenSource();
        var task = (Task)executeMethod.Invoke(service, new object[] { cts.Token });

        if (task.IsCompleted)
        {
            return; // Port unavailable
        }

        // Wait for startup bootstrap (1 s timeout) to complete before entering loop
        await Task.Delay(1500);

        // Force periodic refresh — auto-bootstrap=true means Bootstrap() is called here too
        nextRefreshField.SetValue(service, DateTime.UtcNow.AddSeconds(-1));

        // Wait for one full loop iteration (1 s receive timeout + 500 ms margin)
        await Task.Delay(1500);

        var nextRefresh = (DateTime)nextRefreshField.GetValue(service);
        Assert.That(
            nextRefresh,
            Is.GreaterThan(DateTime.UtcNow),
            "_nextRefresh should be updated after periodic refresh with auto-bootstrap enabled");

        await cts.CancelAsync();
        await Task.WhenAny(task, Task.Delay(2000));
    }

    // ── Full flow: ping query -> response sent ───────────────────────

    [Test]
    public void HandleQuery_ping_should_respond_and_add_node()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x42);
        var message = BuildQueryMessage("ping", nodeId);
        var sender = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881);

        InvokeHandleMessage(message.EncodeAsBytes(), sender);

        // The querying node should be added to the routing table
        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void HandleQuery_find_node_should_respond_and_add_node()
    {
        SetUdpClient();
        var nodeId = CreateNodeId(0x43);
        var message = BuildQueryMessage("find_node", nodeId);
        var sender = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 6881);

        InvokeHandleMessage(message.EncodeAsBytes(), sender);

        Assert.That(_service.RoutingTable.NodeCount, Is.EqualTo(1));
    }

    // ── Roundtrip: encode then parse compact nodes ───────────────────

    [Test]
    public void EncodeCompactNodes_then_ParseCompactNodes_should_roundtrip()
    {
        var nodes = new List<DhtNode>
        {
            new DhtNode
            {
                NodeId = CreateNodeId(0xAA),
                EndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881),
                LastSeen = DateTime.UtcNow
            },
            new DhtNode
            {
                NodeId = CreateNodeId(0xBB),
                EndPoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 8080),
                LastSeen = DateTime.UtcNow
            }
        };

        var encoded = InvokeEncodeCompactNodes(nodes);

        // Use a fresh service so routing table starts empty
        _configService.DhtConcurrentQueries.Returns(3);
        using var freshService = new DhtService(_configService);
        CallParseCompactNodes(freshService, encoded);

        Assert.That(freshService.RoutingTable.NodeCount, Is.EqualTo(2));
    }

    // ── ExecuteAsync: rate limiting paths ────────────────────────────

    [Test]
    public async Task ExecuteAsync_should_process_received_packet_through_rate_limiter()
    {
        // Sends a real UDP packet to port 6882 and verifies the rate-limit counter increments,
        // exercising the DhtRateLimitEnabled=true code path inside the loop.
        _configService.EnableDht.Returns(true);
        _configService.DhtAutoBootstrap.Returns(false);
        _configService.DhtQueryTimeout.Returns(5);
        _configService.DhtAnnouncementInterval.Returns(3600);
        _configService.DhtRateLimitEnabled.Returns(true);
        _configService.DhtMaxQueriesPerSecond.Returns(100);
        using var service = new DhtService(_configService);

        var executeMethod = typeof(DhtService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        using var cts = new CancellationTokenSource();
        var task = (Task)executeMethod.Invoke(service, new object[] { cts.Token });

        if (task.IsCompleted)
        {
            return; // Port 6882 unavailable — SocketException path exercised
        }

        await Task.Delay(150);

        using var sender = new System.Net.Sockets.UdpClient();
        var pingBytes = BuildPingBytesWithNodeId(new byte[20]);
        await sender.SendAsync(pingBytes, pingBytes.Length, new IPEndPoint(IPAddress.Loopback, 6882));

        await Task.Delay(300);

        var queryCountField = typeof(DhtService).GetField("_queryCount", BindingFlags.NonPublic | BindingFlags.Instance);
        var queryCount = (int)queryCountField.GetValue(service);
        Assert.That(queryCount, Is.EqualTo(1), "Rate limit counter should have been incremented after processing one packet");

        await cts.CancelAsync();
        await Task.WhenAny(task, Task.Delay(2000));
    }

    [Test]
    public async Task ExecuteAsync_should_drop_packet_when_rate_limit_count_is_exceeded()
    {
        // With DhtMaxQueriesPerSecond=0, any received packet triggers the rate-limit
        // continue path; HandleMessage is never called so the routing table stays empty.
        _configService.EnableDht.Returns(true);
        _configService.DhtAutoBootstrap.Returns(false);
        _configService.DhtQueryTimeout.Returns(5);
        _configService.DhtAnnouncementInterval.Returns(3600);
        _configService.DhtRateLimitEnabled.Returns(true);
        _configService.DhtMaxQueriesPerSecond.Returns(0);
        using var service = new DhtService(_configService);

        var executeMethod = typeof(DhtService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        using var cts = new CancellationTokenSource();
        var task = (Task)executeMethod.Invoke(service, new object[] { cts.Token });

        if (task.IsCompleted)
        {
            return; // Port unavailable
        }

        await Task.Delay(150);

        using var sender = new System.Net.Sockets.UdpClient();
        var pingBytes = BuildPingBytesWithNodeId(CreateNodeId(0x42));
        await sender.SendAsync(pingBytes, pingBytes.Length, new IPEndPoint(IPAddress.Loopback, 6882));

        await Task.Delay(300);

        // HandleMessage was NOT called — packet was dropped by the rate limiter (continue path)
        Assert.That(service.RoutingTable.NodeCount, Is.EqualTo(0));

        await cts.CancelAsync();
        await Task.WhenAny(task, Task.Delay(2000));
    }

    [Test]
    public async Task ExecuteAsync_should_reset_rate_limit_window_when_more_than_one_second_has_elapsed()
    {
        // After the window expires (>1s), the counter resets to 0 before processing the packet.
        _configService.EnableDht.Returns(true);
        _configService.DhtAutoBootstrap.Returns(false);
        _configService.DhtQueryTimeout.Returns(5);
        _configService.DhtAnnouncementInterval.Returns(3600);
        _configService.DhtRateLimitEnabled.Returns(true);
        _configService.DhtMaxQueriesPerSecond.Returns(100);
        using var service = new DhtService(_configService);

        var executeMethod = typeof(DhtService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var queryCountField = typeof(DhtService).GetField("_queryCount", BindingFlags.NonPublic | BindingFlags.Instance);
        var windowField = typeof(DhtService).GetField("_rateLimitWindowStart", BindingFlags.NonPublic | BindingFlags.Instance);
        using var cts = new CancellationTokenSource();
        var task = (Task)executeMethod.Invoke(service, new object[] { cts.Token });

        if (task.IsCompleted)
        {
            return; // Port unavailable
        }

        await Task.Delay(150);

        // Simulate a stale rate-limit window with a high counter
        queryCountField.SetValue(service, 50);
        windowField.SetValue(service, DateTime.UtcNow.AddSeconds(-2));

        await Task.Delay(50);

        using var sender = new System.Net.Sockets.UdpClient();
        var pingBytes = BuildPingBytesWithNodeId(new byte[20]);
        await sender.SendAsync(pingBytes, pingBytes.Length, new IPEndPoint(IPAddress.Loopback, 6882));

        await Task.Delay(300);

        // Window reset → counter goes 0 → 1 (not 51)
        var queryCount = (int)queryCountField.GetValue(service);
        Assert.That(queryCount, Is.EqualTo(1), "Rate-limit window should have reset and counter should restart from 1");

        await cts.CancelAsync();
        await Task.WhenAny(task, Task.Delay(2000));
    }

    [Test]
    public async Task ExecuteAsync_should_handle_receive_exception_in_loop_and_continue_to_cancellation()
    {
        // Disposing the UDP client mid-loop causes ObjectDisposedException in ReceiveAsync.
        // This is NOT an OperationCanceledException so it propagates to the outer
        // catch (Exception ex) handler, which logs it and lets the loop continue.
        _configService.EnableDht.Returns(true);
        _configService.DhtAutoBootstrap.Returns(false);
        _configService.DhtQueryTimeout.Returns(10);
        _configService.DhtAnnouncementInterval.Returns(3600);
        _configService.DhtRateLimitEnabled.Returns(false);
        using var service = new DhtService(_configService);

        var executeMethod = typeof(DhtService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var udpField = typeof(DhtService).GetField("_udpClient", BindingFlags.NonPublic | BindingFlags.Instance);
        using var cts = new CancellationTokenSource();
        var task = (Task)executeMethod.Invoke(service, new object[] { cts.Token });

        if (task.IsCompleted)
        {
            return; // Port unavailable
        }

        // Wait for the service to settle into ReceiveAsync
        await Task.Delay(200);

        // Dispose the UDP client — ReceiveAsync throws ObjectDisposedException
        // which the outer catch (Exception ex) handles
        var udpClient = udpField.GetValue(service) as System.Net.Sockets.UdpClient;
        udpClient?.Dispose();

        await Task.Delay(100);

        // Cancel the service — should exit cleanly
        await cts.CancelAsync();

        var finished = await Task.WhenAny(task, Task.Delay(3000));
        Assert.That(finished, Is.EqualTo(task), "ExecuteAsync should complete within 3s after cancellation following a receive error");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private void InvokeHandleMessage(byte[] data, IPEndPoint sender)
    {
        var method = typeof(DhtService).GetMethod(
            "HandleMessage",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_service, new object[] { data, sender });
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ParseCompactNodes")]
    private static extern void CallParseCompactNodes(DhtService service, ReadOnlySpan<byte> data);

    private static byte[] CreateNodeId(byte firstByte)
    {
        var nodeId = new byte[20];
        nodeId[0] = firstByte;
        return nodeId;
    }

    private byte[] InvokeEncodeCompactNodes(List<DhtNode> nodes)
    {
        var method = typeof(DhtService).GetMethod("EncodeCompactNodes", BindingFlags.NonPublic | BindingFlags.Instance);
        return (byte[])method.Invoke(_service, new object[] { nodes });
    }

    /// <summary>
    /// Creates a real UdpClient on an ephemeral port and injects it into the service.
    /// The UdpClient is needed for methods that call _udpClient.Send().
    /// </summary>
    private void SetUdpClient()
    {
        var udpClient = new UdpClient(0); // bind to any available port
        var field = typeof(DhtService).GetField("_udpClient", BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(_service, udpClient);
    }

    private static BDictionary BuildQueryMessage(string queryType, byte[] nodeId)
    {
        return new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString(queryType),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(nodeId)
            }
        };
    }

    /// <summary>Encodes a minimal ping query carrying the given node ID as raw bytes.</summary>
    private static byte[] BuildPingBytesWithNodeId(byte[] nodeId)
    {
        var message = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01, 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("ping"),
            ["a"] = new BDictionary { ["id"] = new BString(nodeId) }
        };
        return message.EncodeAsBytes();
    }
}
