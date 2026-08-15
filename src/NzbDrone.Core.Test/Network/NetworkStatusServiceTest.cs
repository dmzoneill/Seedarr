using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Test.Network
{
    [TestFixture]
    public class NetworkStatusServiceTest
    {
        private IUpnpService _upnpService;
        private IExternalIpService _externalIpService;
        private IProxySettingsProvider _proxySettings;
        private NetworkStatusService _subject;

        [SetUp]
        public void SetUp()
        {
            _upnpService = Substitute.For<IUpnpService>();
            _externalIpService = Substitute.For<IExternalIpService>();
            _proxySettings = Substitute.For<IProxySettingsProvider>();

            _upnpService.GetMappings().Returns(new List<PortMapping>());
            _upnpService.IsAvailable.Returns(false);
            _upnpService.ExternalIp.Returns(string.Empty);
            _externalIpService.CachedIp.Returns(string.Empty);
            _proxySettings.IsEnabled.Returns(false);

            _subject = new NetworkStatusService(_upnpService, _externalIpService, _proxySettings);
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
    }
}
