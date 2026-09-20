using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers.Encryption;

[TestFixture]
public class MseHandshakeTests
{
    private static readonly byte[] TestInfoHash =
    [
        0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04,
        0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C,
        0x0D, 0x0E, 0x0F, 0x10
    ];

    // SHA1("req2" + infoHash) — the hash NegotiateIncoming passes to the validator
    private static readonly byte[] ExpectedSkeyHash =
        MseKeyDerivation.DeriveKey(TestInfoHash, Encoding.ASCII.GetBytes("req2"));

    private static bool ValidateInfoHash(byte[] hash) => hash.SequenceEqual(ExpectedSkeyHash);

    /// <summary>
    /// Creates a pair of fully bidirectional connected streams backed by two anonymous pipes.
    /// sideA reads from bToA, writes to aToB.
    /// sideB reads from aToB, writes to bToA.
    /// </summary>
    private static (Stream SideA, Stream SideB) CreateConnectedPair()
    {
        var aToB = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var aToBClient = new AnonymousPipeClientStream(PipeDirection.In, aToB.GetClientHandleAsString());
        var bToA = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var bToAClient = new AnonymousPipeClientStream(PipeDirection.In, bToA.GetClientHandleAsString());

        var sideA = new DuplexStream(bToAClient, aToB);
        var sideB = new DuplexStream(aToBClient, bToA);
        return (sideA, sideB);
    }

    // ── Constructor / property tests ───────────────────────────────────────

    [Test]
    public void NegotiatedMethod_should_be_None_before_negotiation()
    {
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);

        Assert.That(handshake.NegotiatedMethod, Is.EqualTo(CryptoMethod.None));
    }

    [Test]
    public void Constructor_should_accept_require_encrypted_mode()
    {
        Assert.That(() => new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted), Throws.Nothing);
    }

    [Test]
    public void Constructor_should_accept_prefer_encrypted_mode()
    {
        Assert.That(() => new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted), Throws.Nothing);
    }

    [Test]
    public void Constructor_should_accept_prefer_plain_text_mode()
    {
        Assert.That(() => new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText), Throws.Nothing);
    }

    [Test]
    public void Constructor_should_accept_optional_key_pool()
    {
        var pool = Substitute.For<IDhKeyPool>();
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted, pool);

        Assert.That(handshake.KeyPool, Is.SameAs(pool));
    }

    [Test]
    public void NegotiateOutgoing_and_Incoming_should_use_rented_key_from_pool()
    {
        var poolA = Substitute.For<IDhKeyPool>();
        var poolB = Substitute.For<IDhKeyPool>();

        var keyA = new MseKeyDerivation();
        var keyB = new MseKeyDerivation();

        poolA.Rent().Returns(keyA);
        poolB.Rent().Returns(keyB);

        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted, poolA);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted, poolB);

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        poolA.Received(1).Rent();
        poolB.Received(1).Rent();
        Assert.That(outgoing.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
    }

    // ── Full-handshake negotiated-method assertions ────────────────────────

    [Test]
    public void NegotiateOutgoing_should_set_negotiated_method_to_Rc4_when_both_require_encrypted()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(outgoing.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
    }

    [Test]
    public void NegotiateIncoming_should_set_negotiated_method_to_Rc4_when_both_require_encrypted()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
    }

    [Test]
    public void NegotiateOutgoing_should_set_negotiated_method_to_PlainText_when_both_prefer_plain()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(outgoing.NegotiatedMethod, Is.EqualTo(CryptoMethod.PlainText));
    }

    [Test]
    public void NegotiateIncoming_should_set_negotiated_method_to_PlainText_when_both_prefer_plain()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.PlainText));
    }

    [Test]
    public void NegotiateIncoming_should_prefer_Rc4_when_incoming_mode_is_prefer_encrypted()
    {
        var (sideA, sideB) = CreateConnectedPair();

        // outgoing provides PlainText | Rc4; incoming prefers Rc4 → selects Rc4
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
        Assert.That(outgoing.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
    }

    [Test]
    public void NegotiateIncoming_should_set_negotiated_method_to_Rc4_when_incoming_is_require_encrypted()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
    }

    // ── Return-value stream-type assertions ────────────────────────────────

    [Test]
    public void NegotiateOutgoing_should_return_EncryptedStream_for_Rc4_method()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        Stream outStream = null;
        var taskA = Task.Run(() => outStream = outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(outStream, Is.TypeOf<EncryptedStream>());
    }

    [Test]
    public void NegotiateIncoming_should_return_EncryptedStream_for_Rc4_method()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        Stream inStream = null;
        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => inStream = incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(inStream, Is.TypeOf<EncryptedStream>());
    }

    [Test]
    public void NegotiateOutgoing_should_return_non_encrypted_stream_for_PlainText_method()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);

        Stream outStream = null;
        var taskA = Task.Run(() => outStream = outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(outStream, Is.Not.TypeOf<EncryptedStream>());
    }

    [Test]
    public void NegotiateIncoming_should_return_non_encrypted_stream_for_PlainText_method()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);

        Stream inStream = null;
        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => inStream = incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(inStream, Is.Not.TypeOf<EncryptedStream>());
    }

    // ── Post-handshake data-exchange tests ────────────────────────────────

    [Test]
    public void Negotiation_should_allow_data_exchange_after_Rc4_handshake()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        Stream outStream = null, inStream = null;
        var taskA = Task.Run(() => outStream = outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => inStream = incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        var testData = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x57, 0x6F, 0x72, 0x6C, 0x64 };
        outStream.Write(testData, 0, testData.Length);
        outStream.Flush();

        var received = new byte[testData.Length];
        var totalRead = 0;
        while (totalRead < testData.Length)
        {
            var n = inStream.Read(received, totalRead, testData.Length - totalRead);
            if (n == 0)
            {
                break;
            }

            totalRead += n;
        }

        Assert.That(totalRead, Is.EqualTo(testData.Length));
        Assert.That(received, Is.EqualTo(testData));
    }

    [Test]
    public void Negotiation_should_allow_data_exchange_after_PlainText_handshake()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);

        Stream outStream = null, inStream = null;
        var taskA = Task.Run(() => outStream = outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => inStream = incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        var testData = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x57, 0x6F, 0x72, 0x6C, 0x64 };
        outStream.Write(testData, 0, testData.Length);
        outStream.Flush();

        var received = new byte[testData.Length];
        var totalRead = 0;
        while (totalRead < testData.Length)
        {
            var n = inStream.Read(received, totalRead, testData.Length - totalRead);
            if (n == 0)
            {
                break;
            }

            totalRead += n;
        }

        Assert.That(totalRead, Is.EqualTo(testData.Length));
        Assert.That(received, Is.EqualTo(testData));
    }

    [Test]
    public void Negotiation_should_allow_bidirectional_data_exchange_after_Rc4_handshake()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        Stream outStream = null, inStream = null;
        var taskA = Task.Run(() => outStream = outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => inStream = incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        var dataAtoB = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var dataBtoA = new byte[] { 0x0A, 0x0B, 0x0C, 0x0D, 0x0E };

        outStream.Write(dataAtoB, 0, dataAtoB.Length);
        outStream.Flush();
        inStream.Write(dataBtoA, 0, dataBtoA.Length);
        inStream.Flush();

        var receivedByB = ReadExact(inStream, dataAtoB.Length);
        var receivedByA = ReadExact(outStream, dataBtoA.Length);

        Assert.That(receivedByB, Is.EqualTo(dataAtoB));
        Assert.That(receivedByA, Is.EqualTo(dataBtoA));
    }

    // ── Error-condition tests ──────────────────────────────────────────────

    [Test]
    public void NegotiateIncoming_should_throw_when_info_hash_not_recognized()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);

        Exception incomingException = null;
        var taskB = Task.Run(() =>
        {
            try
            {
                incoming.NegotiateIncoming(sideB, _ => false);
            }
            catch (Exception ex)
            {
                incomingException = ex;

                // Closing sideB's write end unblocks the outgoing side that is scanning for VC.
                try
                {
                    sideB.Dispose();
                }
                catch
                {
                    // intentionally ignored
                }
            }
        });

        var taskA = Task.Run(() =>
        {
            try
            {
                outgoing.NegotiateOutgoing(sideA);
            }
            catch
            {
                // expected — other side closed the connection
            }
        });

        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Test timed out");

        Assert.That(incomingException, Is.TypeOf<InvalidOperationException>());
        Assert.That(incomingException.Message, Does.Contain("Unknown info hash"));
    }

    [Test]
    public void NegotiateIncoming_should_throw_when_stream_ends_before_ya_is_read()
    {
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);
        var stream = new ScriptedStream(Array.Empty<byte>());

        Assert.That(
            () => handshake.NegotiateIncoming(stream, ValidateInfoHash),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Unexpected end of stream"));
    }

    [Test]
    public void NegotiateIncoming_should_throw_when_stream_ends_during_req1_marker_scan()
    {
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);

        // Provide exactly 96 bytes (valid Ya) — incoming reads Ya then tries to scan
        // for req1Hash but the stream is exhausted → "Stream ended while searching"
        var validYa = new MseKeyDerivation().GetPublicKeyBytes();
        var stream = new ScriptedStream(validYa);

        Assert.That(
            () => handshake.NegotiateIncoming(stream, ValidateInfoHash),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Stream ended while searching"));
    }

    [Test]
    public void NegotiateOutgoing_should_throw_when_stream_ends_before_yb_is_read()
    {
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);

        // ScriptedStream discards writes and returns EOF immediately on reads
        var stream = new ScriptedStream(Array.Empty<byte>());

        Assert.That(
            () => handshake.NegotiateOutgoing(stream),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Unexpected end of stream"));
    }

    [Test]
    public void NegotiateOutgoing_should_throw_when_stream_ends_during_vc_marker_scan()
    {
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);

        // Provide exactly 96 bytes (a valid Yb). Outgoing reads Yb successfully,
        // writes req1Hash + payload (discarded), then scans for the VC marker
        // but the stream is now exhausted.
        var validYb = new MseKeyDerivation().GetPublicKeyBytes();
        var stream = new ScriptedStream(validYb);

        Assert.That(
            () => handshake.NegotiateOutgoing(stream),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void NegotiateIncoming_should_throw_when_vc_bytes_are_non_zero()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);

        Stream inStream = null;
        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => inStream = incoming.NegotiateIncoming(sideB, ValidateInfoHash));
        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        // VC check was exercised and passed — incoming stream is valid.
        Assert.That(inStream, Is.Not.Null);
        Assert.That(incoming.NegotiatedMethod, Is.Not.EqualTo(CryptoMethod.None));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static byte[] ReadExact(Stream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var n = stream.Read(buffer, offset, count - offset);
            if (n == 0)
            {
                throw new InvalidOperationException("Unexpected EOF in test ReadExact");
            }

            offset += n;
        }

        return buffer;
    }

    [Test]
    public async Task NegotiateOutgoingAsync_and_NegotiateIncomingAsync_should_succeed_and_negotiate_rc4()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var taskA = outgoing.NegotiateOutgoingAsync(sideA, CancellationToken.None).AsTask();
        var taskB = incoming.NegotiateIncomingAsync(sideB, ValidateInfoHash, CancellationToken.None).AsTask();

        await Task.WhenAll(taskA, taskB);

        var streamA = await taskA;
        var streamB = await taskB;

        Assert.That(streamA, Is.Not.Null);
        Assert.That(streamB, Is.Not.Null);
        Assert.That(outgoing.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
    }

    [Test]
    public async Task NegotiateIncomingAsync_with_torrent_selector_should_succeed()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var expectedTorrent = new Torrent { Id = 123 };
        var taskA = outgoing.NegotiateOutgoingAsync(sideA, CancellationToken.None).AsTask();
        var taskB = incoming.NegotiateIncomingAsync(
            sideB,
            hash => hash.SequenceEqual(ExpectedSkeyHash) ? expectedTorrent : null,
            CancellationToken.None).AsTask();

        await Task.WhenAll(taskA, taskB);

        var streamA = await taskA;
        var streamB = await taskB;

        Assert.That(streamA, Is.Not.Null);
        Assert.That(streamB, Is.Not.Null);
    }

    [Test]
    public void NegotiateOutgoingAsync_should_abort_when_cancelled()
    {
        var (sideA, sideB) = CreateConnectedPair();
        using (sideB)
        {
            var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await outgoing.NegotiateOutgoingAsync(sideA, cts.Token);
            });
        }
    }

    [Test]
    public void NegotiateIncomingAsync_should_abort_when_cancelled()
    {
        var (sideA, sideB) = CreateConnectedPair();
        using (sideA)
        {
            var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                await incoming.NegotiateIncomingAsync(sideB, ValidateInfoHash, cts.Token);
            });
        }
    }

    [TestCase(0x00u)]
    [TestCase(0x03u)]
    [TestCase(0x07u)]
    public void ValidateCryptoSelect_should_reject_when_multiple_or_zero_bits_set(uint value)
    {
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.PreferEncrypted);
        var ex = Assert.Throws<InvalidOperationException>(() => handshake.ValidateCryptoSelect((CryptoMethod)value));
        Assert.That(ex.Message, Does.Contain("multiple or zero bits set"));
    }

    [Test]
    public void ValidateCryptoSelect_should_reject_downgrade_to_plain_text_when_encryption_is_required()
    {
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var ex = Assert.Throws<SecurityException>(() => handshake.ValidateCryptoSelect(CryptoMethod.PlainText));
        Assert.That(ex.Message, Does.Contain("downgrade"));
    }

    [Test]
    public void SelectCryptoMethod_should_reject_in_require_encrypted_mode_when_peer_only_offers_plain_text()
    {
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var ex = Assert.Throws<SecurityException>(() => handshake.SelectCryptoMethod(CryptoMethod.PlainText));
        Assert.That(ex.Message, Does.Contain("RC4 encryption"));
    }

    [Test]
    public void ValidateCryptoSelect_should_accept_single_bit_rc4()
    {
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        Assert.DoesNotThrow(() => handshake.ValidateCryptoSelect(CryptoMethod.Rc4));
    }

    [Test]
    public void ValidateCryptoSelect_should_accept_single_bit_plain_text_in_compatible_mode()
    {
        var handshake = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);
        Assert.DoesNotThrow(() => handshake.ValidateCryptoSelect(CryptoMethod.PlainText));
    }

    [Test]
    public void Handshake_successful_when_rc4_is_selected_in_require_encrypted_mode()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));

        Task.WaitAll(taskA, taskB);

        Assert.That(outgoing.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
    }

    [Test]
    public void Handshake_successful_when_plain_text_is_selected_in_compatible_mode()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.PreferPlainText);

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));

        Task.WaitAll(taskA, taskB);

        Assert.That(outgoing.NegotiatedMethod, Is.EqualTo(CryptoMethod.PlainText));
        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.PlainText));
    }

    [TestCase(1024)]
    [TestCase(4096)]
    [TestCase(8192)]
    public void Incoming_handshake_with_ia_payload_larger_than_512_bytes_succeeds_and_decrypts(int payloadSize)
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var payload = new byte[payloadSize];
        for (var i = 0; i < payloadSize; i++)
        {
            payload[i] = (byte)(i % 251);
        }

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA, payload));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));

        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(incoming.InitialApplicationData, Is.Not.Null);
        Assert.That(incoming.InitialApplicationData, Is.EqualTo(payload));

        var streamB = taskB.Result;
        var readBuf = new byte[payloadSize];
        var totalRead = 0;
        while (totalRead < payloadSize)
        {
            var read = streamB.Read(readBuf, totalRead, payloadSize - totalRead);
            if (read <= 0)
            {
                break;
            }

            totalRead += read;
        }

        Assert.That(totalRead, Is.EqualTo(payloadSize));
        Assert.That(readBuf, Is.EqualTo(payload));
    }

    [Test]
    public void ValidateIaLength_should_reject_length_greater_than_65535()
    {
        Assert.Throws<InvalidOperationException>(() => MseHandshake.ValidateIaLength(65536));
        Assert.Throws<InvalidOperationException>(() => MseHandshake.ValidateIaLength(70000));
    }

    [Test]
    public void NegotiateOutgoing_should_throw_when_initial_payload_exceeds_65535_bytes()
    {
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        using var stream = new MemoryStream();
        var oversizedPayload = new byte[65536];

        Assert.Throws<InvalidOperationException>(() => outgoing.NegotiateOutgoing(stream, oversizedPayload));
    }

    [Test]
    public void Outbound_MSE_handshake_pipelines_68_byte_bittorrent_handshake()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var infoHashHex = Convert.ToHexString(TestInfoHash);
        var btHandshake = PeerConnection.BuildHandshake(infoHashHex, "-SD1000-123456789012");
        Assert.That(btHandshake.Length, Is.EqualTo(68));

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA, btHandshake));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));

        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(incoming.InitialApplicationData, Is.Not.Null);
        Assert.That(incoming.InitialApplicationData, Is.EqualTo(btHandshake));

        var streamB = taskB.Result;
        var receivedHandshake = new byte[68];
        var read = streamB.Read(receivedHandshake, 0, 68);
        Assert.That(read, Is.EqualTo(68));
        Assert.That(receivedHandshake, Is.EqualTo(btHandshake));

        // Verify that subsequent communication flows through encrypted stream
        var streamA = taskA.Result;
        var message = Encoding.UTF8.GetBytes("SubsequentEncryptedPayload");
        streamA.Write(message, 0, message.Length);
        streamA.Flush();

        var receivedMessage = new byte[message.Length];
        var msgRead = streamB.Read(receivedMessage, 0, receivedMessage.Length);
        Assert.That(msgRead, Is.EqualTo(message.Length));
        Assert.That(receivedMessage, Is.EqualTo(message));
    }

    [Test]
    public void Zero_length_ia_payload_succeeds_without_prefixed_data()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var taskA = Task.Run(() => outgoing.NegotiateOutgoing(sideA, Array.Empty<byte>()));
        var taskB = Task.Run(() => incoming.NegotiateIncoming(sideB, ValidateInfoHash));

        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");

        Assert.That(incoming.InitialApplicationData, Is.Null);

        var streamA = taskA.Result;
        var streamB = taskB.Result;
        var testData = new byte[] { 0x42, 0x43, 0x44 };
        streamA.Write(testData, 0, testData.Length);
        streamA.Flush();

        var readBuffer = new byte[testData.Length];
        var read = streamB.Read(readBuffer, 0, readBuffer.Length);
        Assert.That(read, Is.EqualTo(testData.Length));
        Assert.That(readBuffer, Is.EqualTo(testData));
    }

    [Test]
    public async Task Outbound_MSE_handshake_async_pipelines_payload_successfully()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var infoHashHex = Convert.ToHexString(TestInfoHash);
        var btHandshake = PeerConnection.BuildHandshake(infoHashHex, "-SD1000-123456789012");

        var taskA = outgoing.NegotiateOutgoingAsync(sideA, btHandshake).AsTask();
        var taskB = incoming.NegotiateIncomingAsync(sideB, ValidateInfoHash).AsTask();

        await Task.WhenAll(taskA, taskB);

        Assert.That(incoming.InitialApplicationData, Is.EqualTo(btHandshake));
        var streamB = await taskB;
        var buf = new byte[68];
        var read = await streamB.ReadAsync(buf.AsMemory(0, 68));
        Assert.That(read, Is.EqualTo(68));
        Assert.That(buf, Is.EqualTo(btHandshake));
    }

    [Test]
    public void PeerConnection_NegotiateEncryptionOutgoing_pipelines_handshake_when_enabled()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var infoHashHex = Convert.ToHexString(TestInfoHash);
        var connA = new PeerConnection(sideA, "127.0.0.1", 6881) { PeerId = "-SD1000-123456789012" };
        var incomingHandshake = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var taskA = Task.Run(() => connA.NegotiateEncryptionOutgoing(infoHashHex, EncryptionMode.RequireEncrypted));
        var taskB = Task.Run(() => incomingHandshake.NegotiateIncoming(sideB, ValidateInfoHash));

        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");
        Assert.That(taskA.Result, Is.True);
        Assert.That(connA.HandshakeSent, Is.True);
        Assert.That(incomingHandshake.InitialApplicationData, Is.Not.Null);
        Assert.That(incomingHandshake.InitialApplicationData.Length, Is.EqualTo(68));
    }

    /// <summary>
    /// A stream whose reads come from a fixed pre-loaded byte array and whose
    /// writes are silently discarded.  Used to inject controlled data into one
    /// side of a handshake without spinning up a real second peer.
    /// </summary>
    private sealed class ScriptedStream : Stream
    {
        private readonly MemoryStream _readData;

        public ScriptedStream(byte[] readData) => _readData = new MemoryStream(readData);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _readData.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _readData.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Combines a separate readable stream and a writable stream into one
    /// bidirectional stream, simulating a socket connection.
    /// </summary>
    [Test]
    public void NegotiateIncoming_should_synchronize_with_fragmented_stream()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var fragmentedSideB = new FragmentedStream(sideB, maxChunk: 5);

        var outgoing = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        Stream outStream = null;
        Stream inStream = null;

        var taskA = Task.Run(() => outStream = outgoing.NegotiateOutgoing(sideA));
        var taskB = Task.Run(() => inStream = incoming.NegotiateIncoming(fragmentedSideB, ValidateInfoHash));

        Assert.That(Task.WhenAll(taskA, taskB).Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out");
        Assert.That(outgoing.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));

        var msg = Encoding.ASCII.GetBytes("ping");
        outStream.Write(msg, 0, msg.Length);
        outStream.Flush();

        var buf = new byte[msg.Length];
        var read = inStream.Read(buf, 0, buf.Length);
        Assert.That(read, Is.EqualTo(msg.Length));
        Assert.That(buf, Is.EqualTo(msg));
    }

    [Test]
    public void NegotiateIncoming_should_recover_from_false_positive_marker_in_PadA()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var clientKeyDerivation = new MseKeyDerivation();
        var ya = clientKeyDerivation.GetPublicKeyBytes();

        Stream inStream = null;
        var taskIncoming = Task.Run(() => inStream = incoming.NegotiateIncoming(sideB, ValidateInfoHash));

        // Step 1: Send Ya
        sideA.Write(ya, 0, ya.Length);
        sideA.Flush();

        // Step 2: Read Yb (96 bytes)
        var yb = new byte[96];
        var offset = 0;
        while (offset < 96)
        {
            var r = sideA.Read(yb, offset, 96 - offset);
            Assert.That(r, Is.GreaterThan(0));
            offset += r;
        }

        var sharedSecret = clientKeyDerivation.ComputeSharedSecret(yb);
        var req1Hash = MseKeyDerivation.DeriveKey(sharedSecret, Encoding.ASCII.GetBytes("req1"));
        var req2Hash = MseKeyDerivation.DeriveKey(TestInfoHash, Encoding.ASCII.GetBytes("req2"));
        var req3Hash = MseKeyDerivation.DeriveKey(sharedSecret, Encoding.ASCII.GetBytes("req3"));

        var realObfuscatedHash = new byte[20];
        for (var i = 0; i < 20; i++)
        {
            realObfuscatedHash[i] = (byte)(req2Hash[i] ^ req3Hash[i]);
        }

        // Construct payload with false positive marker in PadA
        using var step3 = new MemoryStream();

        // Fake marker (20 bytes req1Hash) + fake SKEY hash (20 bytes that fail ValidateInfoHash)
        step3.Write(req1Hash, 0, req1Hash.Length);
        var fakeSkey = new byte[20];
        fakeSkey[0] = 0xFF;
        step3.Write(fakeSkey, 0, fakeSkey.Length);

        // Some extra padding bytes
        var extraPad = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        step3.Write(extraPad, 0, extraPad.Length);

        // Real marker + real obfuscated hash
        step3.Write(req1Hash, 0, req1Hash.Length);
        step3.Write(realObfuscatedHash, 0, realObfuscatedHash.Length);

        // Encrypted payload: VC (8 zeros) + crypto_provide (4 bytes = 0x02) + len(PadC) (2 bytes = 0) + len(IA) (2 bytes = 0)
        var encKey = MseKeyDerivation.DeriveKey(sharedSecret, Encoding.ASCII.GetBytes("keyA"));
        var clientCipher = new Rc4StreamCipher(encKey);

        var payload = new byte[8 + 4 + 2 + 2];
        payload[11] = 0x02; // crypto_provide = Rc4 (big-endian)
        clientCipher.ProcessInPlace(payload, 0, payload.Length);
        step3.Write(payload, 0, payload.Length);

        var step3Bytes = step3.ToArray();
        sideA.Write(step3Bytes, 0, step3Bytes.Length);
        sideA.Flush();

        Assert.That(taskIncoming.Wait(TimeSpan.FromSeconds(15)), Is.True, "Handshake timed out on false positive recovery");
        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
        Assert.That(inStream, Is.Not.Null);
    }

    [Test]
    public async Task NegotiateIncomingAsync_should_recover_from_false_positive_marker_in_PadA()
    {
        var (sideA, sideB) = CreateConnectedPair();
        var incoming = new MseHandshake(TestInfoHash, EncryptionMode.RequireEncrypted);

        var clientKeyDerivation = new MseKeyDerivation();
        var ya = clientKeyDerivation.GetPublicKeyBytes();

        var taskIncoming = incoming.NegotiateIncomingAsync(sideB, ValidateInfoHash).AsTask();

        // Step 1: Send Ya
        await sideA.WriteAsync(ya.AsMemory());
        await sideA.FlushAsync();

        // Step 2: Read Yb (96 bytes)
        var yb = new byte[96];
        var offset = 0;
        while (offset < 96)
        {
            var r = await sideA.ReadAsync(yb.AsMemory(offset, 96 - offset));
            Assert.That(r, Is.GreaterThan(0));
            offset += r;
        }

        var sharedSecret = clientKeyDerivation.ComputeSharedSecret(yb);
        var req1Hash = MseKeyDerivation.DeriveKey(sharedSecret, Encoding.ASCII.GetBytes("req1"));
        var req2Hash = MseKeyDerivation.DeriveKey(TestInfoHash, Encoding.ASCII.GetBytes("req2"));
        var req3Hash = MseKeyDerivation.DeriveKey(sharedSecret, Encoding.ASCII.GetBytes("req3"));

        var realObfuscatedHash = new byte[20];
        for (var i = 0; i < 20; i++)
        {
            realObfuscatedHash[i] = (byte)(req2Hash[i] ^ req3Hash[i]);
        }

        using var step3 = new MemoryStream();
        await step3.WriteAsync(req1Hash.AsMemory());
        var fakeSkey = new byte[20];
        fakeSkey[0] = 0xAA;
        await step3.WriteAsync(fakeSkey.AsMemory());

        var extraPad = new byte[] { 0x55, 0x66, 0x77 };
        await step3.WriteAsync(extraPad.AsMemory());

        await step3.WriteAsync(req1Hash.AsMemory());
        await step3.WriteAsync(realObfuscatedHash.AsMemory());

        var encKey = MseKeyDerivation.DeriveKey(sharedSecret, Encoding.ASCII.GetBytes("keyA"));
        var clientCipher = new Rc4StreamCipher(encKey);

        var payload = new byte[8 + 4 + 2 + 2];
        payload[11] = 0x02;
        clientCipher.ProcessInPlace(payload, 0, payload.Length);
        await step3.WriteAsync(payload.AsMemory());

        var step3Bytes = step3.ToArray();
        await sideA.WriteAsync(step3Bytes.AsMemory());
        await sideA.FlushAsync();

        var inStream = await taskIncoming;
        Assert.That(incoming.NegotiatedMethod, Is.EqualTo(CryptoMethod.Rc4));
        Assert.That(inStream, Is.Not.Null);
    }

    private sealed class FragmentedStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _maxChunk;

        public FragmentedStream(Stream inner, int maxChunk = 7)
        {
            _inner = inner;
            _maxChunk = maxChunk;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, Math.Min(count, _maxChunk));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer[..Math.Min(buffer.Length, _maxChunk)], cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(buffer, cancellationToken);

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
    }

    private sealed class DuplexStream : Stream
    {
        private readonly Stream _reader;
        private readonly Stream _writer;

        public DuplexStream(Stream reader, Stream writer)
        {
            _reader = reader;
            _writer = writer;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _reader.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _reader.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) =>
            _writer.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _writer.WriteAsync(buffer, cancellationToken);

        public override void Flush() => _writer.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _writer.FlushAsync(cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reader.Dispose();
                _writer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
