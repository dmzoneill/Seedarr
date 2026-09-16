using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Transport;

namespace NzbDrone.Core.Test.Transport;

[TestFixture]
public class UtpStreamTest
{
    private IUtpConnection _connection;
    private UtpStream _stream;

    [SetUp]
    public void SetUp()
    {
        _connection = Substitute.For<IUtpConnection>();
        _stream = new UtpStream(_connection);
    }

    [TearDown]
    public void TearDown()
    {
        _stream?.Dispose();
    }

    [Test]
    public void Constructor_WhenConnectionIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new UtpStream(null));
    }

    [Test]
    public void StreamProperties_ReflectConnectionState()
    {
        _connection.IsConnected.Returns(true);
        Assert.That(_stream.CanRead, Is.True);
        Assert.That(_stream.CanWrite, Is.True);
        Assert.That(_stream.CanSeek, Is.False);
        Assert.That(_stream.CanTimeout, Is.True);

        _connection.IsConnected.Returns(false);
        Assert.That(_stream.CanRead, Is.False);
        Assert.That(_stream.CanWrite, Is.False);

        Assert.Throws<NotSupportedException>(() => _ = _stream.Length);
        Assert.Throws<NotSupportedException>(() => _ = _stream.Position);
        Assert.Throws<NotSupportedException>(() => _stream.Position = 10);
        Assert.Throws<NotSupportedException>(() => _stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => _stream.SetLength(10));
    }

    [Test]
    public void ReadTimeout_And_WriteTimeout_CanBeConfigured()
    {
        _stream.ReadTimeout = 5000;
        _stream.WriteTimeout = 3000;

        Assert.That(_stream.ReadTimeout, Is.EqualTo(5000));
        Assert.That(_stream.WriteTimeout, Is.EqualTo(3000));
    }

    [Test]
    public void Write_WhenDisconnected_ThrowsIOException()
    {
        _connection.IsConnected.Returns(false);

        var ex = Assert.Throws<IOException>(() => _stream.Write(new byte[] { 1, 2, 3 }, 0, 3));
        Assert.That(ex.Message, Does.Contain("not connected"));
    }

    [Test]
    public void Write_WhenCountIsZero_ReturnsImmediately()
    {
        _connection.IsConnected.Returns(true);

        _stream.Write(new byte[] { 1, 2, 3 }, 0, 0);

        _connection.DidNotReceive().Send(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Test]
    public void Write_MultiChunk_TransmitsAllBytesUntilComplete()
    {
        _connection.IsConnected.Returns(true);
        var totalBytes = 3000;
        var data = new byte[totalBytes];
        for (var i = 0; i < totalBytes; i++)
        {
            data[i] = (byte)(i % 256);
        }

        // Simulate sending 1000 bytes per chunk
        _connection.Send(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(x => Math.Min(1000, (int)x[2]));

        _stream.Write(data, 0, totalBytes);

        _connection.Received(3).Send(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>());
        _connection.Received(1).Send(data, 0, 3000);
        _connection.Received(1).Send(data, 1000, 2000);
        _connection.Received(1).Send(data, 2000, 1000);
    }

    [Test]
    public void Write_WhenConnectionDropsDuringTransmission_ThrowsIOExceptionWithTruncationDetails()
    {
        _connection.IsConnected.Returns(true);
        var totalBytes = 3000;
        var data = new byte[totalBytes];

        var calls = 0;
        _connection.Send(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(x =>
            {
                calls++;
                if (calls == 1)
                {
                    return 1000;
                }

                _connection.IsConnected.Returns(false);
                return 0;
            });

        var ex = Assert.Throws<IOException>(() => _stream.Write(data, 0, totalBytes));
        Assert.That(ex.Message, Does.Contain("writing 1000 of 3000 bytes"));
    }

    [Test]
    public void Write_WhenSendReturnsZero_ThrowsIOException()
    {
        _connection.IsConnected.Returns(true);
        _connection.Send(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>()).Returns(0);

        var ex = Assert.Throws<IOException>(() => _stream.Write(new byte[] { 1, 2, 3 }, 0, 3));
        Assert.That(ex.Message, Does.Contain("writing 0 of 3 bytes"));
    }

    [Test]
    public void Write_WhenWriteTimeoutExceeded_ThrowsTimeoutException()
    {
        _connection.IsConnected.Returns(true);
        _stream.WriteTimeout = 50;

        _connection.Send(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(x =>
            {
                Thread.Sleep(80);
                return 500;
            });

        Assert.Throws<TimeoutException>(() => _stream.Write(new byte[2000], 0, 2000));
    }

    [Test]
    public async Task WriteAsync_MultiChunk_TransmitsAllBytes()
    {
        _connection.IsConnected.Returns(true);
        var totalBytes = 2500;
        var data = new byte[totalBytes];

        _connection.Send(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(x => Math.Min(1000, (int)x[2]));

#pragma warning disable CA1835
        await _stream.WriteAsync(data, 0, totalBytes);
#pragma warning restore CA1835

        _connection.Received(3).Send(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Test]
    public async Task WriteAsync_Memory_TransmitsAllBytes()
    {
        _connection.IsConnected.Returns(true);
        var totalBytes = 1500;
        var memory = new ReadOnlyMemory<byte>(new byte[totalBytes]);

        _connection.Send(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(x => Math.Min(1000, (int)x[2]));

        await _stream.WriteAsync(memory);

        _connection.Received(2).Send(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Test]
    public void Read_WhenDisconnected_ReturnsZero()
    {
        _connection.IsConnected.Returns(false);

        var bytesRead = _stream.Read(new byte[100], 0, 100);

        Assert.That(bytesRead, Is.EqualTo(0));
    }

    [Test]
    public void Read_WhenDataAvailable_ReturnsBytesRead()
    {
        _connection.IsConnected.Returns(true);
        _connection.Receive(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>()).Returns(42);

        var bytesRead = _stream.Read(new byte[100], 0, 100);

        Assert.That(bytesRead, Is.EqualTo(42));
    }

    [Test]
    public void Read_WhenReadTimeoutExceeded_ThrowsTimeoutException()
    {
        _connection.IsConnected.Returns(true);
        _stream.ReadTimeout = 50;

        _connection.Receive(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(x =>
            {
                Thread.Sleep(150);
                return 10;
            });

        Assert.Throws<TimeoutException>(() => _stream.Read(new byte[100], 0, 100));
    }

    [Test]
    public async Task ReadAsync_WhenDataAvailable_ReturnsBytesRead()
    {
        _connection.IsConnected.Returns(true);
        _connection.Receive(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>()).Returns(25);

#pragma warning disable CA1835
        var bytesRead = await _stream.ReadAsync(new byte[100], 0, 100);
#pragma warning restore CA1835

        Assert.That(bytesRead, Is.EqualTo(25));
    }

    [Test]
    public async Task ReadAsync_Memory_ReturnsBytesRead()
    {
        _connection.IsConnected.Returns(true);
        _connection.Receive(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<int>()).Returns(20);

        var memory = new Memory<byte>(new byte[100]);
        var bytesRead = await _stream.ReadAsync(memory);

        Assert.That(bytesRead, Is.EqualTo(20));
    }

    [Test]
    public void Flush_CallsConnectionFlush()
    {
        _stream.Flush();

        _connection.Received(1).Flush();
    }

    [Test]
    public async Task FlushAsync_CallsConnectionFlush()
    {
        await _stream.FlushAsync();

        _connection.Received(1).Flush();
    }

    [Test]
    public void Dispose_DisposesConnection()
    {
        _stream.Dispose();

        _connection.Received(1).Dispose();
    }

    [Test]
    public void Arguments_Validation_ThrowsExpectedExceptions()
    {
        Assert.Throws<ArgumentNullException>(() => _stream.Read(null, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => _stream.Read(new byte[10], -1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => _stream.Read(new byte[10], 0, -1));
        Assert.Throws<ArgumentException>(() => _stream.Read(new byte[10], 5, 10));

        Assert.Throws<ArgumentNullException>(() => _stream.Write(null, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => _stream.Write(new byte[10], -1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => _stream.Write(new byte[10], 0, -1));
        Assert.Throws<ArgumentException>(() => _stream.Write(new byte[10], 5, 10));
    }
}
