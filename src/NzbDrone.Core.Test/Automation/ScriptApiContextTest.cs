using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class ScriptApiContextTest
{
    private IConfigFileProvider _configFileProvider;
    private ScriptApiContext _subject;

    [SetUp]
    public void SetUp()
    {
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.Port.Returns(8096);
        _configFileProvider.SslPort.Returns(0);
        _configFileProvider.EnableSsl.Returns(false);
        _configFileProvider.BindAddress.Returns("*");
        _configFileProvider.UrlBase.Returns(string.Empty);

        _subject = new ScriptApiContext(_configFileProvider);
    }

    [Test]
    public void BuildApiUrl_should_use_default_http_and_port_8096_when_config_is_null()
    {
        var context = new ScriptApiContext(null);

        var url = context.BuildApiUrl("system/status");

        Assert.That(url, Is.EqualTo("http://127.0.0.1:8096/api/v1/system/status"));
    }

    [Test]
    public void BuildApiUrl_should_use_http_and_configured_port()
    {
        _configFileProvider.Port.Returns(9090);

        var url = _subject.BuildApiUrl("system/status");

        Assert.That(url, Is.EqualTo("http://127.0.0.1:9090/api/v1/system/status"));
    }

    [Test]
    public void BuildApiUrl_should_fallback_to_8096_when_port_is_zero()
    {
        _configFileProvider.Port.Returns(0);

        var url = _subject.BuildApiUrl("system/status");

        Assert.That(url, Is.EqualTo("http://127.0.0.1:8096/api/v1/system/status"));
    }

    [Test]
    public void BuildApiUrl_should_use_https_and_ssl_port_when_ssl_is_enabled()
    {
        _configFileProvider.EnableSsl.Returns(true);
        _configFileProvider.SslPort.Returns(9898);

        var url = _subject.BuildApiUrl("system/status");

        Assert.That(url, Is.EqualTo("https://127.0.0.1:9898/api/v1/system/status"));
    }

    [Test]
    public void BuildApiUrl_should_fallback_to_http_when_ssl_is_enabled_but_ssl_port_is_zero()
    {
        _configFileProvider.EnableSsl.Returns(true);
        _configFileProvider.SslPort.Returns(0);
        _configFileProvider.Port.Returns(8096);

        var url = _subject.BuildApiUrl("system/status");

        Assert.That(url, Is.EqualTo("http://127.0.0.1:8096/api/v1/system/status"));
    }

    [Test]
    public void BuildApiUrl_should_use_loopback_when_bind_address_is_wildcard()
    {
        _configFileProvider.BindAddress.Returns("*");
        Assert.That(_subject.BuildApiUrl("test"), Is.EqualTo("http://127.0.0.1:8096/api/v1/test"));

        _configFileProvider.BindAddress.Returns("0.0.0.0");
        Assert.That(_subject.BuildApiUrl("test"), Is.EqualTo("http://127.0.0.1:8096/api/v1/test"));

        _configFileProvider.BindAddress.Returns("::");
        Assert.That(_subject.BuildApiUrl("test"), Is.EqualTo("http://127.0.0.1:8096/api/v1/test"));
    }

    [Test]
    public void BuildApiUrl_should_use_specific_ipv4_bind_address()
    {
        _configFileProvider.BindAddress.Returns("192.168.1.100");

        var url = _subject.BuildApiUrl("system/status");

        Assert.That(url, Is.EqualTo("http://192.168.1.100:8096/api/v1/system/status"));
    }

    [Test]
    public void BuildApiUrl_should_wrap_ipv6_bind_address_in_brackets()
    {
        _configFileProvider.BindAddress.Returns("::1");

        var url = _subject.BuildApiUrl("system/status");

        Assert.That(url, Is.EqualTo("http://[::1]:8096/api/v1/system/status"));
    }

    [Test]
    public void BuildApiUrl_should_preserve_already_bracketed_ipv6_bind_address()
    {
        _configFileProvider.BindAddress.Returns("[::1]");

        var url = _subject.BuildApiUrl("system/status");

        Assert.That(url, Is.EqualTo("http://[::1]:8096/api/v1/system/status"));
    }

    [Test]
    public void BuildApiUrl_should_include_url_base()
    {
        _configFileProvider.UrlBase.Returns("/seedarr");

        var url = _subject.BuildApiUrl("system/status");

        Assert.That(url, Is.EqualTo("http://127.0.0.1:8096/seedarr/api/v1/system/status"));
    }

    [Test]
    public void BuildApiUrl_should_handle_url_base_without_leading_slash()
    {
        _configFileProvider.UrlBase.Returns("custombase");

        var url = _subject.BuildApiUrl("system/status");

        Assert.That(url, Is.EqualTo("http://127.0.0.1:8096/custombase/api/v1/system/status"));
    }

    [Test]
    public void BuildApiUrl_should_strip_redundant_leading_api_v1()
    {
        var url1 = _subject.BuildApiUrl("api/v1/torrents");
        var url2 = _subject.BuildApiUrl("/api/v1/torrents");

        Assert.That(url1, Is.EqualTo("http://127.0.0.1:8096/api/v1/torrents"));
        Assert.That(url2, Is.EqualTo("http://127.0.0.1:8096/api/v1/torrents"));
    }
}
