using System;
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

        [TestCase("socks5", ProxyType.Socks5)]
        [TestCase("Socks5", ProxyType.Socks5)]
        [TestCase("SOCKS5", ProxyType.Socks5)]
        [TestCase("sOcKs5", ProxyType.Socks5)]
        [TestCase("socks5h", ProxyType.Socks5h)]
        [TestCase("Socks5h", ProxyType.Socks5h)]
        [TestCase("SOCKS5H", ProxyType.Socks5h)]
        [TestCase("sOcKs5h", ProxyType.Socks5h)]
        [TestCase("http", ProxyType.Http)]
        [TestCase("Http", ProxyType.Http)]
        [TestCase("HTTP", ProxyType.Http)]
        [TestCase("hTtP", ProxyType.Http)]
        [TestCase("none", ProxyType.None)]
        [TestCase("None", ProxyType.None)]
        [TestCase("NONE", ProxyType.None)]
        [TestCase("nOnE", ProxyType.None)]
        [TestCase("socks4", ProxyType.None)]
        [TestCase("InvalidValue", ProxyType.None)]
        [TestCase("", ProxyType.None)]
        public void Type_should_parse_case_insensitively(string configValue, ProxyType expectedType)
        {
            _configService.GetValue("ProxyType", "None").Returns(configValue);

            Assert.That(_subject.Type, Is.EqualTo(expectedType));
        }

        [TestCase("socks5")]
        [TestCase("SOCKS5")]
        [TestCase("sOcKs5")]
        [TestCase("socks5h")]
        [TestCase("SOCKS5H")]
        [TestCase("sOcKs5h")]
        [TestCase("http")]
        [TestCase("HTTP")]
        [TestCase("hTtP")]
        public void IsEnabled_should_return_true_for_case_insensitive_proxy_type_when_host_set(string proxyType)
        {
            _configService.GetValue("ProxyType", "None").Returns(proxyType);
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");

            Assert.That(_subject.IsEnabled, Is.True);
        }

        [TestCase("none")]
        [TestCase("None")]
        [TestCase("NONE")]
        [TestCase("socks4")]
        public void IsEnabled_should_return_false_when_proxy_type_is_none_or_unsupported(string proxyType)
        {
            _configService.GetValue("ProxyType", "None").Returns(proxyType);
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");

            Assert.That(_subject.IsEnabled, Is.False);
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
            var creds = proxy.Credentials as NetworkCredential;
            Assert.That(creds, Is.Not.Null);
            Assert.That(creds.UserName, Is.EqualTo("user"));
            Assert.That(creds.Password, Is.EqualTo("pass"));
        }

        [Test]
        public void CreateHandler_should_return_socks5h_proxy_handler_when_socks5_enabled()
        {
            _configService.GetValue("ProxyType", "None").Returns("Socks5");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");
            _configService.GetValueInt("ProxyPort", 8080).Returns(1080);

            var handler = _subject.CreateHandler();

            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.Proxy, Is.Not.Null);
            Assert.That(handler.UseProxy, Is.True);
            var proxy = handler.Proxy as WebProxy;
            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy.Address, Is.EqualTo(new Uri("socks5h://proxy.example.com:1080")));
            Assert.That(proxy.Address.Scheme, Is.EqualTo("socks5h"));

            var iProxy = handler.Proxy as IWebProxy;
            Assert.That(iProxy, Is.Not.Null);
            var transportUri = iProxy.GetProxy(new Uri("http://tracker.example.com"));
            Assert.That(transportUri.Scheme, Is.EqualTo("socks5"));
        }

        [Test]
        public void CreateHandler_should_set_credentials_when_socks5_auth_enabled()
        {
            _configService.GetValue("ProxyType", "None").Returns("Socks5");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");
            _configService.GetValueInt("ProxyPort", 8080).Returns(1080);
            _configService.GetValue("ProxyUsername", "").Returns("socksuser");
            _configService.GetValue("ProxyPassword", "").Returns("sockspass");
            _configService.ProxyAuthEnabled.Returns(true);

            var handler = _subject.CreateHandler();

            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.Proxy, Is.Not.Null);
            var proxy = handler.Proxy as WebProxy;
            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy.Credentials, Is.Not.Null);
            var creds = proxy.Credentials as NetworkCredential;
            Assert.That(creds, Is.Not.Null);
            Assert.That(creds.UserName, Is.EqualTo("socksuser"));
            Assert.That(creds.Password, Is.EqualTo("sockspass"));
        }

        [Test]
        public void CreateHandler_should_not_set_credentials_when_auth_disabled()
        {
            _configService.GetValue("ProxyType", "None").Returns("Socks5");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");
            _configService.GetValueInt("ProxyPort", 8080).Returns(1080);
            _configService.GetValue("ProxyUsername", "").Returns("user");
            _configService.GetValue("ProxyPassword", "").Returns("pass");
            _configService.ProxyAuthEnabled.Returns(false);

            var handler = _subject.CreateHandler();

            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.Proxy, Is.Not.Null);
            var proxy = handler.Proxy as WebProxy;
            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy.Credentials, Is.Null);
        }

        [Test]
        public void CreateHandler_should_return_plain_handler_when_host_is_empty()
        {
            _configService.GetValue("ProxyType", "None").Returns("Socks5");
            _configService.GetValue("ProxyHost", "").Returns("");

            var handler = _subject.CreateHandler();

            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.Proxy, Is.Null);
        }

        [Test]
        public void CreateHandler_should_return_proxy_handler_when_lowercase_http_enabled()
        {
            _configService.GetValue("ProxyType", "None").Returns("http");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");
            _configService.GetValueInt("ProxyPort", 8080).Returns(3128);

            var handler = _subject.CreateHandler();

            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.Proxy, Is.Not.Null);
        }

        [Test]
        public void CreateHandler_should_return_socks5h_proxy_handler_when_lowercase_socks5_enabled()
        {
            _configService.GetValue("ProxyType", "None").Returns("socks5");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");
            _configService.GetValueInt("ProxyPort", 8080).Returns(1080);

            var handler = _subject.CreateHandler();

            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.Proxy, Is.Not.Null);
            Assert.That(handler.UseProxy, Is.True);
            var proxy = handler.Proxy as WebProxy;
            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy.Address, Is.EqualTo(new Uri("socks5h://proxy.example.com:1080")));
            Assert.That(proxy.Address.Scheme, Is.EqualTo("socks5h"));
        }

        [Test]
        public void CreateHandler_should_return_socks5h_proxy_handler_when_socks5h_enabled()
        {
            _configService.GetValue("ProxyType", "None").Returns("socks5h");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");
            _configService.GetValueInt("ProxyPort", 8080).Returns(1080);

            var handler = _subject.CreateHandler();

            Assert.That(handler, Is.Not.Null);
            Assert.That(handler.Proxy, Is.Not.Null);
            Assert.That(handler.UseProxy, Is.True);
            var proxy = handler.Proxy as WebProxy;
            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy.Address, Is.EqualTo(new Uri("socks5h://proxy.example.com:1080")));
            Assert.That(proxy.Address.Scheme, Is.EqualTo("socks5h"));
        }

        [Test]
        public void ProxyUri_should_return_socks5h_scheme_for_socks5()
        {
            _configService.GetValue("ProxyType", "None").Returns("Socks5");
            _configService.GetValue("ProxyHost", "").Returns("proxy.internal.net");
            _configService.GetValueInt("ProxyPort", 8080).Returns(1080);

            Assert.That(_subject.ProxyUri, Is.EqualTo("socks5h://proxy.internal.net:1080"));
        }

        [Test]
        public void ProxyUri_should_return_socks5h_scheme_for_socks5h()
        {
            _configService.GetValue("ProxyType", "None").Returns("Socks5h");
            _configService.GetValue("ProxyHost", "").Returns("remote.proxy.org");
            _configService.GetValueInt("ProxyPort", 8080).Returns(9050);

            Assert.That(_subject.ProxyUri, Is.EqualTo("socks5h://remote.proxy.org:9050"));
        }

        [Test]
        public void ProxyUri_should_return_http_scheme_for_http()
        {
            _configService.GetValue("ProxyType", "None").Returns("Http");
            _configService.GetValue("ProxyHost", "").Returns("squid.example.com");
            _configService.GetValueInt("ProxyPort", 8080).Returns(3128);

            Assert.That(_subject.ProxyUri, Is.EqualTo("http://squid.example.com:3128"));
        }

        [Test]
        public void ProxyUri_should_return_null_when_disabled()
        {
            _configService.GetValue("ProxyType", "None").Returns("None");
            _configService.GetValue("ProxyHost", "").Returns("proxy.example.com");

            Assert.That(_subject.ProxyUri, Is.Null);
        }
    }
}
