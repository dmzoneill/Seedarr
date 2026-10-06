using System;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class SocketInterfaceBindingExtensionsTest
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void BindToNetworkInterface_when_interface_name_is_null_or_whitespace_binds_to_local_ip(string interfaceName)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        socket.BindToNetworkInterface(interfaceName, IPAddress.Loopback, 0);

        Assert.That(socket.IsBound, Is.True);
        Assert.That(socket.LocalEndPoint, Is.Not.Null);
        var endpoint = (IPEndPoint)socket.LocalEndPoint;
        Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Loopback));
        Assert.That(endpoint.Port, Is.GreaterThan(0));
    }

    [Test]
    public void BindToNetworkInterface_handles_socket_exceptions_gracefully_without_crashing()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        Assert.DoesNotThrow(() =>
        {
            socket.BindToNetworkInterface("nonexistent_device_seedarr_test", IPAddress.Loopback, 0);
        });

        Assert.That(socket.IsBound, Is.True);
    }

    [Test]
    public void BindToNetworkInterface_throws_when_socket_is_null()
    {
        Socket socket = null;

        Assert.Throws<ArgumentNullException>(() =>
        {
            socket.BindToNetworkInterface("eth0");
        });
    }

    [Test]
    public void BindToNetworkInterface_when_interface_and_local_ip_null_does_not_bind_and_does_not_throw()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        Assert.DoesNotThrow(() =>
        {
            socket.BindToNetworkInterface(null, null);
        });

        Assert.That(socket.IsBound, Is.False);
    }

    [Test]
    public void BindToNetworkInterface_when_ipv6_socket_with_ipv4_local_ip_maps_to_ipv6()
    {
        using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

        socket.BindToNetworkInterface(null, IPAddress.Loopback, 0);

        Assert.That(socket.IsBound, Is.True);
        Assert.That(socket.LocalEndPoint, Is.Not.Null);
        var endpoint = (IPEndPoint)socket.LocalEndPoint;
        Assert.That(endpoint.AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
        Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Loopback.MapToIPv6()));
    }

    [Test]
    public void BindToNetworkInterface_when_ipv6_socket_with_ipv4_any_maps_to_ipv6_any()
    {
        using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

        socket.BindToNetworkInterface(null, IPAddress.Any, 0);

        Assert.That(socket.IsBound, Is.True);
        Assert.That(socket.LocalEndPoint, Is.Not.Null);
        var endpoint = (IPEndPoint)socket.LocalEndPoint;
        Assert.That(endpoint.AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
        Assert.That(endpoint.Address, Is.EqualTo(IPAddress.IPv6Any));
    }

    [Test]
    public void BindToNetworkInterface_when_ipv4_socket_with_mapped_ipv6_local_ip_maps_to_ipv4()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        socket.BindToNetworkInterface(null, IPAddress.Loopback.MapToIPv6(), 0);

        Assert.That(socket.IsBound, Is.True);
        Assert.That(socket.LocalEndPoint, Is.Not.Null);
        var endpoint = (IPEndPoint)socket.LocalEndPoint;
        Assert.That(endpoint.AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
        Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Loopback));
    }

    [Test]
    public void BindToNetworkInterface_when_ipv4_socket_with_ipv6_any_maps_to_ipv4_any()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        socket.BindToNetworkInterface(null, IPAddress.IPv6Any, 0);

        Assert.That(socket.IsBound, Is.True);
        Assert.That(socket.LocalEndPoint, Is.Not.Null);
        var endpoint = (IPEndPoint)socket.LocalEndPoint;
        Assert.That(endpoint.AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
        Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Any));
    }

    [Test]
    public void BindToNetworkInterface_when_ipv4_socket_with_unmappable_ipv6_leaves_unbound()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        Assert.DoesNotThrow(() =>
        {
            socket.BindToNetworkInterface(null, IPAddress.IPv6Loopback, 0);
        });

        Assert.That(socket.IsBound, Is.False);
    }

    [Test]
    public void BindToNetworkInterface_when_interface_is_ip_string_and_local_ip_null_binds_to_ip()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        socket.BindToNetworkInterface("127.0.0.1", null, 0);

        Assert.That(socket.IsBound, Is.True);
        Assert.That(socket.LocalEndPoint, Is.Not.Null);
        var endpoint = (IPEndPoint)socket.LocalEndPoint;
        Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Loopback));
    }

    [Test]
    public void BindToNetworkInterface_when_bind_throws_socket_exception_does_not_crash()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var unreachableIp = IPAddress.Parse("198.51.100.1");

        Assert.DoesNotThrow(() =>
        {
            socket.BindToNetworkInterface(null, unreachableIp, 0);
        });

        Assert.That(socket.IsBound, Is.False);
    }
}
