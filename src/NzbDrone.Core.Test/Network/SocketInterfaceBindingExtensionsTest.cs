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
}
