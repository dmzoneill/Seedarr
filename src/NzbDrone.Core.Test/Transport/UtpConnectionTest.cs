using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Transport;

namespace NzbDrone.Core.Test.Transport;

[TestFixture]
public class UtpConnectionTest
{
    [Test]
    public void Constructor_should_set_is_connected_false()
    {
        using var connection = new UtpConnection();

        Assert.That(connection.IsConnected, Is.False);
    }

    [Test]
    public void Send_should_return_zero_when_not_connected()
    {
        using var connection = new UtpConnection();
        var data = new byte[] { 1, 2, 3 };

        var result = connection.Send(data, 0, data.Length);

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void Receive_should_return_zero_when_not_connected()
    {
        using var connection = new UtpConnection();
        var buffer = new byte[100];

        var result = connection.Receive(buffer, 0, buffer.Length);

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void BuildPacket_should_create_header_only_for_empty_payload()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Syn, Array.Empty<byte>() });

        Assert.That(result.Length, Is.EqualTo(20));
    }

    [Test]
    public void BuildPacket_should_include_payload()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, payload });

        Assert.That(result.Length, Is.EqualTo(24));
        Assert.That(result[20], Is.EqualTo(0xDE));
        Assert.That(result[21], Is.EqualTo(0xAD));
        Assert.That(result[22], Is.EqualTo(0xBE));
        Assert.That(result[23], Is.EqualTo(0xEF));
    }

    [Test]
    public void BuildPacket_should_encode_syn_type_in_first_nibble()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Syn, Array.Empty<byte>() });

        var packetType = (result[0] >> 4) & 0x0F;
        Assert.That(packetType, Is.EqualTo((byte)UtpPacketType.Syn));
    }

    [Test]
    public void BuildPacket_should_encode_data_type_in_first_nibble()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, Array.Empty<byte>() });

        var packetType = (result[0] >> 4) & 0x0F;
        Assert.That(packetType, Is.EqualTo((byte)UtpPacketType.Data));
    }

    [Test]
    public void BuildPacket_should_encode_fin_type_in_first_nibble()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Fin, Array.Empty<byte>() });

        var packetType = (result[0] >> 4) & 0x0F;
        Assert.That(packetType, Is.EqualTo((byte)UtpPacketType.Fin));
    }

    [Test]
    public void BuildPacket_should_set_version_1_in_low_nibble()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, Array.Empty<byte>() });

        var version = result[0] & 0x0F;
        Assert.That(version, Is.EqualTo(1));
    }

    [Test]
    public void BuildPacket_should_set_extension_to_zero()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, Array.Empty<byte>() });

        Assert.That(result[1], Is.EqualTo(0));
    }

    [Test]
    public void BuildPacket_should_encode_window_size()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, Array.Empty<byte>() });

        var windowSize = (uint)((result[12] << 24) | (result[13] << 16) | (result[14] << 8) | result[15]);
        Assert.That(windowSize, Is.EqualTo(65535u));
    }

    [Test]
    public void BuildPacket_should_encode_sequence_number()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, Array.Empty<byte>() });

        var seqNum = (ushort)((result[16] << 8) | result[17]);
        Assert.That(seqNum, Is.EqualTo(1));
    }

    [Test]
    public void ParseHeader_should_parse_packet_type()
    {
        var method = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var data = new byte[20];
        data[0] = ((byte)UtpPacketType.State << 4) | 1;

        var header = (UtpHeader)method.Invoke(null, new object[] { data });

        Assert.That(header.Type, Is.EqualTo(UtpPacketType.State));
    }

    [Test]
    public void ParseHeader_should_parse_version()
    {
        var method = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var data = new byte[20];
        data[0] = ((byte)UtpPacketType.Data << 4) | 1;

        var header = (UtpHeader)method.Invoke(null, new object[] { data });

        Assert.That(header.Version, Is.EqualTo(1));
    }

    [Test]
    public void ParseHeader_should_parse_connection_id()
    {
        var method = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var data = new byte[20];
        data[2] = 0x1A;
        data[3] = 0x2B;

        var header = (UtpHeader)method.Invoke(null, new object[] { data });

        Assert.That(header.ConnectionId, Is.EqualTo(0x1A2B));
    }

    [Test]
    public void ParseHeader_should_parse_sequence_number()
    {
        var method = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var data = new byte[20];
        data[16] = 0x00;
        data[17] = 0x05;

        var header = (UtpHeader)method.Invoke(null, new object[] { data });

        Assert.That(header.SequenceNumber, Is.EqualTo(5));
    }

    [Test]
    public void ParseHeader_should_parse_ack_number()
    {
        var method = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var data = new byte[20];
        data[18] = 0x00;
        data[19] = 0x03;

        var header = (UtpHeader)method.Invoke(null, new object[] { data });

        Assert.That(header.AckNumber, Is.EqualTo(3));
    }

    [Test]
    public void ParseHeader_should_parse_window_size()
    {
        var method = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var data = new byte[20];
        data[12] = 0x00;
        data[13] = 0x00;
        data[14] = 0xFF;
        data[15] = 0xFF;

        var header = (UtpHeader)method.Invoke(null, new object[] { data });

        Assert.That(header.WindowSize, Is.EqualTo(65535u));
    }

    [Test]
    public void ParseHeader_should_parse_timestamp()
    {
        var method = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var data = new byte[20];
        data[4] = 0x01;
        data[5] = 0x02;
        data[6] = 0x03;
        data[7] = 0x04;

        var header = (UtpHeader)method.Invoke(null, new object[] { data });

        Assert.That(header.Timestamp, Is.EqualTo(0x01020304u));
    }

    [Test]
    public void ParseHeader_should_parse_timestamp_diff()
    {
        var method = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var data = new byte[20];
        data[8] = 0x05;
        data[9] = 0x06;
        data[10] = 0x07;
        data[11] = 0x08;

        var header = (UtpHeader)method.Invoke(null, new object[] { data });

        Assert.That(header.TimestampDiff, Is.EqualTo(0x05060708u));
    }

    [Test]
    public void Dispose_should_set_is_connected_false()
    {
        var connection = new UtpConnection();

        connection.Dispose();

        Assert.That(connection.IsConnected, Is.False);
    }

    [Test]
    public void GetMicroseconds_should_return_a_value()
    {
        var method = typeof(UtpConnection).GetMethod("GetMicroseconds", BindingFlags.NonPublic | BindingFlags.Static);

        var result = (uint)method.Invoke(null, null);

        Assert.That(result, Is.GreaterThanOrEqualTo(0u));
    }

    [Test]
    public void BuildPacket_and_ParseHeader_should_roundtrip()
    {
        using var connection = new UtpConnection();
        var buildMethod = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);
        var parseMethod = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);

        var packet = (byte[])buildMethod.Invoke(connection, new object[] { UtpPacketType.State, Array.Empty<byte>() });
        var header = (UtpHeader)parseMethod.Invoke(null, new object[] { packet });

        Assert.That(header.Type, Is.EqualTo(UtpPacketType.State));
        Assert.That(header.Version, Is.EqualTo(1));
        Assert.That(header.WindowSize, Is.EqualTo(65535u));
    }

    [Test]
    public void ParseHeader_should_parse_extension()
    {
        var method = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var data = new byte[20];
        data[1] = 0x03;

        var header = (UtpHeader)method.Invoke(null, new object[] { data });

        Assert.That(header.Extension, Is.EqualTo(3));
    }

    [Test]
    public void UtpPacketType_should_have_correct_values()
    {
        Assert.That((byte)UtpPacketType.Data, Is.EqualTo(0));
        Assert.That((byte)UtpPacketType.Fin, Is.EqualTo(1));
        Assert.That((byte)UtpPacketType.State, Is.EqualTo(2));
        Assert.That((byte)UtpPacketType.Reset, Is.EqualTo(3));
        Assert.That((byte)UtpPacketType.Syn, Is.EqualTo(4));
    }

    [Test]
    public void UtpHeader_should_have_default_version_1()
    {
        var header = new UtpHeader();

        Assert.That(header.Version, Is.EqualTo(1));
    }

    [Test]
    public void BuildPacket_should_encode_reset_type_in_first_nibble()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Reset, Array.Empty<byte>() });

        var packetType = (result[0] >> 4) & 0x0F;
        Assert.That(packetType, Is.EqualTo((byte)UtpPacketType.Reset));
    }

    [Test]
    public void BuildPacket_should_encode_state_type_in_first_nibble()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.State, Array.Empty<byte>() });

        var packetType = (result[0] >> 4) & 0x0F;
        Assert.That(packetType, Is.EqualTo((byte)UtpPacketType.State));
    }

    [Test]
    public void BuildPacket_should_encode_connection_id_in_big_endian()
    {
        using var connection = new UtpConnection();
        var field = typeof(UtpConnection).GetField("_connectionId", BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(connection, (ushort)0xABCD);

        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, Array.Empty<byte>() });

        Assert.That(result[2], Is.EqualTo(0xAB));
        Assert.That(result[3], Is.EqualTo(0xCD));
    }

    [Test]
    public void BuildPacket_should_encode_connection_id_matching_field_value()
    {
        using var connection = new UtpConnection();
        var field = typeof(UtpConnection).GetField("_connectionId", BindingFlags.NonPublic | BindingFlags.Instance);
        var connectionId = (ushort)field.GetValue(connection);

        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.State, Array.Empty<byte>() });

        var encodedId = (ushort)((result[2] << 8) | result[3]);
        Assert.That(encodedId, Is.EqualTo(connectionId));
    }

    [Test]
    public void BuildPacket_should_populate_timestamp_as_nonzero()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, Array.Empty<byte>() });

        var timestamp = (uint)((result[4] << 24) | (result[5] << 16) | (result[6] << 8) | result[7]);
        Assert.That(timestamp, Is.GreaterThan(0u));
    }

    [Test]
    public void BuildPacket_should_leave_timestamp_diff_as_zero()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, Array.Empty<byte>() });

        var timestampDiff = (uint)((result[8] << 24) | (result[9] << 16) | (result[10] << 8) | result[11]);
        Assert.That(timestampDiff, Is.EqualTo(0u));
    }

    [Test]
    public void Dispose_when_connected_should_set_is_connected_false()
    {
        var connection = new UtpConnection();
        var backingField = typeof(UtpConnection).GetField("<IsConnected>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        backingField.SetValue(connection, true);

        var endpointField = typeof(UtpConnection).GetField("_remoteEndpoint", BindingFlags.NonPublic | BindingFlags.Instance);
        endpointField.SetValue(connection, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 55555));

        Assert.That(connection.IsConnected, Is.True);

        connection.Dispose();

        Assert.That(connection.IsConnected, Is.False);
    }

    [Test]
    public void Dispose_when_connected_should_not_throw()
    {
        var connection = new UtpConnection();
        var backingField = typeof(UtpConnection).GetField("<IsConnected>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        backingField.SetValue(connection, true);

        var endpointField = typeof(UtpConnection).GetField("_remoteEndpoint", BindingFlags.NonPublic | BindingFlags.Instance);
        endpointField.SetValue(connection, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 55555));

        Assert.DoesNotThrow(() => connection.Dispose());
    }

    [Test]
    public void Constructor_with_custom_timeout_should_not_throw()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 10);

        Assert.That(connection.IsConnected, Is.False);
    }

    [Test]
    public void Constructor_with_large_timeout_should_set_is_connected_false()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 120);

        Assert.That(connection.IsConnected, Is.False);
    }

    [Test]
    public void ParseHeader_roundtrip_should_preserve_all_fields()
    {
        using var connection = new UtpConnection();
        var connIdField = typeof(UtpConnection).GetField("_connectionId", BindingFlags.NonPublic | BindingFlags.Instance);
        connIdField.SetValue(connection, (ushort)0x1234);

        var seqField = typeof(UtpConnection).GetField("_sequenceNumber", BindingFlags.NonPublic | BindingFlags.Instance);
        seqField.SetValue(connection, (ushort)42);

        var ackField = typeof(UtpConnection).GetField("_ackNumber", BindingFlags.NonPublic | BindingFlags.Instance);
        ackField.SetValue(connection, (ushort)7);

        var buildMethod = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);
        var parseMethod = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);

        var packet = (byte[])buildMethod.Invoke(connection, new object[] { UtpPacketType.Fin, Array.Empty<byte>() });
        var header = (UtpHeader)parseMethod.Invoke(null, new object[] { packet });

        Assert.That(header.Type, Is.EqualTo(UtpPacketType.Fin));
        Assert.That(header.Version, Is.EqualTo(1));
        Assert.That(header.Extension, Is.EqualTo(0));
        Assert.That(header.ConnectionId, Is.EqualTo(0x1234));
        Assert.That(header.Timestamp, Is.GreaterThan(0u));
        Assert.That(header.TimestampDiff, Is.EqualTo(0u));
        Assert.That(header.WindowSize, Is.EqualTo(65535u));
        Assert.That(header.SequenceNumber, Is.EqualTo(42));
        Assert.That(header.AckNumber, Is.EqualTo(7));
    }

    [Test]
    public void BuildPacket_should_encode_ack_number_in_big_endian()
    {
        using var connection = new UtpConnection();
        var ackField = typeof(UtpConnection).GetField("_ackNumber", BindingFlags.NonPublic | BindingFlags.Instance);
        ackField.SetValue(connection, (ushort)0xBEEF);

        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.State, Array.Empty<byte>() });

        Assert.That(result[18], Is.EqualTo(0xBE));
        Assert.That(result[19], Is.EqualTo(0xEF));
    }

    [Test]
    public void BuildPacket_sequence_number_should_reflect_field_changes()
    {
        using var connection = new UtpConnection();
        var seqField = typeof(UtpConnection).GetField("_sequenceNumber", BindingFlags.NonPublic | BindingFlags.Instance);
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var first = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, Array.Empty<byte>() });
        var firstSeq = (ushort)((first[16] << 8) | first[17]);
        Assert.That(firstSeq, Is.EqualTo(1));

        seqField.SetValue(connection, (ushort)2);

        var second = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Data, Array.Empty<byte>() });
        var secondSeq = (ushort)((second[16] << 8) | second[17]);
        Assert.That(secondSeq, Is.EqualTo(2));
    }

    [Test]
    public void BuildPacket_reset_type_should_have_correct_byte_value()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.Reset, Array.Empty<byte>() });

        Assert.That(result[0], Is.EqualTo(0x31));
    }

    [Test]
    public void BuildPacket_state_type_should_have_correct_byte_value()
    {
        using var connection = new UtpConnection();
        var method = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(connection, new object[] { UtpPacketType.State, Array.Empty<byte>() });

        Assert.That(result[0], Is.EqualTo(0x21));
    }

    [Test]
    public void ParseHeader_roundtrip_with_reset_type()
    {
        var parseMethod = typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static);
        var data = new byte[20];
        data[0] = ((byte)UtpPacketType.Reset << 4) | 1;
        data[2] = 0xCA;
        data[3] = 0xFE;
        data[4] = 0x11;
        data[5] = 0x22;
        data[6] = 0x33;
        data[7] = 0x44;
        data[8] = 0xAA;
        data[9] = 0xBB;
        data[10] = 0xCC;
        data[11] = 0xDD;
        data[12] = 0x00;
        data[13] = 0x01;
        data[14] = 0x00;
        data[15] = 0x00;
        data[16] = 0x00;
        data[17] = 0x0A;
        data[18] = 0x00;
        data[19] = 0x05;

        var header = (UtpHeader)parseMethod.Invoke(null, new object[] { data });

        Assert.That(header.Type, Is.EqualTo(UtpPacketType.Reset));
        Assert.That(header.Version, Is.EqualTo(1));
        Assert.That(header.ConnectionId, Is.EqualTo(0xCAFE));
        Assert.That(header.Timestamp, Is.EqualTo(0x11223344u));
        Assert.That(header.TimestampDiff, Is.EqualTo(0xAABBCCDDu));
        Assert.That(header.WindowSize, Is.EqualTo(0x00010000u));
        Assert.That(header.SequenceNumber, Is.EqualTo(10));
        Assert.That(header.AckNumber, Is.EqualTo(5));
    }

    [Test]
    public void Dispose_when_not_connected_should_not_throw()
    {
        var connection = new UtpConnection();

        Assert.That(connection.IsConnected, Is.False);
        Assert.DoesNotThrow(() => connection.Dispose());
    }

    // ---- Send-when-connected tests ----

    [Test]
    public void Send_should_return_data_length_when_connected()
    {
        using var connection = new UtpConnection();
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var receiverPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

        SetConnected(connection, true);
        SetRemoteEndpoint(connection, new IPEndPoint(IPAddress.Loopback, receiverPort));

        var data = new byte[] { 10, 20, 30, 40, 50 };
        var result = connection.Send(data, 0, data.Length);

        Assert.That(result, Is.EqualTo(5));
    }

    [Test]
    public void Send_should_return_partial_length_when_offset_and_length_are_used()
    {
        using var connection = new UtpConnection();
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var receiverPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

        SetConnected(connection, true);
        SetRemoteEndpoint(connection, new IPEndPoint(IPAddress.Loopback, receiverPort));

        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var result = connection.Send(data, 2, 3);

        Assert.That(result, Is.EqualTo(3));
    }

    [Test]
    public void Send_should_increment_sequence_number_when_connected()
    {
        using var connection = new UtpConnection();
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var receiverPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

        SetConnected(connection, true);
        SetRemoteEndpoint(connection, new IPEndPoint(IPAddress.Loopback, receiverPort));

        var seqField = typeof(UtpConnection).GetField("_sequenceNumber",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var initialSeq = (ushort)seqField.GetValue(connection)!;

        connection.Send(new byte[] { 1, 2, 3 }, 0, 3);

        var newSeq = (ushort)seqField.GetValue(connection)!;
        Assert.That(newSeq, Is.EqualTo((ushort)(initialSeq + 1)));
    }

    // ---- Receive-when-connected tests ----

    [Test]
    public void Receive_should_return_zero_when_received_data_is_exactly_header_size()
    {
        using var connection = new UtpConnection();
        BindInternalUdpClient(connection, out var localPort);

        SetConnected(connection, true);
        SetRemoteEndpoint(connection, new IPEndPoint(IPAddress.Loopback, 1));

        // Send exactly HeaderSize (20) bytes from a sender
        using var sender = new UdpClient();
        sender.Send(new byte[20], 20, new IPEndPoint(IPAddress.Loopback, localPort));

        var buffer = new byte[100];
        var result = connection.Receive(buffer, 0, buffer.Length);

        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void Receive_should_return_payload_length_when_data_exceeds_header()
    {
        using var connection = new UtpConnection();
        BindInternalUdpClient(connection, out var localPort);

        // Set up a receiver for the ACK the method sends back
        using var ackReceiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var ackPort = ((IPEndPoint)ackReceiver.Client.LocalEndPoint!).Port;

        SetConnected(connection, true);
        SetRemoteEndpoint(connection, new IPEndPoint(IPAddress.Loopback, ackPort));

        // Build a 25-byte packet: 20-byte header + 5-byte payload
        var packet = new byte[25];
        packet[0] = ((byte)UtpPacketType.Data << 4) | 1;
        packet[16] = 0x00;
        packet[17] = 0x07;   // sequenceNumber = 7
        packet[20] = 0xAA;
        packet[21] = 0xBB;
        packet[22] = 0xCC;
        packet[23] = 0xDD;
        packet[24] = 0xEE;

        using var sender = new UdpClient();
        sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, localPort));

        var buffer = new byte[100];
        var result = connection.Receive(buffer, 0, buffer.Length);

        Assert.That(result, Is.EqualTo(5));
        Assert.That(buffer[0], Is.EqualTo(0xAA));
        Assert.That(buffer[4], Is.EqualTo(0xEE));
    }

    [Test]
    public void Receive_should_update_ack_number_from_received_header()
    {
        using var connection = new UtpConnection();
        BindInternalUdpClient(connection, out var localPort);

        using var ackReceiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var ackPort = ((IPEndPoint)ackReceiver.Client.LocalEndPoint!).Port;

        SetConnected(connection, true);
        SetRemoteEndpoint(connection, new IPEndPoint(IPAddress.Loopback, ackPort));

        var packet = new byte[21];  // 20-byte header + 1 byte payload
        packet[0] = ((byte)UtpPacketType.Data << 4) | 1;
        packet[16] = 0x00;
        packet[17] = 42;  // sequenceNumber = 42

        using var sender = new UdpClient();
        sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, localPort));

        var buffer = new byte[100];
        connection.Receive(buffer, 0, buffer.Length);

        var ackField = typeof(UtpConnection).GetField("_ackNumber",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var ackNumber = (ushort)ackField.GetValue(connection)!;

        Assert.That(ackNumber, Is.EqualTo(42));
    }

    [Test]
    public void Receive_should_cap_payload_at_buffer_length()
    {
        using var connection = new UtpConnection();
        BindInternalUdpClient(connection, out var localPort);

        using var ackReceiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var ackPort = ((IPEndPoint)ackReceiver.Client.LocalEndPoint!).Port;

        SetConnected(connection, true);
        SetRemoteEndpoint(connection, new IPEndPoint(IPAddress.Loopback, ackPort));

        // Send 10 payload bytes but provide a 4-byte buffer
        var packet = new byte[30];
        packet[0] = ((byte)UtpPacketType.Data << 4) | 1;

        using var sender = new UdpClient();
        sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, localPort));

        var buffer = new byte[4];
        var result = connection.Receive(buffer, 0, buffer.Length);

        Assert.That(result, Is.EqualTo(4));
    }

    // ---- Connect tests ----

    [Test]
    public void Connect_should_set_is_connected_when_server_sends_state_response()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        // Background server: receive SYN, send State response
        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            server.Receive(ref ep);

            var response = new byte[20];
            response[0] = ((byte)UtpPacketType.State << 4) | 1;  // 0x21
            response[2] = 0x10;   // connectionId high byte
            response[3] = 0x00;   // connectionId low byte
            response[16] = 0x00;  // sequenceNumber high byte
            response[17] = 0x05;  // sequenceNumber low byte = 5

            server.Send(response, response.Length, ep);
        });

        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        connection.Connect(new IPEndPoint(IPAddress.Loopback, serverPort));

        serverTask.Wait(TimeSpan.FromSeconds(5));

        Assert.That(connection.IsConnected, Is.True);
    }

    [Test]
    public void Connect_should_update_ack_number_from_server_sequence_number()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            server.Receive(ref ep);

            var response = new byte[20];
            response[0] = ((byte)UtpPacketType.State << 4) | 1;
            response[16] = 0x00;
            response[17] = 99;  // sequenceNumber = 99

            server.Send(response, response.Length, ep);
        });

        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        connection.Connect(new IPEndPoint(IPAddress.Loopback, serverPort));
        serverTask.Wait(TimeSpan.FromSeconds(5));

        var ackField = typeof(UtpConnection).GetField("_ackNumber",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var ackNumber = (ushort)ackField.GetValue(connection)!;

        Assert.That(ackNumber, Is.EqualTo(99));
    }

    [Test]
    public void Connect_should_not_set_is_connected_when_server_sends_non_state_packet()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            server.Receive(ref ep);

            // Respond with Data packet, not State
            var response = new byte[20];
            response[0] = ((byte)UtpPacketType.Data << 4) | 1;  // 0x01

            server.Send(response, response.Length, ep);
        });

        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        connection.Connect(new IPEndPoint(IPAddress.Loopback, serverPort));
        serverTask.Wait(TimeSpan.FromSeconds(5));

        Assert.That(connection.IsConnected, Is.False);
    }

    [Test]
    public void Connect_should_not_set_is_connected_when_response_is_shorter_than_header()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            server.Receive(ref ep);
            server.Send(new byte[10], 10, ep);
        });

        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        connection.Connect(new IPEndPoint(IPAddress.Loopback, serverPort));
        serverTask.Wait(TimeSpan.FromSeconds(5));

        Assert.That(connection.IsConnected, Is.False);
    }

    [Test]
    public void Connect_should_update_connection_id_from_server_response()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            server.Receive(ref ep);

            var response = new byte[20];
            response[0] = ((byte)UtpPacketType.State << 4) | 1;
            response[2] = 0x12;
            response[3] = 0x34;

            server.Send(response, response.Length, ep);
        });

        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        connection.Connect(new IPEndPoint(IPAddress.Loopback, serverPort));
        serverTask.Wait(TimeSpan.FromSeconds(5));

        var connIdField = typeof(UtpConnection).GetField("_connectionId",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var connId = (ushort)connIdField.GetValue(connection)!;

        Assert.That(connId, Is.EqualTo(0x1235));
    }

    // ---- helpers ----

    private static void SetConnected(UtpConnection connection, bool value)
    {
        var backingField = typeof(UtpConnection).GetField(
            "<IsConnected>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        backingField.SetValue(connection, value);
    }

    private static void SetRemoteEndpoint(UtpConnection connection, IPEndPoint endpoint)
    {
        var field = typeof(UtpConnection).GetField(
            "_remoteEndpoint",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(connection, endpoint);
    }

    /// <summary>
    /// Binds the internal _udpClient of the connection to a loopback port so
    /// tests can send packets to it. Returns the bound port.
    /// </summary>
    private static void BindInternalUdpClient(UtpConnection connection, out int localPort)
    {
        var udpClientField = typeof(UtpConnection).GetField(
            "_udpClient",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var udpClient = (UdpClient)udpClientField.GetValue(connection)!;
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        udpClient.Client.ReceiveTimeout = 3000;
        localPort = ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port;
    }
}
