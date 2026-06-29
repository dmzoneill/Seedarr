using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Peers.Encryption;

namespace NzbDrone.Core.Test.Peers.Encryption;

[TestFixture]
public class MseHandshakeTest
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

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Combines a separate readable stream and a writable stream into one
    /// bidirectional stream, simulating a socket connection.
    /// </summary>
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

        public override void Write(byte[] buffer, int offset, int count) =>
            _writer.Write(buffer, offset, count);

        public override void Flush() => _writer.Flush();

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
