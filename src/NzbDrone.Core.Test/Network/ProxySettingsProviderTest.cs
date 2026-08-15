using System.Net;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Test.Network
{
    [TestFixture]
    public class ProxySettingsProviderTest
    {
        private IConfigService _configService;
        private ProxySettingsProvider _subject;

        [SetUp]
        public void SetUp()
        {
            _configService = Substitute.For<IConfigService>();
            _configService.GetValue("ProxyType", "None").Returns("None");
            _configService.GetValue("ProxyHost", "").Returns("");
            _configService.GetValueInt("ProxyPort", 8080).Returns(8080);
            _configService.GetValue("ProxyUsername", "").Returns("");
            _configService.GetValue("ProxyPassword", "").Returns("");
            _configService.ProxyAuthEnabled.Returns(false);

            _subject = new ProxySettingsProvider(_configService);
        }

        [Test]
        public void Type_should_return_none_when_config_is_none()
        {
            _configService.GetValue("ProxyType", "None").Returns("None");

            Assert.That(_subject.Type, Is.EqualTo(ProxyType.None));
        }

        [Test]
        public void Type_should_return_http_when_config_is_http()
        {
            _configService.GetValue("ProxyType", "None").Returns("Http");

            Assert.That(_subject.Type, Is.EqualTo(ProxyType.Http));
        }

        [Test]
        public void Type_should_return_socks5_when_config_is_socks5()
        {
            _configService.GetValue("ProxyType", "None").Returns("Socks5");

            Assert.That(_subject.Type, Is.EqualTo(ProxyType.Socks5));
        }

        [Test]
        public void Type_should_return_none_when_config_is_invalid()
        {
            _configService.GetValue("ProxyType", "None").Returns("InvalidValue");

            Assert.That(_subject.Type, Is.EqualTo(ProxyType.None));
        }

        [Test]
        public void Host_should_return_config_value()
        {
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");

            Assert.That(_subject.Host, Is.EqualTo("proxy.example.com"));
        }

        [Test]
        public void Port_should_return_config_value()
        {
            _configService.GetValueInt("ProxyPort", 8080).Returns(3128);

            Assert.That(_subject.Port, Is.EqualTo(3128));
        }

        [Test]
        public void IsEnabled_should_return_false_when_type_is_none()
        {
            _configService.GetValue("ProxyType", "None").Returns("None");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");

            Assert.That(_subject.IsEnabled, Is.False);
        }

        [Test]
        public void IsEnabled_should_return_false_when_host_is_empty()
        {
            _configService.GetValue("ProxyType", "None").Returns("Http");
            _configService.GetValue("ProxyHost", "").Returns("");

            Assert.That(_subject.IsEnabled, Is.False);
        }

        [Test]
        public void IsEnabled_should_return_true_when_type_is_http_and_host_set()
        {
            _configService.GetValue("ProxyType", "None").Returns("Http");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");

            Assert.That(_subject.IsEnabled, Is.True);
        }

        [Test]
        public void CreateHandler_should_return_plain_handler_when_not_enabled()
        {
            _configService.GetValue("ProxyType", "None").Returns("None");

            var handler = _subject.CreateHandler();

            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.Proxy, Is.Null);
        }

        [Test]
        public void CreateHandler_should_return_proxy_handler_when_http_enabled()
        {
            _configService.GetValue("ProxyType", "None").Returns("Http");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");
            _configService.GetValueInt("ProxyPort", 8080).Returns(3128);

            var handler = _subject.CreateHandler();

            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.Proxy, Is.Not.Null);
        }

        [Test]
        public void CreateHandler_should_set_credentials_when_auth_enabled()
        {
            _configService.GetValue("ProxyType", "None").Returns("Http");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");
            _configService.GetValueInt("ProxyPort", 8080).Returns(3128);
            _configService.GetValue("ProxyUsername", "").Returns("user");
            _configService.GetValue("ProxyPassword", "").Returns("pass");
            _configService.ProxyAuthEnabled.Returns(true);

            var handler = _subject.CreateHandler();

            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.Proxy, Is.Not.Null);
            var proxy = handler.Proxy as WebProxy;
            Assert.That(proxy.Credentials, Is.Not.Null);
        }
    }
}
