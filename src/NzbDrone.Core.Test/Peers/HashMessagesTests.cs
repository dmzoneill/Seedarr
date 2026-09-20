using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Messages;
using NzbDrone.Core.Torrents.Merkle;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class HashMessagesTests
{
    private byte[] _testRoot;
    private byte[] _testHash1;
    private byte[] _testHash2;

    [SetUp]
    public void SetUp()
    {
        _testRoot = new byte[32];
        _testHash1 = new byte[32];
        _testHash2 = new byte[32];

        for (var i = 0; i < 32; i++)
        {
            _testRoot[i] = (byte)(i + 1);
            _testHash1[i] = (byte)(i + 10);
            _testHash2[i] = (byte)(i + 20);
        }
    }

    [Test]
    public void HashRequestMessage_EncodingAndDecoding_ExactBytes()
    {
        var request = new HashRequestMessage(_testRoot, baseLayer: 0, index: 0, length: 2, proofLayers: 4);
        var bytes = request.ToBytes();

        Assert.That(bytes.Length, Is.EqualTo(48));

        // Exact byte verification
        Assert.That(bytes[..32], Is.EqualTo(_testRoot));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(32, 4)), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(36, 4)), Is.EqualTo(0));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(40, 4)), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(44, 4)), Is.EqualTo(4));

        var decoded = HashRequestMessage.FromBytes(bytes);
        Assert.That(decoded.PiecesRoot, Is.EqualTo(_testRoot));
        Assert.That(decoded.BaseLayer, Is.EqualTo(0));
        Assert.That(decoded.BaseLayerShort, Is.EqualTo(0));
        Assert.That(decoded.Index, Is.EqualTo(0));
        Assert.That(decoded.Length, Is.EqualTo(2));
        Assert.That(decoded.ProofLayers, Is.EqualTo(4));
        Assert.That(decoded.ProofLayersShort, Is.EqualTo(4));
        Assert.That(decoded.IsValid(), Is.True);

        var peerMsg = request.ToPeerMessage();
        Assert.That(peerMsg.Type, Is.EqualTo(PeerMessageType.HashRequest));
        Assert.That(peerMsg.Payload, Is.EqualTo(bytes));

        var fromPeer = HashRequestMessage.FromPeerMessage(peerMsg);
        Assert.That(fromPeer.PiecesRoot, Is.EqualTo(_testRoot));
    }

    [Test]
    public void HashesMessage_EncodingAndDecoding_ExactBytes()
    {
        var combinedHashes = new byte[64];
        Array.Copy(_testHash1, 0, combinedHashes, 0, 32);
        Array.Copy(_testHash2, 0, combinedHashes, 32, 32);

        var msg = new HashesMessage(_testRoot, baseLayer: 1, index: 4, length: 2, proofLayers: 2, combinedHashes);
        var bytes = msg.ToBytes();

        Assert.That(bytes.Length, Is.EqualTo(48 + 64));
        Assert.That(msg.HashCount, Is.EqualTo(2));

        var hashesList = msg.GetHashesList();
        Assert.That(hashesList.Count, Is.EqualTo(2));
        Assert.That(hashesList[0], Is.EqualTo(_testHash1));
        Assert.That(hashesList[1], Is.EqualTo(_testHash2));

        var decoded = HashesMessage.FromBytes(bytes);
        Assert.That(decoded.PiecesRoot, Is.EqualTo(_testRoot));
        Assert.That(decoded.BaseLayer, Is.EqualTo(1));
        Assert.That(decoded.Index, Is.EqualTo(4));
        Assert.That(decoded.Length, Is.EqualTo(2));
        Assert.That(decoded.ProofLayers, Is.EqualTo(2));
        Assert.That(decoded.Hashes, Is.EqualTo(combinedHashes));
        Assert.That(decoded.HashCount, Is.EqualTo(2));
        Assert.That(decoded.IsValid(), Is.True);

        var peerMsg = msg.ToPeerMessage();
        Assert.That(peerMsg.Type, Is.EqualTo(PeerMessageType.Hashes));
        Assert.That(peerMsg.Payload, Is.EqualTo(bytes));

        var fromPeer = HashesMessage.FromPeerMessage(peerMsg);
        Assert.That(fromPeer.Hashes, Is.EqualTo(combinedHashes));
    }

    [Test]
    public void HashRejectMessage_EncodingAndDecoding_ExactBytes()
    {
        var reject = new HashRejectMessage(_testRoot, baseLayer: 0, index: 8, length: 4, proofLayers: 3);
        var bytes = reject.ToBytes();

        Assert.That(bytes.Length, Is.EqualTo(48));

        var decoded = HashRejectMessage.FromBytes(bytes);
        Assert.That(decoded.PiecesRoot, Is.EqualTo(_testRoot));
        Assert.That(decoded.BaseLayer, Is.EqualTo(0));
        Assert.That(decoded.Index, Is.EqualTo(8));
        Assert.That(decoded.Length, Is.EqualTo(4));
        Assert.That(decoded.ProofLayers, Is.EqualTo(3));
        Assert.That(decoded.IsValid(), Is.True);

        var peerMsg = reject.ToPeerMessage();
        Assert.That(peerMsg.Type, Is.EqualTo(PeerMessageType.HashReject));
        Assert.That(peerMsg.Payload, Is.EqualTo(bytes));

        var fromPeer = HashRejectMessage.FromPeerMessage(peerMsg);
        Assert.That(fromPeer.PiecesRoot, Is.EqualTo(_testRoot));

        // Copy constructor from request
        var req = new HashRequestMessage(_testRoot, 0, 8, 4, 3);
        var rejectFromReq = new HashRejectMessage(req);
        Assert.That(rejectFromReq.Index, Is.EqualTo(req.Index));
        Assert.That(rejectFromReq.Length, Is.EqualTo(req.Length));
    }

    [Test]
    public void MalformedPayloads_ThrowAppropriateExceptions()
    {
        // Truncated HashRequest
        Assert.Throws<ArgumentException>(() => HashRequestMessage.FromBytes(new byte[47]));

        // Truncated HashesMessage
        Assert.Throws<ArgumentException>(() => HashesMessage.FromBytes(new byte[40]));

        // HashesMessage with partial hash (not multiple of 32)
        Assert.Throws<ArgumentException>(() => HashesMessage.FromBytes(new byte[48 + 31]));

        // Truncated HashReject
        Assert.Throws<ArgumentException>(() => HashRejectMessage.FromBytes(new byte[47]));

        // Mismatched peer message types
        var wrongMsg = new PeerMessage { Type = PeerMessageType.Choke, Payload = new byte[48] };
        Assert.Throws<ArgumentException>(() => HashRequestMessage.FromPeerMessage(wrongMsg));
        Assert.Throws<ArgumentException>(() => HashesMessage.FromPeerMessage(wrongMsg));
        Assert.Throws<ArgumentException>(() => HashRejectMessage.FromPeerMessage(wrongMsg));
    }

    [Test]
    public void Validation_InvalidRanges()
    {
        // Length not a power of two
        var req1 = new HashRequestMessage(_testRoot, 0, 0, 3, 1);
        Assert.That(req1.IsValid(), Is.False);

        // Index not multiple of length
        var req2 = new HashRequestMessage(_testRoot, 0, 3, 2, 1);
        Assert.That(req2.IsValid(), Is.False);

        // Length < 2
        var req3 = new HashRequestMessage(_testRoot, 0, 0, 1, 1);
        Assert.That(req3.IsValid(), Is.False);

        // Negative layer
        var req4 = new HashRequestMessage(_testRoot, -1, 0, 2, 1);
        Assert.That(req4.IsValid(), Is.False);

        // Valid request
        var req5 = new HashRequestMessage(_testRoot, 0, 4, 4, 2);
        Assert.That(req5.IsValid(), Is.True);
    }

    [Test]
    public void MerkleTree_UncleProofGeneration_And_HashesVerification()
    {
        // 64 KiB = 4 blocks of 16 KiB
        var data = new byte[64 * 1024];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i & 0xFF);
        }

        var tree = MerkleTree.Create(data);
        Assert.That(tree.LeafHashes.Count, Is.EqualTo(4));

        // Request baseLayer = 0, index = 0, length = 2, proofLayers = 1
        // Length = 2 has impliedLayers = 1. With proofLayers = 1, exactly 1 uncle hash is returned.
        // Total hashes: 2 base + 1 uncle = 3 hashes (96 bytes).
        var hashesData = tree.GetHashesForRequest(baseLayer: 0, index: 0, length: 2, proofLayers: 1);
        Assert.That(hashesData, Is.Not.Null);
        Assert.That(hashesData.Length, Is.EqualTo(3 * 32));

        var hashesMsg = new HashesMessage(tree.RootHash, 0, 0, 2, 1, hashesData);
        Assert.That(hashesMsg.HashCount, Is.EqualTo(3));

        // Verify hashes against file root
        var isValid = hashesMsg.VerifyAgainstRoot(tree.RootHash);
        Assert.That(isValid, Is.True);

        // Tamper with one byte of the uncle proof
        hashesData[64] ^= 0xFF;
        var tamperedMsg = new HashesMessage(tree.RootHash, 0, 0, 2, 1, hashesData);
        Assert.That(tamperedMsg.VerifyAgainstRoot(tree.RootHash), Is.False);
    }

    [Test]
    public void Leechers_IncorporateReceivedHashes_ToVerify16KiBLeafBlocks()
    {
        var data = new byte[64 * 1024];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 3) & 0xFF);
        }

        var tree = MerkleTree.Create(data);

        // Request leaf hashes for index 0..1 (first two blocks) with proof up to root
        var hashesData = tree.GetHashesForRequest(0, 0, 2, 1);
        var hashesMsg = new HashesMessage(tree.RootHash, 0, 0, 2, 1, hashesData);
        Assert.That(hashesMsg.VerifyAgainstRoot(tree.RootHash), Is.True);

        // Extract verified leaf hashes
        var verifiedLeaves = hashesMsg.GetHashesList();
        Assert.That(verifiedLeaves.Count, Is.GreaterThanOrEqualTo(2));

        // Block 0 data (16 KiB)
        var block0 = new byte[16384];
        Array.Copy(data, 0, block0, 0, 16384);
        var block0Hash = SHA256.HashData(block0);
        Assert.That(block0Hash, Is.EqualTo(verifiedLeaves[0]));

        // Block 1 data (16 KiB)
        var block1 = new byte[16384];
        Array.Copy(data, 16384, block1, 0, 16384);
        var block1Hash = SHA256.HashData(block1);
        Assert.That(block1Hash, Is.EqualTo(verifiedLeaves[1]));

        // Corrupted block fails leaf hash check
        block0[0] ^= 0xEE;
        var corruptedHash = SHA256.HashData(block0);
        Assert.That(corruptedHash, Is.Not.EqualTo(verifiedLeaves[0]));
    }

    [Test]
    public void PeerConnection_HandleMessage_SeederRespondsWithHashes()
    {
        var data = new byte[32 * 1024]; // 2 blocks
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);

        var tree = MerkleTree.Create(data);

        using var stream = new MemoryStream();
        using var connection = new PeerConnection(stream, "127.0.0.1", 6881);
        connection.SupportsV2Protocol = true;
        connection.RegisterMerkleTree(tree.RootHash, tree);

        HashesMessage receivedHashes = null;
        HashRejectMessage receivedReject = null;

        connection.OnHashesReceived = msg => receivedHashes = msg;
        connection.OnHashRejectReceived = msg => receivedReject = msg;

        // Seeder handles incoming HashRequest
        var req = new HashRequestMessage(tree.RootHash, 0, 0, 2, 1);
        connection.HandleMessage(req.ToPeerMessage());

        // Seeder wrote response message to stream
        stream.Seek(0, SeekOrigin.Begin);
        var responseMsg = connection.ReceiveMessage();

        Assert.That(responseMsg, Is.Not.Null);
        Assert.That(responseMsg.Type, Is.EqualTo(PeerMessageType.Hashes));

        var responseHashes = HashesMessage.FromPeerMessage(responseMsg);
        Assert.That(responseHashes.VerifyAgainstRoot(tree.RootHash), Is.True);
    }

    [Test]
    public void PeerConnection_HandleMessage_InvalidOrMissingTree_RespondsWithHashReject()
    {
        using var stream = new MemoryStream();
        using var connection = new PeerConnection(stream, "127.0.0.1", 6881);
        connection.SupportsV2Protocol = true;

        // Unknown PiecesRoot
        var req = new HashRequestMessage(_testRoot, 0, 0, 2, 1);
        connection.HandleMessage(req.ToPeerMessage());

        stream.Seek(0, SeekOrigin.Begin);
        var responseMsg = connection.ReceiveMessage();

        Assert.That(responseMsg, Is.Not.Null);
        Assert.That(responseMsg.Type, Is.EqualTo(PeerMessageType.HashReject));

        var responseReject = HashRejectMessage.FromPeerMessage(responseMsg);
        Assert.That(responseReject.PiecesRoot, Is.EqualTo(_testRoot));
        Assert.That(responseReject.Index, Is.EqualTo(0));
    }

    [Test]
    public void PeerConnection_HashesReceived_DispatchesCallbackAndEvent()
    {
        using var stream = new MemoryStream();
        using var connection = new PeerConnection(stream, "127.0.0.1", 6881);

        HashesMessage callbackResult = null;
        HashesMessage eventResult = null;

        connection.OnHashesReceived = msg => callbackResult = msg;
        connection.HashesReceived += (sender, msg) => eventResult = msg;

        var combinedHashes = new byte[64];
        Array.Copy(_testHash1, 0, combinedHashes, 0, 32);
        Array.Copy(_testHash2, 0, combinedHashes, 32, 32);

        var hashesMsg = new HashesMessage(_testRoot, 0, 0, 2, 0, combinedHashes);
        connection.HandleMessage(hashesMsg.ToPeerMessage());

        Assert.That(callbackResult, Is.Not.Null);
        Assert.That(callbackResult.PiecesRoot, Is.EqualTo(_testRoot));
        Assert.That(eventResult, Is.Not.Null);
        Assert.That(eventResult.PiecesRoot, Is.EqualTo(_testRoot));
    }

    [Test]
    public void HybridSwarm_ProtocolNegotiation_ReflectsV2Support()
    {
        using var stream = new MemoryStream();
        using var connection = new PeerConnection(stream, "127.0.0.1", 6881);

        // Initially false
        Assert.That(connection.SupportsV2Protocol, Is.False);
        Assert.That(connection.SupportsV2, Is.False);

        // Negotiated BEP 52 capability
        connection.SupportsV2Protocol = true;
        Assert.That(connection.SupportsV2Protocol, Is.True);
        Assert.That(connection.SupportsV2, Is.True);
        Assert.That(connection.SupportsBep52, Is.True);

        // Fallback to legacy v1
        connection.SupportsV2Protocol = false;
        Assert.That(connection.SupportsV2Protocol, Is.False);
        Assert.That(connection.SupportsBep52, Is.False);
    }
}
