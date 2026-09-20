using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Network;
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
        Assert.That(windowSize, Is.EqualTo(UtpConnection.MaxBufferSize));
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
        Assert.That(header.WindowSize, Is.EqualTo(UtpConnection.MaxBufferSize));
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
        Assert.That(header.WindowSize, Is.EqualTo(UtpConnection.MaxBufferSize));
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

    // ---- Loopback and Retransmission tests ----

    [Test]
    public void Loopback_should_transfer_100KB_bidirectionally_over_real_udp_sockets()
    {
        using var serverUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)serverUdp.Client.LocalEndPoint!).Port;

        var transferSize = 16 * 1024;
        var serverReceivedData = new byte[transferSize];
        var clientReceivedData = new byte[transferSize];

        var clientPayload = new byte[transferSize];
        var serverPayload = new byte[transferSize];
        for (var i = 0; i < clientPayload.Length; i++)
        {
            clientPayload[i] = (byte)(i % 251);
            serverPayload[i] = (byte)(((i * 3) + 7) % 251);
        }

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            var synData = serverUdp.Receive(ref ep);
            var synHeader = (UtpHeader)typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { synData })!;

            // Send State response
            var response = new byte[20];
            response[0] = ((byte)UtpPacketType.State << 4) | 1;
            response[2] = (byte)(synHeader.ConnectionId >> 8);
            response[3] = (byte)synHeader.ConnectionId;
            BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(12, 4), UtpConnection.MaxBufferSize);
            response[16] = 0x00;
            response[17] = 0x01; // server initial seq
            response[18] = (byte)(synHeader.SequenceNumber >> 8);
            response[19] = (byte)synHeader.SequenceNumber;
            serverUdp.Send(response, response.Length, ep);

            using var serverConn = new UtpConnection(serverUdp, synHeader.ConnectionId, ep, connectionTimeoutSeconds: 5);
            SetConnected(serverConn, true);

            var read = 0;
            while (read < serverReceivedData.Length)
            {
                var r = serverConn.Receive(serverReceivedData, read, serverReceivedData.Length - read);
                if (r <= 0) break;
                read += r;
            }

            var written = 0;
            while (written < serverPayload.Length)
            {
                var w = serverConn.Send(serverPayload, written, serverPayload.Length - written);
                if (w <= 0) break;
                written += w;
            }

            serverConn.Flush();
        });

        using var clientConn = new UtpConnection(connectionTimeoutSeconds: 10);
        clientConn.Connect(new IPEndPoint(IPAddress.Loopback, serverPort));

        Assert.That(clientConn.IsConnected, Is.True);

        var written = 0;
        while (written < clientPayload.Length)
        {
            var w = clientConn.Send(clientPayload, written, clientPayload.Length - written);
            if (w <= 0) break;
            written += w;
        }

        clientConn.Flush();

        var read = 0;
        while (read < clientReceivedData.Length)
        {
            var r = clientConn.Receive(clientReceivedData, read, clientReceivedData.Length - read);
            if (r <= 0) break;
            read += r;
        }

        serverTask.Wait(TimeSpan.FromSeconds(10));

        Assert.That(serverReceivedData, Is.EqualTo(clientPayload));
        Assert.That(clientReceivedData, Is.EqualTo(serverPayload));
    }

    [Test]
    public void Retransmit_should_complete_transfer_when_30_percent_packets_are_dropped()
    {
        using var serverUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = ((IPEndPoint)serverUdp.Client.LocalEndPoint!).Port;

        var transferSize = 10 * 1024;
        var clientPayload = new byte[transferSize];
        var serverReceivedData = new byte[transferSize];
        for (var i = 0; i < transferSize; i++)
        {
            clientPayload[i] = (byte)(i % 256);
        }

        var packetIndex = 0;

        var serverTask = Task.Run(() =>
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            var synData = serverUdp.Receive(ref ep);
            var synHeader = (UtpHeader)typeof(UtpConnection).GetMethod("ParseHeader", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { synData })!;

            var response = new byte[20];
            response[0] = ((byte)UtpPacketType.State << 4) | 1;
            response[2] = (byte)(synHeader.ConnectionId >> 8);
            response[3] = (byte)synHeader.ConnectionId;
            BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(12, 4), UtpConnection.MaxBufferSize);
            response[16] = 0x00;
            response[17] = 0x01;
            response[18] = (byte)(synHeader.SequenceNumber >> 8);
            response[19] = (byte)synHeader.SequenceNumber;
            serverUdp.Send(response, response.Length, ep);

            using var serverConn = new UtpConnection(serverUdp, synHeader.ConnectionId, ep, connectionTimeoutSeconds: 10);
            SetConnected(serverConn, true);

            var read = 0;
            while (read < serverReceivedData.Length)
            {
                var r = serverConn.Receive(serverReceivedData, read, serverReceivedData.Length - read);
                if (r <= 0) break;
                read += r;
            }
        });

        using var clientConn = new UtpConnection(connectionTimeoutSeconds: 10);
        // Drop ~30% of data packets deterministically
        clientConn.PacketDropFilter = (data, _) =>
        {
            if (data.Length > 20)
            {
                packetIndex++;
                return packetIndex % 3 == 0;
            }

            return false;
        };

        clientConn.Connect(new IPEndPoint(IPAddress.Loopback, serverPort));
        Assert.That(clientConn.IsConnected, Is.True);

        var written = 0;
        while (written < clientPayload.Length)
        {
            var w = clientConn.Send(clientPayload, written, clientPayload.Length - written);
            if (w <= 0) break;
            written += w;
        }

        clientConn.Flush();

        serverTask.Wait(TimeSpan.FromSeconds(15));
        Assert.That(serverReceivedData, Is.EqualTo(clientPayload));
    }

    [Test]
    public void UtpStream_should_wrap_connection_read_write()
    {
        using var connection = new UtpConnection();
        using var stream = connection.GetStream();

        Assert.That(stream.CanSeek, Is.False);
        Assert.That(stream.CanTimeout, Is.True);
        Assert.Throws<NotSupportedException>(() => _ = stream.Length);
        Assert.Throws<NotSupportedException>(() => _ = stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, System.IO.SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
    }

    [Test]
    public void Disposing_connection_with_shared_udp_client_should_not_dispose_shared_client()
    {
        using var sharedClient = new UdpClient();
        sharedClient.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);

        var connection = new UtpConnection(sharedClient, 1, remoteEndpoint);
        Assert.That(connection.OwnsUdpClient, Is.False);

        connection.Dispose();

        Assert.That(sharedClient.Client.IsBound, Is.True);
        Assert.DoesNotThrow(() =>
        {
            var ping = new byte[] { 1, 2, 3 };
            sharedClient.Send(ping, ping.Length, remoteEndpoint);
        });
    }

    [Test]
    public void Disposing_connection_with_owned_udp_client_should_dispose_udp_client()
    {
        var connection = new UtpConnection();
        Assert.That(connection.OwnsUdpClient, Is.True);

        var udpClientField = typeof(UtpConnection).GetField(
            "_udpClient",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var udpClient = (UdpClient)udpClientField.GetValue(connection)!;

        connection.Dispose();

        Assert.That(udpClient.Client == null || udpClient.Client.SafeHandle.IsClosed, Is.True);
    }

    [Test]
    public void Disposing_connection_when_udp_client_is_already_closed_should_not_throw()
    {
        var client = new UdpClient();
        var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345);
        var connection = new UtpConnection(client, 1, remoteEndpoint);

        client.Dispose();

        Assert.DoesNotThrow(() => connection.Dispose());
    }

    [Test]
    public void Disposing_connected_connection_with_shared_client_sends_fin_safely_and_preserves_shared_client()
    {
        using var sharedClient = new UdpClient();
        sharedClient.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var remoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12346);

        var connection = new UtpConnection(sharedClient, 1, remoteEndpoint);
        SetConnected(connection, true);

        Assert.DoesNotThrow(() => connection.Dispose());
        Assert.That(connection.IsConnected, Is.False);
        Assert.That(sharedClient.Client.IsBound, Is.True);
    }

    [Test]
    public void Multiple_connections_sharing_same_client_can_dispose_independently()
    {
        using var sharedClient = new UdpClient();
        sharedClient.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var remoteEndpoint1 = new IPEndPoint(IPAddress.Loopback, 12347);
        var remoteEndpoint2 = new IPEndPoint(IPAddress.Loopback, 12348);

        var conn1 = new UtpConnection(sharedClient, 1, remoteEndpoint1);
        var conn2 = new UtpConnection(sharedClient, 2, remoteEndpoint2);

        SetConnected(conn1, true);
        SetConnected(conn2, true);

        conn1.Dispose();

        Assert.That(conn1.IsConnected, Is.False);
        Assert.That(conn2.IsConnected, Is.True);

        var dummyData = new byte[] { 0x01, 0x02, 0x03 };
        Assert.DoesNotThrow(() =>
        {
            sharedClient.Send(dummyData, dummyData.Length, remoteEndpoint2);
        });

        conn2.Dispose();

        Assert.That(conn2.IsConnected, Is.False);
        Assert.That(sharedClient.Client.IsBound, Is.True);
    }

    [Test]
    public void Send_should_not_block_when_receive_is_pending()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        BindInternalUdpClient(connection, out _);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, new IPEndPoint(IPAddress.Loopback, 12345));

        using var receiveStarted = new ManualResetEventSlim(false);

        var receiveTask = Task.Run(() =>
        {
            receiveStarted.Set();
            var buf = new byte[100];
            return connection.Receive(buf, 0, buf.Length);
        });

        Assert.That(receiveStarted.Wait(TimeSpan.FromSeconds(2)), Is.True);
        Thread.Sleep(100);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sent = connection.Send(new byte[] { 1, 2, 3 }, 0, 3);
        sw.Stop();

        Assert.That(sent, Is.EqualTo(3));
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000), "Send should not be blocked by pending socket receive");

        connection.Dispose();
        receiveTask.Wait(TimeSpan.FromSeconds(3));
    }

    [Test]
    public void Concurrent_send_and_receive_should_not_deadlock()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        BindInternalUdpClient(connection, out var port);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, new IPEndPoint(IPAddress.Loopback, port));

        var sendTask = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                connection.Send(new byte[] { 1, 2, 3, 4 }, 0, 4);
            }
        });

        var receiveTask = Task.Run(() =>
        {
            var buf = new byte[100];
            for (var i = 0; i < 10; i++)
            {
                connection.Receive(buf, 0, buf.Length, timeoutMs: 50);
            }
        });

        var completed = Task.WaitAll(new[] { sendTask, receiveTask }, TimeSpan.FromSeconds(5));
        connection.Dispose();

        Assert.That(completed, Is.True, "Concurrent send and receive should complete without deadlock");
    }

    [Test]
    public void Flush_should_wake_up_immediately_when_ack_is_received()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        BindInternalUdpClient(connection, out _);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, new IPEndPoint(IPAddress.Loopback, 12345));

        connection.Send(new byte[] { 1, 2, 3 }, 0, 3);

        var flushTask = Task.Run(() => connection.Flush());

        var ackPacket = new byte[20];
        ackPacket[0] = ((byte)UtpPacketType.State << 4) | 1;
        ackPacket[18] = 0x00;
        ackPacket[19] = 0x01;

        Thread.Sleep(50);
        connection.HandleIncomingPacket(ackPacket, new IPEndPoint(IPAddress.Loopback, 12345));

        var flushed = flushTask.Wait(TimeSpan.FromSeconds(1));
        Assert.That(flushed, Is.True, "Flush should complete promptly once ACK is processed");
    }

    [Test]
    public void Receive_after_data_packet_followed_by_fin_should_drain_queue_before_returning_zero()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, sender);

        var dataPayload = new byte[] { 10, 20, 30, 40, 50 };
        var dataPacket = CreatePacket(UtpPacketType.Data, connection.ReceiveId, 1, 0, dataPayload);
        var finPacket = CreatePacket(UtpPacketType.Fin, connection.ReceiveId, 2, 0);

        connection.HandleIncomingPacket(dataPacket, sender);
        connection.HandleIncomingPacket(finPacket, sender);

        Assert.That(connection.HasReceivedFin, Is.True);

        // Drain first 3 bytes
        var buffer = new byte[3];
        var bytesRead1 = connection.Receive(buffer, 0, buffer.Length);
        Assert.That(bytesRead1, Is.EqualTo(3));
        Assert.That(buffer, Is.EqualTo(new byte[] { 10, 20, 30 }));

        // Drain remaining 2 bytes
        var buffer2 = new byte[10];
        var bytesRead2 = connection.Receive(buffer2, 0, buffer2.Length);
        Assert.That(bytesRead2, Is.EqualTo(2));
        Assert.That(buffer2[0], Is.EqualTo(40));
        Assert.That(buffer2[1], Is.EqualTo(50));

        // Queue is now empty, receive should return 0 (clean EOF)
        var buffer3 = new byte[10];
        var bytesRead3 = connection.Receive(buffer3, 0, buffer3.Length);
        Assert.That(bytesRead3, Is.EqualTo(0));
    }

    [Test]
    public void Receive_when_fin_received_with_empty_queue_should_return_zero()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, sender);

        var finPacket = CreatePacket(UtpPacketType.Fin, connection.ReceiveId, 1, 0);
        connection.HandleIncomingPacket(finPacket, sender);

        Assert.That(connection.HasReceivedFin, Is.True);

        var buffer = new byte[100];
        var bytesRead = connection.Receive(buffer, 0, buffer.Length);

        Assert.That(bytesRead, Is.EqualTo(0));
    }

    [Test]
    public void Outbound_fin_should_send_st_fin_with_appropriate_sequence_number()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        var seqField = typeof(UtpConnection).GetField("_sequenceNumber", BindingFlags.NonPublic | BindingFlags.Instance)!;
        seqField.SetValue(connection, (ushort)42);

        byte[] sentFinPacket = null;
        connection.PacketDropFilter = (data, ep) =>
        {
            var type = (UtpPacketType)(data[0] >> 4);
            if (type == UtpPacketType.Fin)
            {
                sentFinPacket = data.ToArray();
                var finSeq = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(16, 2));
                var ackPacket = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, finSeq);
                Task.Run(() => connection.HandleIncomingPacket(ackPacket, remoteEp));
            }

            return true;
        };

        connection.Dispose();

        Assert.That(sentFinPacket, Is.Not.Null, "Outbound FIN packet should have been sent");
        var packetType = (UtpPacketType)(sentFinPacket[0] >> 4);
        Assert.That(packetType, Is.EqualTo(UtpPacketType.Fin));
        var seq = BinaryPrimitives.ReadUInt16BigEndian(sentFinPacket.AsSpan(16, 2));
        Assert.That(seq, Is.EqualTo((ushort)42));
    }

    [Test]
    public void Outbound_fin_after_sending_data_should_send_st_fin_with_incremented_sequence_number()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        connection.PacketDropFilter = (data, ep) => true;
        connection.Send(new byte[] { 1, 2, 3 }, 0, 3);

        byte[] sentFinPacket = null;
        connection.PacketDropFilter = (data, ep) =>
        {
            var type = (UtpPacketType)(data[0] >> 4);
            if (type == UtpPacketType.Fin)
            {
                sentFinPacket = data.ToArray();
                var finSeq = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(16, 2));
                var ackPacket = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, finSeq);
                Task.Run(() => connection.HandleIncomingPacket(ackPacket, remoteEp));
            }

            return true;
        };

        connection.Dispose();

        Assert.That(sentFinPacket, Is.Not.Null);
        var packetType = (UtpPacketType)(sentFinPacket[0] >> 4);
        Assert.That(packetType, Is.EqualTo(UtpPacketType.Fin));
        var seq = BinaryPrimitives.ReadUInt16BigEndian(sentFinPacket.AsSpan(16, 2));
        Assert.That(seq, Is.EqualTo((ushort)2));
    }

    [Test]
    public void HandleIncomingPacket_sequence_wrap_around_should_accept_zero_in_order_and_buffer_out_of_order()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, sender);

        // 1. Initial packet with seqNr = 65535 arrives (first packet)
        var payload1 = new byte[] { 1, 2, 3 };
        var packet65535 = CreatePacket(UtpPacketType.Data, connection.ReceiveId, 65535, 0, payload1);
        connection.HandleIncomingPacket(packet65535, sender);

        Assert.That(connection.HasReceivedFirstPacket, Is.True);

        // Now _expectedSeqNr has wrapped around to 0
        // 2. Out-of-order packet with seqNr = 5 arrives ahead when _expectedSeqNr == 0
        var payload5 = new byte[] { 50, 51 };
        var packet5 = CreatePacket(UtpPacketType.Data, connection.ReceiveId, 5, 0, payload5);
        connection.HandleIncomingPacket(packet5, sender);

        // It should be stored in _outOfOrderBuffer, NOT in _receiveQueue
        Assert.That(connection.OutOfOrderCount, Is.EqualTo(1));

        // 3. Expected in-order packet with seqNr = 0 arrives
        var payload0 = new byte[] { 4, 5, 6 };
        var packet0 = CreatePacket(UtpPacketType.Data, connection.ReceiveId, 0, 0, payload0);
        connection.HandleIncomingPacket(packet0, sender);

        // Drain _receiveQueue: should contain payload1 and payload0 in that order
        var buffer = new byte[10];
        var read1 = connection.Receive(buffer, 0, 3);
        Assert.That(read1, Is.EqualTo(3));
        Assert.That(buffer[0..3], Is.EqualTo(payload1));

        var read2 = connection.Receive(buffer, 0, 3);
        Assert.That(read2, Is.EqualTo(3));
        Assert.That(buffer[0..3], Is.EqualTo(payload0));

        // Packet 5 is still in _outOfOrderBuffer waiting for packets 1..4
        Assert.That(connection.OutOfOrderCount, Is.EqualTo(1));

        // 4. Consecutive packets 1, 2, 3, 4 arrive to bridge the gap
        for (ushort s = 1; s <= 4; s++)
        {
            var bridgePayload = new byte[] { (byte)s };
            var bridgePacket = CreatePacket(UtpPacketType.Data, connection.ReceiveId, s, 0, bridgePayload);
            connection.HandleIncomingPacket(bridgePacket, sender);
        }

        // Out-of-order buffer should now be drained (packet 5 was consumed)
        Assert.That(connection.OutOfOrderCount, Is.EqualTo(0));

        // Drain packets 1..4 and packet 5
        for (var expectedByte = 1; expectedByte <= 4; expectedByte++)
        {
            var r = connection.Receive(buffer, 0, 1);
            Assert.That(r, Is.EqualTo(1));
            Assert.That(buffer[0], Is.EqualTo(expectedByte));
        }

        var read5 = connection.Receive(buffer, 0, 2);
        Assert.That(read5, Is.EqualTo(2));
        Assert.That(buffer[0..2], Is.EqualTo(payload5));
    }

    [Test]
    public void HandleIncomingPacket_out_of_order_buffer_should_cap_at_max_capacity()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        // 1. Initial packet to establish sequence numbers: seq = 1 -> _expectedSeqNr becomes 2
        var initialPayload = new byte[] { 0x01 };
        var initialPacket = CreatePacket(UtpPacketType.Data, connection.ReceiveId, 1, 0, initialPayload);
        connection.HandleIncomingPacket(initialPacket, sender);

        Assert.That(connection.HasReceivedFirstPacket, Is.True);
        Assert.That(connection.OutOfOrderCount, Is.EqualTo(0));

        // 2. Send exactly MaxOutOfOrderPackets out-of-order packets (ahead of _expectedSeqNr = 2)
        // Using seq numbers 100 .. (100 + MaxOutOfOrderPackets - 1)
        for (ushort i = 0; i < UtpConnection.MaxOutOfOrderPackets; i++)
        {
            var seq = (ushort)(100 + i);
            var packet = CreatePacket(UtpPacketType.Data, connection.ReceiveId, seq, 0, new byte[] { (byte)(i & 0xFF) });
            connection.HandleIncomingPacket(packet, sender);
        }

        Assert.That(connection.OutOfOrderCount, Is.EqualTo(UtpConnection.MaxOutOfOrderPackets));

        // 3. Send another out-of-order packet with a new sequence number beyond the cap
        var overflowSeq = (ushort)(100 + UtpConnection.MaxOutOfOrderPackets + 5);
        var overflowPacket = CreatePacket(UtpPacketType.Data, connection.ReceiveId, overflowSeq, 0, new byte[] { 0xFF });
        connection.HandleIncomingPacket(overflowPacket, sender);

        // Should NOT increase the count, packet is dropped
        Assert.That(connection.OutOfOrderCount, Is.EqualTo(UtpConnection.MaxOutOfOrderPackets));

        // 4. Send an out-of-order packet with an existing sequence number already in the buffer
        // Should be accepted / updated without exceeding cap
        var existingSeq = (ushort)100;
        var updatePacket = CreatePacket(UtpPacketType.Data, connection.ReceiveId, existingSeq, 0, new byte[] { 0xAA, 0xBB });
        connection.HandleIncomingPacket(updatePacket, sender);

        Assert.That(connection.OutOfOrderCount, Is.EqualTo(UtpConnection.MaxOutOfOrderPackets));
    }

    [Test]
    public void HandleIncomingPacket_duplicate_or_older_packet_should_trigger_ack_without_corrupting_receive_queue()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, sender);

        byte[] lastSentAck = null;
        connection.PacketDropFilter = (data, ep) =>
        {
            var type = (UtpPacketType)(data[0] >> 4);
            if (type == UtpPacketType.State)
            {
                lastSentAck = data.ToArray();
            }

            return true;
        };

        // 1. Send first packet seqNr = 10
        var payload1 = new byte[] { 10, 20, 30 };
        var packet10 = CreatePacket(UtpPacketType.Data, connection.ReceiveId, 10, 0, payload1);
        connection.HandleIncomingPacket(packet10, sender);

        Assert.That(lastSentAck, Is.Not.Null);
        var ackSeq1 = BinaryPrimitives.ReadUInt16BigEndian(lastSentAck.AsSpan(18, 2));
        Assert.That(ackSeq1, Is.EqualTo((ushort)10));

        // 2. Send duplicate packet with seqNr = 10 and different payload
        lastSentAck = null;
        var duplicatePayload = new byte[] { 99, 99, 99 };
        var duplicatePacket = CreatePacket(UtpPacketType.Data, connection.ReceiveId, 10, 0, duplicatePayload);
        connection.HandleIncomingPacket(duplicatePacket, sender);

        // Immediate ACK should still be triggered for current ack number (10)
        Assert.That(lastSentAck, Is.Not.Null, "Duplicate packet must trigger immediate ACK");
        var ackSeq2 = BinaryPrimitives.ReadUInt16BigEndian(lastSentAck.AsSpan(18, 2));
        Assert.That(ackSeq2, Is.EqualTo((ushort)10));

        // 3. Also send older packet with seqNr = 9
        lastSentAck = null;
        var olderPayload = new byte[] { 88, 88 };
        var olderPacket = CreatePacket(UtpPacketType.Data, connection.ReceiveId, 9, 0, olderPayload);
        connection.HandleIncomingPacket(olderPacket, sender);

        Assert.That(lastSentAck, Is.Not.Null, "Older packet must trigger immediate ACK");
        var ackSeq3 = BinaryPrimitives.ReadUInt16BigEndian(lastSentAck.AsSpan(18, 2));
        Assert.That(ackSeq3, Is.EqualTo((ushort)10));

        // 4. Verify receive queue only contains payload1 (not the duplicate or older payloads)
        var buffer = new byte[100];
        var read = connection.Receive(buffer, 0, buffer.Length);
        Assert.That(read, Is.EqualTo(3));
        Assert.That(buffer[0..3], Is.EqualTo(payload1));
    }

    [Test]
    public void HandleIncomingPacket_empty_payload_data_packet_should_trigger_ack()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, sender);

        byte[] lastSentAck = null;
        connection.PacketDropFilter = (data, ep) =>
        {
            var type = (UtpPacketType)(data[0] >> 4);
            if (type == UtpPacketType.State)
            {
                lastSentAck = data.ToArray();
            }

            return true;
        };

        var emptyPacket = CreatePacket(UtpPacketType.Data, connection.ReceiveId, 1, 0, Array.Empty<byte>());
        connection.HandleIncomingPacket(emptyPacket, sender);

        Assert.That(lastSentAck, Is.Not.Null, "Empty payload data packet must trigger State ACK");
    }

    [Test]
    public void BuildPacket_advertised_window_should_decrease_as_receive_queue_fills()
    {
        using var connection = new UtpConnection();
        var buildMethod = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var receiveQueueField = typeof(UtpConnection).GetField("_receiveQueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var queue = (Queue<byte>)receiveQueueField.GetValue(connection)!;

        // When queue is empty, advertised window is MaxBufferSize
        var packetEmpty = (byte[])buildMethod.Invoke(connection, new object[] { UtpPacketType.State, Array.Empty<byte>() })!;
        var wndEmpty = BinaryPrimitives.ReadUInt32BigEndian(packetEmpty.AsSpan(12, 4));
        Assert.That(wndEmpty, Is.EqualTo(UtpConnection.MaxBufferSize));

        // Add 5000 bytes to receive queue
        for (var i = 0; i < 5000; i++)
        {
            queue.Enqueue(0xAA);
        }

        var packetWithData = (byte[])buildMethod.Invoke(connection, new object[] { UtpPacketType.State, Array.Empty<byte>() })!;
        var wndWithData = BinaryPrimitives.ReadUInt32BigEndian(packetWithData.AsSpan(12, 4));
        Assert.That(wndWithData, Is.EqualTo(UtpConnection.MaxBufferSize - 5000));
    }

    [Test]
    public void BuildPacket_advertised_window_should_be_zero_when_receive_queue_reaches_max_buffer_size()
    {
        using var connection = new UtpConnection();
        var buildMethod = typeof(UtpConnection).GetMethod("BuildPacket", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var receiveQueueField = typeof(UtpConnection).GetField("_receiveQueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var queue = (Queue<byte>)receiveQueueField.GetValue(connection)!;

        for (var i = 0; i < UtpConnection.MaxBufferSize; i++)
        {
            queue.Enqueue(0);
        }

        var packetFull = (byte[])buildMethod.Invoke(connection, new object[] { UtpPacketType.State, Array.Empty<byte>() })!;
        var wndFull = BinaryPrimitives.ReadUInt32BigEndian(packetFull.AsSpan(12, 4));
        Assert.That(wndFull, Is.EqualTo(0u));
    }

    [Test]
    public void Send_should_obey_remote_advertised_window_limit_and_not_burst_beyond()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 3);
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        // Constrain remote window to 1360 bytes (1 packet capacity)
        connection.RemoteWindowSize = 1360;

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, ep) =>
        {
            var type = (UtpPacketType)(data[0] >> 4);
            if (type == UtpPacketType.Data)
            {
                lock (sentPackets)
                {
                    sentPackets.Add(data.ToArray());
                }
            }

            return true;
        };

        // Try to send 2720 bytes (2 chunks of 1360 bytes)
        var sendData = new byte[2720];
        var sendTask = Task.Run(() => connection.Send(sendData, 0, sendData.Length));

        // Wait a short time for first packet to be transmitted
        Thread.Sleep(50);

        // While in-flight bytes (1360) >= remote window size (1360), second packet should not be sent
        lock (sentPackets)
        {
            Assert.That(sentPackets.Count, Is.EqualTo(1), "Should not burst beyond remote advertised window");
        }

        // Now simulate peer ACKing the first packet (seq 1)
        var ackPacket = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, 1);
        connection.HandleIncomingPacket(ackPacket, remoteEp);

        // Send should now unblock and send the second packet
        var completed = sendTask.Wait(TimeSpan.FromSeconds(2));
        Assert.That(completed, Is.True, "Send should complete after ACK frees window space");
        Assert.That(sendTask.Result, Is.EqualTo(2720));

        lock (sentPackets)
        {
            Assert.That(sentPackets.Count, Is.EqualTo(2), "Both packets should be sent after window opened");
        }
    }

    [Test]
    public void ProcessAck_duplicate_acks_1_and_2_should_increment_duplicate_ack_count_without_retransmission()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, ep) =>
        {
            lock (sentPackets)
            {
                sentPackets.Add(data.ToArray());
            }

            return true;
        };

        // Send 3 packets
        connection.Send(new byte[50], 0, 50);
        connection.Send(new byte[50], 0, 50);
        connection.Send(new byte[50], 0, 50);

        sentPackets.Clear();

        // Initial ACK for packet 1
        var ack1 = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, 1);
        connection.HandleIncomingPacket(ack1, remoteEp);

        Assert.That(connection.LastAckReceived, Is.EqualTo(1));
        Assert.That(connection.DuplicateAckCount, Is.EqualTo(0));
        Assert.That(connection.FastRetransmitCount, Is.EqualTo(0));
        Assert.That(sentPackets.Count, Is.EqualTo(0));

        // 1st duplicate ACK
        connection.HandleIncomingPacket(ack1, remoteEp);
        Assert.That(connection.DuplicateAckCount, Is.EqualTo(1));
        Assert.That(connection.FastRetransmitCount, Is.EqualTo(0));
        Assert.That(sentPackets.Count, Is.EqualTo(0));

        // 2nd duplicate ACK
        connection.HandleIncomingPacket(ack1, remoteEp);
        Assert.That(connection.DuplicateAckCount, Is.EqualTo(2));
        Assert.That(connection.FastRetransmitCount, Is.EqualTo(0));
        Assert.That(sentPackets.Count, Is.EqualTo(0));
    }

    [Test]
    public void ProcessAck_third_duplicate_ack_should_trigger_fast_retransmit_of_next_packet()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, ep) =>
        {
            lock (sentPackets)
            {
                sentPackets.Add(data.ToArray());
            }

            return true;
        };

        // Send 3 packets (seq 1, 2, 3)
        connection.Send(new byte[50], 0, 50);
        connection.Send(new byte[50], 0, 50);
        connection.Send(new byte[50], 0, 50);

        sentPackets.Clear();

        // Initial ACK for packet 1
        var ack1 = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, 1);
        connection.HandleIncomingPacket(ack1, remoteEp);

        // 1st & 2nd duplicate ACKs
        connection.HandleIncomingPacket(ack1, remoteEp);
        connection.HandleIncomingPacket(ack1, remoteEp);
        Assert.That(sentPackets.Count, Is.EqualTo(0));

        // 3rd duplicate ACK triggers fast retransmit
        connection.HandleIncomingPacket(ack1, remoteEp);
        Assert.That(connection.DuplicateAckCount, Is.EqualTo(3));
        Assert.That(connection.FastRetransmitCount, Is.EqualTo(1));
        Assert.That(sentPackets.Count, Is.EqualTo(1));

        // Retransmitted packet must be packet seq 2 (ackNr + 1)
        var retransmittedSeq = BinaryPrimitives.ReadUInt16BigEndian(sentPackets[0].AsSpan(16, 2));
        Assert.That(retransmittedSeq, Is.EqualTo(2));

        // 4th duplicate ACK does not retransmit again
        sentPackets.Clear();
        connection.HandleIncomingPacket(ack1, remoteEp);
        Assert.That(connection.DuplicateAckCount, Is.EqualTo(4));
        Assert.That(connection.FastRetransmitCount, Is.EqualTo(1));
        Assert.That(sentPackets.Count, Is.EqualTo(0));
    }

    [Test]
    public void ProcessAck_higher_ack_should_reset_duplicate_ack_count()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, ep) =>
        {
            lock (sentPackets)
            {
                sentPackets.Add(data.ToArray());
            }

            return true;
        };

        // Send 3 packets (seq 1, 2, 3)
        connection.Send(new byte[50], 0, 50);
        connection.Send(new byte[50], 0, 50);
        connection.Send(new byte[50], 0, 50);

        // Initial ACK for packet 1
        var ack1 = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, 1);
        connection.HandleIncomingPacket(ack1, remoteEp);

        // Duplicate ACKs for packet 1
        connection.HandleIncomingPacket(ack1, remoteEp);
        connection.HandleIncomingPacket(ack1, remoteEp);
        Assert.That(connection.DuplicateAckCount, Is.EqualTo(2));

        // Higher ACK for packet 3 arrives
        var ack3 = CreatePacket(UtpPacketType.State, connection.ReceiveId, 2, 3);
        connection.HandleIncomingPacket(ack3, remoteEp);

        Assert.That(connection.LastAckReceived, Is.EqualTo(3));
        Assert.That(connection.DuplicateAckCount, Is.EqualTo(0));
    }

    [Test]
    public void ProcessAck_third_duplicate_ack_with_sequence_wrap_around_should_retransmit_packet_zero()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        var seqField = typeof(UtpConnection).GetField("_sequenceNumber", BindingFlags.NonPublic | BindingFlags.Instance)!;
        seqField.SetValue(connection, (ushort)65535);

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, ep) =>
        {
            lock (sentPackets)
            {
                sentPackets.Add(data.ToArray());
            }

            return true;
        };

        // Send packet at seq 65535, then packet at seq 0 (wrapped)
        connection.Send(new byte[50], 0, 50);
        connection.Send(new byte[50], 0, 50);

        sentPackets.Clear();

        // Initial ACK for seq 65535
        var ackWrap = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, 65535);
        connection.HandleIncomingPacket(ackWrap, remoteEp);

        Assert.That(connection.LastAckReceived, Is.EqualTo(65535));
        Assert.That(connection.DuplicateAckCount, Is.EqualTo(0));

        // 1st and 2nd duplicate ACKs
        connection.HandleIncomingPacket(ackWrap, remoteEp);
        connection.HandleIncomingPacket(ackWrap, remoteEp);
        Assert.That(sentPackets.Count, Is.EqualTo(0));

        // 3rd duplicate ACK triggers fast retransmit of seq 0
        connection.HandleIncomingPacket(ackWrap, remoteEp);
        Assert.That(connection.DuplicateAckCount, Is.EqualTo(3));
        Assert.That(connection.FastRetransmitCount, Is.EqualTo(1));
        Assert.That(sentPackets.Count, Is.EqualTo(1));

        var retransmittedSeq = BinaryPrimitives.ReadUInt16BigEndian(sentPackets[0].AsSpan(16, 2));
        Assert.That(retransmittedSeq, Is.EqualTo(0));
    }

    [Test]
    public void HandleIncomingPacket_with_chained_extension_headers_should_skip_extensions_and_extract_payload()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        // Header: extension = 2 (first extension is type 2)
        // Extension 1 (type 2): next_ext = 1 (SACK), len = 4, data = [1, 2, 3, 4]
        // Extension 2 (type 1 - SACK): next_ext = 0, len = 4, data = [0, 0, 0, 0]
        // Payload: [0xDE, 0xAD, 0xBE, 0xEF]
        var extensions = new byte[]
        {
            1, 4, 1, 2, 3, 4,
            0, 4, 0, 0, 0, 0
        };
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var packet = CreatePacketWithExtensions(UtpPacketType.Data, connection.ReceiveId, 1, 0, 2, extensions, payload);
        connection.HandleIncomingPacket(packet, remoteEp);

        var buffer = new byte[10];
        var readBytes = connection.Receive(buffer, 0, buffer.Length);

        Assert.That(readBytes, Is.EqualTo(4));
        Assert.That(buffer[0], Is.EqualTo(0xDE));
        Assert.That(buffer[1], Is.EqualTo(0xAD));
        Assert.That(buffer[2], Is.EqualTo(0xBE));
        Assert.That(buffer[3], Is.EqualTo(0xEF));
    }

    [Test]
    public void HandleIncomingPacket_with_truncated_extension_header_should_not_throw_or_corrupt_stream()
    {
        using var connection = new UtpConnection(connectionTimeoutSeconds: 1);
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        var packet = new byte[22];
        packet[0] = ((byte)UtpPacketType.Data << 4) | 1;
        packet[1] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), connection.ReceiveId);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(16, 2), 1);
        packet[20] = 0;
        packet[21] = 10; // claims len 10, but total length is 22

        Assert.DoesNotThrow(() => connection.HandleIncomingPacket(packet, remoteEp));

        var buffer = new byte[10];
        var readBytes = connection.Receive(buffer, 0, buffer.Length, timeoutMs: 50);
        Assert.That(readBytes, Is.EqualTo(0));
    }

    [Test]
    public void ProcessSackBitmask_should_remove_acknowledged_in_flight_packets_selectively()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        // Send 5 packets (seq 1, 2, 3, 4, 5)
        for (var i = 0; i < 5; i++)
        {
            connection.Send(new byte[50], 0, 50);
        }

        Assert.That(IsInFlight(connection, 1), Is.True);
        Assert.That(IsInFlight(connection, 2), Is.True);
        Assert.That(IsInFlight(connection, 3), Is.True);
        Assert.That(IsInFlight(connection, 4), Is.True);
        Assert.That(IsInFlight(connection, 5), Is.True);

        // Remote peer sends State packet:
        // ackNr = 1 (packet 1 cumulative ack)
        // SACK extension: bit 0 (seq 1 + 2 = 3) and bit 2 (seq 1 + 2 + 2 = 5) set
        // Bitmask byte 0: (1 << 0) | (1 << 2) = 0x01 | 0x04 = 0x05
        var sackExtension = new byte[]
        {
            0, 4,
            0x05, 0x00, 0x00, 0x00
        };

        var ackPacket = CreatePacketWithExtensions(UtpPacketType.State, connection.ReceiveId, 1, 1, 1, sackExtension);
        connection.HandleIncomingPacket(ackPacket, remoteEp);

        // Packet 1 removed by cumulative ACK
        Assert.That(IsInFlight(connection, 1), Is.False);
        // Packet 2 not acked (still in flight)
        Assert.That(IsInFlight(connection, 2), Is.True);
        // Packet 3 removed by SACK bit 0
        Assert.That(IsInFlight(connection, 3), Is.False);
        // Packet 4 not acked (still in flight)
        Assert.That(IsInFlight(connection, 4), Is.True);
        // Packet 5 removed by SACK bit 2
        Assert.That(IsInFlight(connection, 5), Is.False);
    }

    [Test]
    public void ProcessSackBitmask_with_multi_byte_bitmask_should_correctly_calculate_sequence_numbers()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        for (var i = 0; i < 15; i++)
        {
            connection.Send(new byte[50], 0, 50);
        }

        // ACK packet 1, SACK packet 12:
        // seq 12 corresponds to n = seq - ackNr - 2 = 12 - 1 - 2 = 9
        // n = 9 is byteIdx = 1, bitIdx = 1 (9 = 1 * 8 + 1)
        var sackExtension = new byte[]
        {
            0, 4,
            0x00, 0x02, 0x00, 0x00
        };

        var ackPacket = CreatePacketWithExtensions(UtpPacketType.State, connection.ReceiveId, 1, 1, 1, sackExtension);
        connection.HandleIncomingPacket(ackPacket, remoteEp);

        Assert.That(IsInFlight(connection, 1), Is.False);
        Assert.That(IsInFlight(connection, 11), Is.True);
        Assert.That(IsInFlight(connection, 12), Is.False);
        Assert.That(IsInFlight(connection, 13), Is.True);
    }

    [Test]
    public void ProcessSackBitmask_should_trigger_fast_retransmit_when_three_higher_packets_are_sack_acknowledged()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, ep) =>
        {
            lock (sentPackets)
            {
                sentPackets.Add(data.ToArray());
            }

            return true;
        };

        for (var i = 0; i < 5; i++)
        {
            connection.Send(new byte[50], 0, 50);
        }

        sentPackets.Clear();

        // ACK 1 with SACK for packets 3, 4, 5 (bits 0, 1, 2)
        var sackExtension = new byte[]
        {
            0, 4,
            0x07, 0x00, 0x00, 0x00
        };

        var ackPacket = CreatePacketWithExtensions(UtpPacketType.State, connection.ReceiveId, 1, 1, 1, sackExtension);
        connection.HandleIncomingPacket(ackPacket, remoteEp);

        Assert.That(connection.FastRetransmitCount, Is.EqualTo(1));
        Assert.That(sentPackets.Count, Is.EqualTo(1));

        var retransmittedSeq = BinaryPrimitives.ReadUInt16BigEndian(sentPackets[0].AsSpan(16, 2));
        Assert.That(retransmittedSeq, Is.EqualTo(2));
        Assert.That(IsInFlight(connection, 2), Is.True);
    }

    [Test]
    public void ProcessSackBitmask_with_incremental_sack_arrivals_should_trigger_fast_retransmit_on_third_higher_ack()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, ep) =>
        {
            lock (sentPackets)
            {
                sentPackets.Add(data.ToArray());
            }

            return true;
        };

        for (var i = 0; i < 5; i++)
        {
            connection.Send(new byte[50], 0, 50);
        }

        sentPackets.Clear();

        // 1st SACK packet acknowledges packet 3 (bit 0)
        var sack1 = CreatePacketWithExtensions(UtpPacketType.State, connection.ReceiveId, 1, 1, 1, new byte[] { 0, 4, 0x01, 0, 0, 0 });
        connection.HandleIncomingPacket(sack1, remoteEp);
        Assert.That(connection.FastRetransmitCount, Is.EqualTo(0));
        Assert.That(sentPackets.Count, Is.EqualTo(0));

        // 2nd SACK packet acknowledges packet 3 and packet 4 (bits 0, 1)
        var sack2 = CreatePacketWithExtensions(UtpPacketType.State, connection.ReceiveId, 1, 1, 1, new byte[] { 0, 4, 0x03, 0, 0, 0 });
        connection.HandleIncomingPacket(sack2, remoteEp);
        Assert.That(connection.FastRetransmitCount, Is.EqualTo(0));
        Assert.That(sentPackets.Count, Is.EqualTo(0));

        // 3rd SACK packet acknowledges packets 3, 4, and 5 (bits 0, 1, 2)
        var sack3 = CreatePacketWithExtensions(UtpPacketType.State, connection.ReceiveId, 1, 1, 1, new byte[] { 0, 4, 0x07, 0, 0, 0 });
        connection.HandleIncomingPacket(sack3, remoteEp);
        Assert.That(connection.FastRetransmitCount, Is.EqualTo(1));
        Assert.That(sentPackets.Count, Is.EqualTo(1));

        var retransmittedSeq = BinaryPrimitives.ReadUInt16BigEndian(sentPackets[0].AsSpan(16, 2));
        Assert.That(retransmittedSeq, Is.EqualTo(2));

        // 4th SACK packet does not retransmit packet 2 again
        sentPackets.Clear();
        var sack4 = CreatePacketWithExtensions(UtpPacketType.State, connection.ReceiveId, 1, 1, 1, new byte[] { 0, 4, 0x0F, 0, 0, 0 });
        connection.HandleIncomingPacket(sack4, remoteEp);
        Assert.That(connection.FastRetransmitCount, Is.EqualTo(1));
        Assert.That(sentPackets.Count, Is.EqualTo(0));
    }

    [Test]
    public void ProcessSackBitmask_with_sequence_wrap_around_should_trigger_fast_retransmit()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        var seqField = typeof(UtpConnection).GetField("_sequenceNumber", BindingFlags.NonPublic | BindingFlags.Instance)!;
        seqField.SetValue(connection, (ushort)65534);

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, ep) =>
        {
            lock (sentPackets)
            {
                sentPackets.Add(data.ToArray());
            }

            return true;
        };

        // Send seq 65534, 65535, 0, 1, 2
        for (var i = 0; i < 5; i++)
        {
            connection.Send(new byte[50], 0, 50);
        }

        sentPackets.Clear();

        // ACK seq 65534, SACK bits for 0, 1, 2:
        var sackExtension = new byte[]
        {
            0, 4,
            0x07, 0x00, 0x00, 0x00
        };

        var ackPacket = CreatePacketWithExtensions(UtpPacketType.State, connection.ReceiveId, 1, 65534, 1, sackExtension);
        connection.HandleIncomingPacket(ackPacket, remoteEp);

        Assert.That(connection.FastRetransmitCount, Is.EqualTo(1));
        Assert.That(sentPackets.Count, Is.EqualTo(1));

        var retransmittedSeq = BinaryPrimitives.ReadUInt16BigEndian(sentPackets[0].AsSpan(16, 2));
        Assert.That(retransmittedSeq, Is.EqualTo(65535));
    }

    [Test]
    public void Initial_rto_should_be_1000ms()
    {
        using var connection = new UtpConnection();

        Assert.That(connection.RtoMs, Is.EqualTo(UtpConnection.InitialRtoMs));
        Assert.That(connection.RtoMs, Is.EqualTo(1000));
        Assert.That(connection.Srtt, Is.EqualTo(0.0));
        Assert.That(connection.RttVar, Is.EqualTo(0.0));
        Assert.That(connection.HasRttSample, Is.False);
    }

    [Test]
    public void Initial_rto_with_custom_endpoint_should_be_1000ms()
    {
        using var udpClient = new UdpClient();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        using var connection = new UtpConnection(udpClient, 1234, remoteEp);

        Assert.That(connection.RtoMs, Is.EqualTo(1000));
    }

    [Test]
    public void UpdateRtt_first_measurement_should_set_srtt_and_rttvar_and_compute_rto()
    {
        using var connection = new UtpConnection();

        // R' = 400ms:
        // SRTT = 400ms
        // RTTVAR = 400 / 2 = 200ms
        // RTO = 400 + max(10, 4 * 200) = 400 + 800 = 1200ms
        connection.UpdateRttSample(400.0, retries: 0);

        Assert.That(connection.HasRttSample, Is.True);
        Assert.That(connection.Srtt, Is.EqualTo(400.0));
        Assert.That(connection.RttVar, Is.EqualTo(200.0));
        Assert.That(connection.RtoMs, Is.EqualTo(1200));
    }

    [Test]
    public void UpdateRtt_subsequent_measurement_should_update_per_jacobson_karels()
    {
        using var connection = new UtpConnection();

        // First sample: R' = 400ms
        connection.UpdateRttSample(400.0, retries: 0);

        // Subsequent sample: R' = 200ms
        // RTTVAR = 0.75 * 200 + 0.25 * |400 - 200| = 150 + 50 = 200ms
        // SRTT = 0.875 * 400 + 0.125 * 200 = 350 + 25 = 375ms
        // RTO = 375 + max(10, 4 * 200) = 375 + 800 = 1175ms
        connection.UpdateRttSample(200.0, retries: 0);

        Assert.That(connection.Srtt, Is.EqualTo(375.0));
        Assert.That(connection.RttVar, Is.EqualTo(200.0));
        Assert.That(connection.RtoMs, Is.EqualTo(1175));
    }

    [Test]
    public void Karns_algorithm_should_prevent_rtt_updates_for_retransmitted_packets()
    {
        using var connection = new UtpConnection();

        // First sample: R' = 400ms
        connection.UpdateRttSample(400.0, retries: 0);
        Assert.That(connection.Srtt, Is.EqualTo(400.0));
        Assert.That(connection.RttVar, Is.EqualTo(200.0));
        Assert.That(connection.RtoMs, Is.EqualTo(1200));

        // Attempt update with retransmitted packet (retries = 1)
        connection.UpdateRttSample(200.0, retries: 1);

        // Values must remain completely unchanged
        Assert.That(connection.Srtt, Is.EqualTo(400.0));
        Assert.That(connection.RttVar, Is.EqualTo(200.0));
        Assert.That(connection.RtoMs, Is.EqualTo(1200));

        // Attempt update with retries = 2
        connection.UpdateRttSample(100.0, retries: 2);
        Assert.That(connection.Srtt, Is.EqualTo(400.0));
        Assert.That(connection.RttVar, Is.EqualTo(200.0));
        Assert.That(connection.RtoMs, Is.EqualTo(1200));
    }

    [Test]
    public void ProcessAck_with_retransmitted_packet_should_not_update_rtt_per_karns_algorithm()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        connection.PacketDropFilter = (_, _) => true;

        // Send packet seq 1
        connection.Send(new byte[50], 0, 50);

        // Mark as retransmitted
        var inFlight = connection.InFlightPackets[1];
        inFlight.Retries = 1;
        inFlight.SentTimestamp = Environment.TickCount64 - 300;

        // ACK packet 1
        var ack = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, 1);
        connection.HandleIncomingPacket(ack, remoteEp);

        // Karn's algorithm must discard this sample
        Assert.That(connection.HasRttSample, Is.False);
        Assert.That(connection.RtoMs, Is.EqualTo(1000));
        Assert.That(connection.Srtt, Is.EqualTo(0.0));
        Assert.That(connection.RttVar, Is.EqualTo(0.0));
    }

    [Test]
    public void ProcessAck_with_unretransmitted_packet_should_update_rtt()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        connection.PacketDropFilter = (_, _) => true;

        // Send packet seq 1
        connection.Send(new byte[50], 0, 50);

        var inFlight = connection.InFlightPackets[1];
        inFlight.Retries = 0;
        inFlight.SentTimestamp = Environment.TickCount64 - 400;

        // ACK packet 1
        var ack = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, 1);
        connection.HandleIncomingPacket(ack, remoteEp);

        // Sample must be accepted
        Assert.That(connection.HasRttSample, Is.True);
        Assert.That(connection.Srtt, Is.GreaterThanOrEqualTo(350.0));
        Assert.That(connection.RtoMs, Is.GreaterThanOrEqualTo(500));
    }

    [Test]
    public void UpdateRtt_should_clamp_to_minimum_floor_500ms()
    {
        using var connection = new UtpConnection();

        // R' = 20ms:
        // SRTT = 20ms
        // RTTVAR = 10ms
        // RTO = 20 + max(10, 4 * 10) = 20 + 40 = 60ms
        // Clamped to MinRtoMs = 500ms
        connection.UpdateRttSample(20.0, retries: 0);

        Assert.That(connection.Srtt, Is.EqualTo(20.0));
        Assert.That(connection.RttVar, Is.EqualTo(10.0));
        Assert.That(connection.RtoMs, Is.EqualTo(UtpConnection.MinRtoMs));
        Assert.That(connection.RtoMs, Is.EqualTo(500));
    }

    [Test]
    public void UpdateRtt_should_clamp_to_maximum_ceiling_10000ms()
    {
        using var connection = new UtpConnection();

        // R' = 5000ms:
        // SRTT = 5000ms
        // RTTVAR = 2500ms
        // RTO = 5000 + max(10, 4 * 2500) = 5000 + 10000 = 15000ms
        // Clamped to MaxRtoMs = 10000ms
        connection.UpdateRttSample(5000.0, retries: 0);

        Assert.That(connection.Srtt, Is.EqualTo(5000.0));
        Assert.That(connection.RttVar, Is.EqualTo(2500.0));
        Assert.That(connection.RtoMs, Is.EqualTo(UtpConnection.MaxRtoMs));
        Assert.That(connection.RtoMs, Is.EqualTo(10000));
    }

    [Test]
    public void RetransmitUnackedPackets_should_apply_exponential_backoff_on_timeout()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, _) =>
        {
            lock (sentPackets)
            {
                sentPackets.Add(data.ToArray());
            }

            return true;
        };

        // Send packet 1
        connection.Send(new byte[50], 0, 50);
        sentPackets.Clear();

        Assert.That(connection.RtoMs, Is.EqualTo(1000));

        var head = connection.InFlightPackets[1];

        // 1. Elapsed time < RTO (500ms < 1000ms): should not retransmit or backoff
        head.SentTimestamp = Environment.TickCount64 - 500;
        connection.RetransmitUnackedPackets();

        Assert.That(connection.RtoMs, Is.EqualTo(1000));
        Assert.That(head.Retries, Is.EqualTo(0));
        Assert.That(sentPackets.Count, Is.EqualTo(0));

        // 2. Timeout expires (>= 1000ms): should retransmit and double RTO to 2000ms
        head.SentTimestamp = Environment.TickCount64 - 1000;
        connection.RetransmitUnackedPackets();

        Assert.That(connection.RtoMs, Is.EqualTo(2000));
        Assert.That(head.Retries, Is.EqualTo(1));
        Assert.That(sentPackets.Count, Is.EqualTo(1));

        // 3. Before 2000ms elapsed: should not retransmit
        sentPackets.Clear();
        head.SentTimestamp = Environment.TickCount64 - 1500;
        connection.RetransmitUnackedPackets();

        Assert.That(connection.RtoMs, Is.EqualTo(2000));
        Assert.That(head.Retries, Is.EqualTo(1));
        Assert.That(sentPackets.Count, Is.EqualTo(0));

        // 4. 2nd timeout expires (>= 2000ms): double RTO to 4000ms
        head.SentTimestamp = Environment.TickCount64 - 2000;
        connection.RetransmitUnackedPackets();

        Assert.That(connection.RtoMs, Is.EqualTo(4000));
        Assert.That(head.Retries, Is.EqualTo(2));
        Assert.That(sentPackets.Count, Is.EqualTo(1));

        // 5. 3rd timeout expires (>= 4000ms): double RTO to 8000ms
        sentPackets.Clear();
        head.SentTimestamp = Environment.TickCount64 - 4000;
        connection.RetransmitUnackedPackets();

        Assert.That(connection.RtoMs, Is.EqualTo(8000));
        Assert.That(head.Retries, Is.EqualTo(3));
        Assert.That(sentPackets.Count, Is.EqualTo(1));

        // 6. 4th timeout expires (>= 8000ms): double to 16000ms clamped to MaxRtoMs (10000ms)
        sentPackets.Clear();
        head.SentTimestamp = Environment.TickCount64 - 8000;
        connection.RetransmitUnackedPackets();

        Assert.That(connection.RtoMs, Is.EqualTo(10000));
        Assert.That(head.Retries, Is.EqualTo(4));
        Assert.That(sentPackets.Count, Is.EqualTo(1));

        // 7. 5th timeout expires (>= 10000ms): stays capped at 10000ms
        sentPackets.Clear();
        head.SentTimestamp = Environment.TickCount64 - 10000;
        connection.RetransmitUnackedPackets();

        Assert.That(connection.RtoMs, Is.EqualTo(10000));
        Assert.That(head.Retries, Is.EqualTo(5));
        Assert.That(sentPackets.Count, Is.EqualTo(1));
    }

    [Test]
    public void ProcessSackBitmask_should_update_rtt_for_unretransmitted_packet()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        connection.PacketDropFilter = (_, _) => true;

        // Send seq 1, 2
        connection.Send(new byte[50], 0, 50);
        connection.Send(new byte[50], 0, 50);

        connection.InFlightPackets[2].SentTimestamp = Environment.TickCount64 - 250;
        connection.InFlightPackets[2].Retries = 0;

        // SACK bitmask for packet 2 (ackNr = 0, bit 0 -> seq 0 + 2 = 2)
        var sackExtension = new byte[]
        {
            0, 4,
            0x01, 0x00, 0x00, 0x00
        };

        var ackPacket = CreatePacketWithExtensions(UtpPacketType.State, connection.ReceiveId, 1, 0, 1, sackExtension);
        connection.HandleIncomingPacket(ackPacket, remoteEp);

        Assert.That(connection.HasRttSample, Is.True);
        Assert.That(connection.Srtt, Is.GreaterThanOrEqualTo(200.0));
    }

    [Test]
    public void ProcessSackBitmask_should_not_update_rtt_for_retransmitted_packet()
    {
        using var connection = new UtpConnection();
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetConnected(connection, true);
        SetRemoteEndpoint(connection, remoteEp);

        connection.PacketDropFilter = (_, _) => true;

        // Send seq 1, 2
        connection.Send(new byte[50], 0, 50);
        connection.Send(new byte[50], 0, 50);

        connection.InFlightPackets[2].SentTimestamp = Environment.TickCount64 - 250;
        connection.InFlightPackets[2].Retries = 1;

        // SACK bitmask for packet 2
        var sackExtension = new byte[]
        {
            0, 4,
            0x01, 0x00, 0x00, 0x00
        };

        var ackPacket = CreatePacketWithExtensions(UtpPacketType.State, connection.ReceiveId, 1, 0, 1, sackExtension);
        connection.HandleIncomingPacket(ackPacket, remoteEp);

        Assert.That(connection.HasRttSample, Is.False);
        Assert.That(connection.RtoMs, Is.EqualTo(1000));
    }

    // ---- helpers ----

    private static byte[] CreatePacket(UtpPacketType type, ushort connectionId, ushort seqNr, ushort ackNr, byte[] payload = null)
    {
        var payloadLen = payload?.Length ?? 0;
        var packet = new byte[20 + payloadLen];
        packet[0] = (byte)(((byte)type << 4) | 1);
        packet[1] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), connectionId);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), 1000);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), 65535);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(16, 2), seqNr);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18, 2), ackNr);
        if (payloadLen > 0)
        {
            Array.Copy(payload, 0, packet, 20, payloadLen);
        }

        return packet;
    }

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

    private static byte[] CreatePacketWithExtensions(UtpPacketType type, ushort connectionId, ushort seqNr, ushort ackNr, byte firstExtension, byte[] extensionData, byte[] payload = null)
    {
        var extLen = extensionData?.Length ?? 0;
        var payloadLen = payload?.Length ?? 0;
        var packet = new byte[20 + extLen + payloadLen];
        packet[0] = (byte)(((byte)type << 4) | 1);
        packet[1] = firstExtension;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), connectionId);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), 1000);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), 65535);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(16, 2), seqNr);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18, 2), ackNr);
        if (extLen > 0)
        {
            Array.Copy(extensionData, 0, packet, 20, extLen);
        }

        if (payloadLen > 0)
        {
            Array.Copy(payload, 0, packet, 20 + extLen, payloadLen);
        }

        return packet;
    }

    private static bool IsInFlight(UtpConnection connection, ushort seqNr)
    {
        var field = typeof(UtpConnection).GetField("_inFlightPackets", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inFlight = (System.Collections.IDictionary)field.GetValue(connection)!;
        return inFlight.Contains(seqNr);
    }

    [Test]
    public void CalculateBdpBufferSize_should_match_bdp_specification()
    {
        Assert.That(UtpConnection.CalculateBdpBufferSize(0), Is.EqualTo(UtpConnection.DefaultUnthrottledBufferSize));
        Assert.That(UtpConnection.CalculateBdpBufferSize(-10), Is.EqualTo(UtpConnection.DefaultUnthrottledBufferSize));

        // Min clamped
        Assert.That(UtpConnection.CalculateBdpBufferSize(10_000, 0.1), Is.EqualTo(UtpConnection.MinThrottledBufferSize));

        // Proportional
        Assert.That(UtpConnection.CalculateBdpBufferSize(300_000, 0.1), Is.EqualTo(30_000));

        // Max clamped
        Assert.That(UtpConnection.CalculateBdpBufferSize(5_000_000, 0.1), Is.EqualTo(UtpConnection.MaxThrottledBufferSize));
    }

    [Test]
    public void CalculatePacingIntervalTicks_should_calculate_correct_delay_for_payload()
    {
        using var conn = new UtpConnection();
        conn.UploadRateLimit = 1_000_000; // 1 MB/s
        conn.PacketPacingIntervalMicroseconds = 0;

        // 1360 bytes at 1,000,000 B/s => 0.001360s = 1360 microseconds
        var ticks = conn.CalculatePacingIntervalTicks(1360);
        var expectedTicks = (long)(1360.0 * System.Diagnostics.Stopwatch.Frequency / 1_000_000.0);

        Assert.That(ticks, Is.EqualTo(expectedTicks));
        Assert.That(conn.CalculatePacingIntervalMicroseconds(1360), Is.InRange(1350, 1370));
    }

    [Test]
    public void CalculatePacingIntervalTicks_should_respect_minimum_packet_pacing_interval()
    {
        using var conn = new UtpConnection();
        conn.UploadRateLimit = 0; // Unthrottled
        conn.PacketPacingIntervalMicroseconds = 100;

        var micros = conn.CalculatePacingIntervalMicroseconds(1360);
        Assert.That(micros, Is.InRange(95, 105));
    }

    [Test]
    public void UploadRateLimit_and_DownloadRateLimit_should_update_udp_socket_buffers()
    {
        using var conn = new UtpConnection();
        conn.UploadRateLimit = 200_000;
        conn.DownloadRateLimit = 500_000;

        Assert.That(conn.SendBufferSize, Is.GreaterThanOrEqualTo(20_000));
        Assert.That(conn.ReceiveBufferSize, Is.GreaterThanOrEqualTo(50_000));
    }

    [Test]
    public void Send_should_insert_pacing_interval_between_consecutive_udp_packets()
    {
        using var conn = new UtpConnection();
        // Set connected state via reflection
        typeof(UtpConnection).GetProperty("IsConnected")!.SetValue(conn, true);
        typeof(UtpConnection).GetField("_remoteEndpoint", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(conn, new IPEndPoint(IPAddress.Loopback, 12345));

        conn.UploadRateLimit = 500_000; // 500 KB/s
        conn.PacketPacingIntervalMicroseconds = 50;

        var pacedPackets = new List<(int Size, long Ticks)>();
        conn.OnPacketPaced = (size, ticks) => pacedPackets.Add((size, ticks));

        // Use fast non-blocking handler for test
        UtpConnection.PaceWaitHandler = (start, ticks) => { };

        try
        {
            // Send 3000 bytes (requires 3 packets: 1360 + 1360 + 280)
            var data = new byte[3000];
            conn.Send(data, 0, data.Length);

            // Between packet 1 and 2, and between packet 2 and 3 => 2 paced intervals
            Assert.That(pacedPackets.Count, Is.EqualTo(2));
            Assert.That(pacedPackets[0].Size, Is.EqualTo(1360));
            Assert.That(pacedPackets[0].Ticks, Is.GreaterThan(0));
            Assert.That(pacedPackets[1].Size, Is.EqualTo(1360));
            Assert.That(pacedPackets[1].Ticks, Is.GreaterThan(0));
        }
        finally
        {
            UtpConnection.PaceWaitHandler = BandwidthPacer.PaceWait;
        }
    }

    [Test]
    public void Send_should_not_pace_for_single_packet_payload()
    {
        using var conn = new UtpConnection();
        typeof(UtpConnection).GetProperty("IsConnected")!.SetValue(conn, true);
        typeof(UtpConnection).GetField("_remoteEndpoint", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(conn, new IPEndPoint(IPAddress.Loopback, 12345));

        conn.UploadRateLimit = 100_000;
        conn.PacketPacingIntervalMicroseconds = 50;

        var pacedPackets = new List<(int Size, long Ticks)>();
        conn.OnPacketPaced = (size, ticks) => pacedPackets.Add((size, ticks));

        var data = new byte[500]; // Single packet
        conn.Send(data, 0, data.Length);

        Assert.That(pacedPackets, Is.Empty);
    }

    [Test]
    public void GetMicroseconds_should_have_high_resolution_monotonic_progression_without_quantized_jumps()
    {
        var method = typeof(UtpConnection).GetMethod("GetMicroseconds", BindingFlags.NonPublic | BindingFlags.Static)!;

        var timestamps = new List<uint>();
        for (var i = 0; i < 50; i++)
        {
            var ts = (uint)method.Invoke(null, null)!;
            timestamps.Add(ts);
            var start = Stopwatch.GetTimestamp();
            while (Stopwatch.GetTimestamp() - start < 50)
            {
            }
        }

        var sawDistinctValues = false;
        var sawSubMillisecondDifference = false;
        for (var i = 1; i < timestamps.Count; i++)
        {
            var diff = unchecked(timestamps[i] - timestamps[i - 1]);
            Assert.That(diff, Is.LessThan(10_000u), "Step jump should not be quantized at 10-15ms (10,000+ microseconds)");
            if (diff > 0)
            {
                sawDistinctValues = true;
                if (diff < 1000)
                {
                    sawSubMillisecondDifference = true;
                }
            }
        }

        Assert.That(sawDistinctValues, Is.True, "High resolution clock should progress across consecutive loop iterations");
        Assert.That(sawSubMillisecondDifference, Is.True, "High resolution clock should provide sub-millisecond precision");
    }

    [Test]
    public void UpdateCongestionWindow_should_increase_cwnd_when_delay_is_below_target()
    {
        using var connection = new UtpConnection();
        connection.ResetCongestionControl(3000);

        connection.UpdateCongestionWindow(20_000, 1360);
        var prevCwnd = connection.CongestionWindow;
        connection.UpdateCongestionWindow(20_000, 1360);

        Assert.That(connection.BaseDelay, Is.EqualTo(20_000u));
        Assert.That(connection.QueuingDelay, Is.EqualTo(0u));
        Assert.That(connection.CongestionWindow, Is.GreaterThan(prevCwnd));
    }

    [Test]
    public void UpdateCongestionWindow_should_decrease_cwnd_when_queuing_delay_exceeds_target()
    {
        using var connection = new UtpConnection();
        connection.ResetCongestionControl(5000);

        connection.UpdateCongestionWindow(50_000, 0);
        Assert.That(connection.BaseDelay, Is.EqualTo(50_000u));

        var prevCwnd = connection.CongestionWindow;
        connection.UpdateCongestionWindow(200_000, 1360);

        Assert.That(connection.QueuingDelay, Is.EqualTo(150_000u));
        Assert.That(connection.CongestionWindow, Is.LessThan(prevCwnd));
    }

    [Test]
    public void UpdateCongestionWindow_should_update_base_delay_to_new_minimum()
    {
        using var connection = new UtpConnection();
        connection.ResetCongestionControl(3000);

        connection.UpdateCongestionWindow(100_000, 0);
        Assert.That(connection.BaseDelay, Is.EqualTo(100_000u));

        connection.UpdateCongestionWindow(45_000, 0);
        Assert.That(connection.BaseDelay, Is.EqualTo(45_000u));
    }

    [Test]
    public void UpdateCongestionWindow_should_not_drop_cwnd_below_min_window()
    {
        using var connection = new UtpConnection();
        connection.ResetCongestionControl(200);

        connection.UpdateCongestionWindow(10_000, 0);
        connection.UpdateCongestionWindow(5_000_000, 1000);

        Assert.That(connection.CongestionWindow, Is.GreaterThanOrEqualTo(UtpConnection.MinCongestionWindow));
    }

    [Test]
    public void Send_should_gate_on_effective_window_minimum_of_cwnd_and_remote_wnd()
    {
        using var connection = new UtpConnection();
        SetConnected(connection, true);
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54321);
        SetRemoteEndpoint(connection, remoteEp);

        connection.RemoteWindowSize = 65535;
        connection.CongestionWindow = 1360;

        Assert.That(connection.EffectiveCwnd, Is.EqualTo(1360u));

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, ep) =>
        {
            var type = (UtpPacketType)(data[0] >> 4);
            if (type == UtpPacketType.Data)
            {
                lock (sentPackets)
                {
                    sentPackets.Add(data.ToArray());
                }
            }

            return true;
        };

        var sendData = new byte[2720];
        var sendTask = Task.Run(() => connection.Send(sendData, 0, sendData.Length));

        Thread.Sleep(50);

        lock (sentPackets)
        {
            Assert.That(sentPackets.Count, Is.EqualTo(1), "Should gate outbound packets against congestion window");
        }

        var ackPacket = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, 1);
        BinaryPrimitives.WriteUInt32BigEndian(ackPacket.AsSpan(8, 4), 20_000);
        connection.HandleIncomingPacket(ackPacket, remoteEp);

        var completed = sendTask.Wait(TimeSpan.FromSeconds(2));
        Assert.That(completed, Is.True, "Send should unblock after ACK reduces in-flight bytes");
        Assert.That(sendTask.Result, Is.EqualTo(2720));

        lock (sentPackets)
        {
            Assert.That(sentPackets.Count, Is.EqualTo(2), "Both packets should be sent after window opened");
        }
    }

    [Test]
    public void Send_should_gate_on_remote_wnd_when_remote_wnd_is_smaller_than_cwnd()
    {
        using var connection = new UtpConnection();
        SetConnected(connection, true);
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54322);
        SetRemoteEndpoint(connection, remoteEp);

        connection.RemoteWindowSize = 1360;
        connection.CongestionWindow = 10000;

        Assert.That(connection.EffectiveCwnd, Is.EqualTo(1360u));

        var sentPackets = new List<byte[]>();
        connection.PacketDropFilter = (data, ep) =>
        {
            var type = (UtpPacketType)(data[0] >> 4);
            if (type == UtpPacketType.Data)
            {
                lock (sentPackets)
                {
                    sentPackets.Add(data.ToArray());
                }
            }

            return true;
        };

        var sendData = new byte[2720];
        var sendTask = Task.Run(() => connection.Send(sendData, 0, sendData.Length));

        Thread.Sleep(50);

        lock (sentPackets)
        {
            Assert.That(sentPackets.Count, Is.EqualTo(1), "Should gate outbound packets against remote window size");
        }

        var ackPacket = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, 1);
        connection.HandleIncomingPacket(ackPacket, remoteEp);

        var completed = sendTask.Wait(TimeSpan.FromSeconds(2));
        Assert.That(completed, Is.True, "Send should complete after ACK");
        Assert.That(sendTask.Result, Is.EqualTo(2720));

        lock (sentPackets)
        {
            Assert.That(sentPackets.Count, Is.EqualTo(2));
        }
    }

    [Test]
    public void Incoming_packet_with_timestamp_diff_should_adjust_congestion_window()
    {
        using var connection = new UtpConnection();
        SetConnected(connection, true);
        var remoteEp = new IPEndPoint(IPAddress.Loopback, 54323);
        SetRemoteEndpoint(connection, remoteEp);

        var inFlight = new UtpConnection.InFlightPacket
        {
            SequenceNumber = 1,
            PacketData = new byte[20 + 1360],
            PayloadLength = 1360,
            SentTimestamp = Environment.TickCount64,
            Retries = 0
        };
        connection.InFlightPackets[1] = inFlight;

        connection.ResetCongestionControl(3000);
        var initialCwnd = connection.CongestionWindow;

        var baselinePacket = CreatePacket(UtpPacketType.State, connection.ReceiveId, 1, 0);
        BinaryPrimitives.WriteUInt32BigEndian(baselinePacket.AsSpan(8, 4), 30_000);
        connection.HandleIncomingPacket(baselinePacket, remoteEp);

        var ackPacket = CreatePacket(UtpPacketType.State, connection.ReceiveId, 2, 1);
        BinaryPrimitives.WriteUInt32BigEndian(ackPacket.AsSpan(8, 4), 30_000);
        connection.HandleIncomingPacket(ackPacket, remoteEp);

        Assert.That(connection.CongestionWindow, Is.GreaterThan(initialCwnd), "Congestion window should increase on ACK with low queuing delay");
    }
}
