using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Test.Network
{
    [TestFixture]
    public class NetworkStatusServiceTest
    {
        private IUpnpService _upnpService;
        private IExternalIpService _externalIpService;
        private IProxySettingsProvider _proxySettings;
        private IConfigService _configService;
        private IVpnKillSwitchService _vpnKillSwitchService;
        private NetworkStatusService _subject;

        [SetUp]
        public void SetUp()
        {
            _upnpService = Substitute.For<IUpnpService>();
            _externalIpService = Substitute.For<IExternalIpService>();
            _proxySettings = Substitute.For<IProxySettingsProvider>();
            _configService = Substitute.For<IConfigService>();
            _vpnKillSwitchService = Substitute.For<IVpnKillSwitchService>();

            _upnpService.GetMappings().Returns(new List<PortMapping>());
            _upnpService.IsAvailable.Returns(false);
            _upnpService.ExternalIp.Returns(string.Empty);
            _externalIpService.CachedIp.Returns(string.Empty);
            _proxySettings.IsEnabled.Returns(false);

            _subject = new NetworkStatusService(_upnpService, _externalIpService, _proxySettings, _configService, _vpnKillSwitchService);
        }

        [Test]
        public void GetStatus_should_use_upnp_external_ip_when_available()
        {
            _upnpService.ExternalIp.Returns("203.0.113.10");

            var result = _subject.GetStatus();

            Assert.That(result.ExternalIp, Is.EqualTo("203.0.113.10"));
        }

        [Test]
        public void GetStatus_should_fall_back_to_cached_ip_when_upnp_empty()
        {
            _upnpService.ExternalIp.Returns(string.Empty);
            _externalIpService.CachedIp.Returns("198.51.100.5");

            var result = _subject.GetStatus();

            Assert.That(result.ExternalIp, Is.EqualTo("198.51.100.5"));
        }

        [Test]
        public void GetStatus_should_call_get_external_ip_async_when_both_empty()
        {
            _upnpService.ExternalIp.Returns(string.Empty);
            _externalIpService.CachedIp.Returns(string.Empty);
            _externalIpService.GetExternalIpAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult("192.0.2.1"));

            var result = _subject.GetStatus();

            _externalIpService.Received().GetExternalIpAsync(Arg.Any<CancellationToken>());
        }

        [Test]
        public void GetStatus_should_return_valid_status_object()
        {
            _upnpService.ExternalIp.Returns("203.0.113.10");

            var result = _subject.GetStatus();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.LocalIp, Is.Not.Null);
        }

        [Test]
        public void GetStatus_should_return_upnp_availability()
        {
            _upnpService.IsAvailable.Returns(true);

            var result = _subject.GetStatus();

            Assert.That(result.UpnpAvailable, Is.True);
        }

        [Test]
        public void GetStatus_should_return_proxy_enabled_status()
        {
            _proxySettings.IsEnabled.Returns(true);

            var result = _subject.GetStatus();

            Assert.That(result.ProxyEnabled, Is.True);
        }

        [Test]
        public void GetStatus_should_return_port_mappings()
        {
            var mappings = new List<PortMapping>
            {
                new PortMapping
                {
                    InternalPort = 8080,
                    ExternalPort = 80,
                    Protocol = "TCP",
                    Description = "Web",
                    IsActive = true
                }
            };

            _upnpService.GetMappings().Returns(mappings);

            var result = _subject.GetStatus();

            Assert.That(result.PortMappings, Has.Count.EqualTo(1));
            Assert.That(result.PortMappings[0].InternalPort, Is.EqualTo(8080));
            Assert.That(result.PortMappings[0].ExternalPort, Is.EqualTo(80));
        }

        [Test]
        public void GetLocalAddresses_should_return_list()
        {
            var result = _subject.GetLocalAddresses();

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<List<string>>());
        }

        [Test]
        public async Task TestPortAsync_should_return_open_when_listener_is_running()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            try
            {
                _upnpService.ExternalIp.Returns("127.0.0.1");

                var result = await _subject.TestPortAsync(port);

                Assert.That(result.IsOpen, Is.True);
                Assert.That(result.Port, Is.EqualTo(port));
                Assert.That(result.ExternalIp, Is.EqualTo("127.0.0.1"));
                Assert.That(result.ErrorMessage, Is.Null);
                Assert.That(result.ResponseTime, Is.GreaterThanOrEqualTo(System.TimeSpan.Zero));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public async Task TestPortAsync_should_return_closed_when_port_is_not_listening()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            _upnpService.ExternalIp.Returns("127.0.0.1");

            var result = await _subject.TestPortAsync(port);

            Assert.That(result.IsOpen, Is.False);
            Assert.That(result.Port, Is.EqualTo(port));
            Assert.That(result.ErrorMessage, Is.Not.Null);
        }

        [Test]
        public async Task TestPortAsync_should_handle_cancellation()
        {
            var canceledToken = new CancellationToken(true);

            _upnpService.ExternalIp.Returns("127.0.0.1");

            var result = await _subject.TestPortAsync(65530, canceledToken);

            Assert.That(result.IsOpen, Is.False);
        }

        [Test]
        public void GetStatus_when_vpn_interface_is_configured_and_active_should_reflect_bound_tunnel_ip()
        {
            _configService.BindInterface.Returns("tun0");
            _configService.EnableVpnKillSwitch.Returns(true);
            _vpnKillSwitchService.IsKillSwitchEnabled.Returns(true);
            _vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork).Returns(IPAddress.Parse("10.8.0.2"));
            _subject.LocalAddressesResolver = () => new List<string> { "192.168.1.50", "10.8.0.2" };

            var result = _subject.GetStatus();

            Assert.That(result.LocalIp, Is.EqualTo("10.8.0.2"));
            Assert.That(result.BoundIp, Is.EqualTo("10.8.0.2"));
            Assert.That(result.BoundInterface, Is.EqualTo("tun0"));
            Assert.That(result.PhysicalIp, Is.EqualTo("192.168.1.50"));
            Assert.That(result.IsVpnKillSwitchActive, Is.True);
        }

        [Test]
        public void GetStatus_when_no_specific_interface_is_bound_should_fallback_to_physical_ip_and_any()
        {
            _configService.BindInterface.Returns("Any");
            _configService.EnableVpnKillSwitch.Returns(false);
            _vpnKillSwitchService.IsKillSwitchEnabled.Returns(false);
            _subject.LocalAddressesResolver = () => new List<string> { "192.168.1.50" };

            var result = _subject.GetStatus();

            Assert.That(result.BoundInterface, Is.EqualTo("Any"));
            Assert.That(result.BoundIp, Is.EqualTo("192.168.1.50"));
            Assert.That(result.LocalIp, Is.EqualTo("192.168.1.50"));
            Assert.That(result.PhysicalIp, Is.EqualTo("192.168.1.50"));
            Assert.That(result.IsVpnKillSwitchActive, Is.False);
        }

        [Test]
        public void GetStatus_when_vpn_interface_is_configured_but_unplumbed_should_fallback_to_physical_ip_and_any()
        {
            _configService.BindInterface.Returns("tun0");
            _configService.EnableVpnKillSwitch.Returns(true);
            _vpnKillSwitchService.IsKillSwitchEnabled.Returns(true);
            _vpnKillSwitchService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns((IPAddress)null);
            _subject.LocalAddressesResolver = () => new List<string> { "192.168.1.50" };

            var result = _subject.GetStatus();

            Assert.That(result.BoundInterface, Is.EqualTo("Any"));
            Assert.That(result.BoundIp, Is.EqualTo("192.168.1.50"));
            Assert.That(result.LocalIp, Is.EqualTo("192.168.1.50"));
            Assert.That(result.PhysicalIp, Is.EqualTo("192.168.1.50"));
            Assert.That(result.IsVpnKillSwitchActive, Is.True);
        }
    }
}
