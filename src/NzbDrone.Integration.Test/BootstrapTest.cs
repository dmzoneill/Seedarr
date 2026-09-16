using System;
using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;
using NzbDrone.Host;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class BootstrapTest
{
    [TestCase("[::1]", "::1")]
    [TestCase("::1", "::1")]
    [TestCase(" [::1] ", "::1")]
    [TestCase("[fe80::1]", "fe80::1")]
    [TestCase("fe80::1", "fe80::1")]
    [TestCase("[::]", "::")]
    [TestCase("::", "::")]
    [TestCase("127.0.0.1", "127.0.0.1")]
    [TestCase("[127.0.0.1]", "127.0.0.1")]
    [TestCase("localhost", "127.0.0.1")]
    [TestCase("192.168.1.100", "192.168.1.100")]
    public void ResolveBindAddress_should_parse_addresses_correctly(string input, string expectedIp)
    {
        var result = Bootstrap.ResolveBindAddress(input);
        Assert.That(result, Is.EqualTo(IPAddress.Parse(expectedIp)));
    }

    [TestCase("*")]
    [TestCase("0.0.0.0")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void ResolveBindAddress_should_return_any_for_wildcards_and_empty(string input)
    {
        var result = Bootstrap.ResolveBindAddress(input);
        Assert.That(result, Is.EqualTo(IPAddress.Any));
    }

    [Test]
    public void ResolveBindAddress_should_fallback_to_any_on_invalid_address()
    {
        var result = Bootstrap.ResolveBindAddress("invalid_ip_format");
        Assert.That(result, Is.EqualTo(IPAddress.Any));
    }

    [Test]
    public void HasPortCollision_should_return_true_when_ssl_enabled_and_ports_are_equal()
    {
        var result = Bootstrap.HasPortCollision(enableSsl: true, port: 8989, sslPort: 8989);
        Assert.That(result, Is.True);
    }

    [Test]
    public void HasPortCollision_should_return_false_when_ssl_enabled_and_ports_differ()
    {
        var result = Bootstrap.HasPortCollision(enableSsl: true, port: 8989, sslPort: 9899);
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasPortCollision_should_return_false_when_ssl_disabled_even_if_ports_match()
    {
        var result = Bootstrap.HasPortCollision(enableSsl: false, port: 8989, sslPort: 8989);
        Assert.That(result, Is.False);
    }

    [Test]
    public void HasPortCollision_with_configProvider_should_detect_collision()
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.EnableSsl.Returns(true);
        config.Port.Returns(8080);
        config.SslPort.Returns(8080);

        Assert.That(Bootstrap.HasPortCollision(config), Is.True);

        config.SslPort.Returns(8443);
        Assert.That(Bootstrap.HasPortCollision(config), Is.False);

        config.EnableSsl.Returns(false);
        config.SslPort.Returns(8080);
        Assert.That(Bootstrap.HasPortCollision(config), Is.False);
    }

    [Test]
    public void HasPortCollision_with_null_configProvider_should_return_false()
    {
        Assert.That(Bootstrap.HasPortCollision((IConfigFileProvider)null), Is.False);
    }

    [Test]
    public void ConfigureKestrelLimits_should_apply_resource_limits()
    {
        var serverOptions = new KestrelServerOptions();

        Bootstrap.ConfigureKestrelLimits(serverOptions);

        Assert.That(serverOptions.Limits.MaxRequestBodySize, Is.EqualTo(500L * 1024 * 1024));
        Assert.That(serverOptions.Limits.MaxConcurrentConnections, Is.EqualTo(1000L));
        Assert.That(serverOptions.Limits.KeepAliveTimeout, Is.EqualTo(TimeSpan.FromMinutes(2)));
        Assert.That(serverOptions.Limits.RequestHeadersTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void ConfigureKestrel_should_prevent_port_collision_without_throwing()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        var certManager = Substitute.For<ICertificateManager>();

        configProvider.BindAddress.Returns("[::1]");
        configProvider.Port.Returns(8989);
        configProvider.SslPort.Returns(8989);
        configProvider.EnableSsl.Returns(true);

        var serverOptions = new KestrelServerOptions();

        Assert.DoesNotThrow(() => Bootstrap.ConfigureKestrel(serverOptions, configProvider, certManager));
    }

    [Test]
    public void ConfigureKestrel_should_handle_any_bind_address_with_collision_without_throwing()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        var certManager = Substitute.For<ICertificateManager>();

        configProvider.BindAddress.Returns("*");
        configProvider.Port.Returns(8989);
        configProvider.SslPort.Returns(8989);
        configProvider.EnableSsl.Returns(true);

        var serverOptions = new KestrelServerOptions();

        Assert.DoesNotThrow(() => Bootstrap.ConfigureKestrel(serverOptions, configProvider, certManager));
    }

    [Test]
    public void ConfigureKestrel_should_skip_configuration_when_urls_provided()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        var certManager = Substitute.For<ICertificateManager>();

        var serverOptions = new KestrelServerOptions();

        Assert.DoesNotThrow(() => Bootstrap.ConfigureKestrel(serverOptions, configProvider, certManager, new[] { "http://localhost:5000" }));
    }
}
