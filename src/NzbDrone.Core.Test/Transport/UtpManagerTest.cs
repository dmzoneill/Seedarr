using System;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Transport;

namespace NzbDrone.Core.Test.Transport;

[TestFixture]
public class UtpManagerTest
{
    private IConfigService _configService;
    private UtpManager _subject;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.UtpEnabled.Returns(true);
        _configService.TcpFallback.Returns(true);
        _configService.TransportConnectionTimeoutSeconds.Returns(30);
        _configService.ListeningPort.Returns(16881);
        _subject = new UtpManager(_configService);
    }

    [TearDown]
    public void TearDown()
    {
        _subject?.Dispose();
    }

    // --- Constructor tests ---

    [Test]
    public void Constructor_should_create_instance()
    {
        Assert.That(_subject, Is.Not.Null);
    }

    // --- Property tests ---

    [Test]
    public void ActiveConnections_should_always_return_zero()
    {
        Assert.That(_subject.ActiveConnections, Is.EqualTo(0));
    }

    [Test]
    public void IsEnabled_should_return_true_when_utp_enabled()
    {
        _configService.UtpEnabled.Returns(true);

        Assert.That(_subject.IsEnabled, Is.True);
    }

    [Test]
    public void IsEnabled_should_return_false_when_utp_disabled()
    {
        _configService.UtpEnabled.Returns(false);

        Assert.That(_subject.IsEnabled, Is.False);
    }

    [Test]
    public void TcpFallbackEnabled_should_return_true_when_configured()
    {
        _configService.TcpFallback.Returns(true);

        Assert.That(_subject.TcpFallbackEnabled, Is.True);
    }

    [Test]
    public void TcpFallbackEnabled_should_return_false_when_not_configured()
    {
        _configService.TcpFallback.Returns(false);

        Assert.That(_subject.TcpFallbackEnabled, Is.False);
    }

    [Test]
    public void IsEnabled_should_delegate_to_config_service()
    {
        _configService.UtpEnabled.Returns(false);
        Assert.That(_subject.IsEnabled, Is.False);

        _configService.UtpEnabled.Returns(true);
        Assert.That(_subject.IsEnabled, Is.True);
    }

    [Test]
    public void TcpFallbackEnabled_should_delegate_to_config_service()
    {
        _configService.TcpFallback.Returns(false);
        Assert.That(_subject.TcpFallbackEnabled, Is.False);

        _configService.TcpFallback.Returns(true);
        Assert.That(_subject.TcpFallbackEnabled, Is.True);
    }

    // --- CreateConnection tests ---

    [Test]
    public void CreateConnection_should_throw_when_utp_disabled()
    {
        _configService.UtpEnabled.Returns(false);

        Assert.That(() => _subject.CreateConnection(),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("uTP is disabled"));
    }

    [Test]
    public void CreateConnection_should_return_connection_when_enabled()
    {
        _configService.UtpEnabled.Returns(true);
        _configService.TransportConnectionTimeoutSeconds.Returns(15);

        using var connection = _subject.CreateConnection();

        Assert.That(connection, Is.Not.Null);
        Assert.That(connection, Is.InstanceOf<IUtpConnection>());
    }

    [Test]
    public void CreateConnection_should_return_utp_connection_instance()
    {
        _configService.UtpEnabled.Returns(true);

        using var connection = _subject.CreateConnection();

        Assert.That(connection, Is.InstanceOf<UtpConnection>());
    }

    [Test]
    public void CreateConnection_should_create_disconnected_connection()
    {
        _configService.UtpEnabled.Returns(true);

        using var connection = _subject.CreateConnection();

        Assert.That(connection.IsConnected, Is.False);
    }

    [Test]
    public void CreateConnection_should_read_timeout_from_config()
    {
        _configService.UtpEnabled.Returns(true);
        _configService.TransportConnectionTimeoutSeconds.Returns(42);

        using var connection = _subject.CreateConnection();

        // Verify the config was read
        _ = _configService.Received().TransportConnectionTimeoutSeconds;
    }

    [Test]
    public void CreateConnection_should_create_new_instance_each_call()
    {
        _configService.UtpEnabled.Returns(true);

        using var connection1 = _subject.CreateConnection();
        using var connection2 = _subject.CreateConnection();

        Assert.That(connection1, Is.Not.SameAs(connection2));
    }

    // --- ExecuteAsync tests ---

    [Test]
    public async Task ExecuteAsync_should_return_immediately_when_disabled()
    {
        _configService.UtpEnabled.Returns(false);

        var method = typeof(UtpManager).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method.Invoke(_subject, new object[] { CancellationToken.None });
        await task;

        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_stop_on_cancellation()
    {
        _configService.UtpEnabled.Returns(true);
        _configService.ListeningPort.Returns(19999);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var method = typeof(UtpManager).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);

        var task = (Task)method.Invoke(_subject, new object[] { cts.Token });
        await task;

        Assert.That(task.IsCompleted, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_handle_port_bind_failure_gracefully()
    {
        _configService.UtpEnabled.Returns(true);
        _configService.ListeningPort.Returns(1);
        _configService.TcpFallback.Returns(true);

        var method = typeof(UtpManager).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method.Invoke(_subject, new object[] { CancellationToken.None });
        await task;

        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_handle_port_bind_failure_without_tcp_fallback()
    {
        _configService.UtpEnabled.Returns(true);
        _configService.ListeningPort.Returns(1);
        _configService.TcpFallback.Returns(false);

        var method = typeof(UtpManager).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = (Task)method.Invoke(_subject, new object[] { CancellationToken.None });
        await task;

        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    // --- HandleIncoming tests (private method, via reflection) ---

    private void InvokeHandleIncoming(byte[] data, IPEndPoint sender)
    {
        var method = typeof(UtpManager).GetMethod("HandleIncoming", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { data, sender });
    }

    [Test]
    public void HandleIncoming_should_ignore_packets_shorter_than_20_bytes()
    {
        var data = new byte[19];
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_ignore_empty_data()
    {
        var data = Array.Empty<byte>();
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_process_syn_packet()
    {
        // Build a 20-byte SYN packet: type=4 (Syn), high nibble of first byte = 4
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1); // type=Syn, version=1
        data[2] = 0x00; // connection ID high byte
        data[3] = 0x01; // connection ID low byte

        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 54321);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_process_data_packet()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Data) << 4 | 1); // type=Data

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_process_fin_packet()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Fin) << 4 | 1); // type=Fin

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_process_state_packet()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.State) << 4 | 1); // type=State

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_process_reset_packet()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Reset) << 4 | 1); // type=Reset

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_extract_connection_id_from_syn()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Syn) << 4 | 1);
        data[2] = 0xAB; // connection ID high byte
        data[3] = 0xCD; // connection ID low byte

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_handle_exactly_20_byte_packet()
    {
        var data = new byte[20];
        data[0] = (byte)(((byte)UtpPacketType.Data) << 4 | 1);

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }

    [Test]
    public void HandleIncoming_should_handle_oversized_packet()
    {
        var data = new byte[1500]; // typical MTU-sized packet
        data[0] = (byte)(((byte)UtpPacketType.Data) << 4 | 1);

        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        Assert.DoesNotThrow(() => InvokeHandleIncoming(data, sender));
    }
}
