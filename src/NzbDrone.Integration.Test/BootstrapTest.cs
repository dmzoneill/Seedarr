using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;
using NzbDrone.Core.Seeding;
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
    [TestCase("+")]
    [TestCase("0.0.0.0")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void ResolveBindAddress_should_return_any_for_wildcards_and_empty(string input)
    {
        var result = Bootstrap.ResolveBindAddress(input);
        Assert.That(result, Is.EqualTo(IPAddress.Any));
    }

    [TestCase("invalid_ip_format")]
    [TestCase("vpn0")]
    [TestCase("192.168.1.00")]
    public void ResolveBindAddress_should_throw_on_invalid_address(string input)
    {
        var ex = Assert.Throws<ArgumentException>(() => Bootstrap.ResolveBindAddress(input));
        Assert.That(ex.ParamName, Is.EqualTo("bindAddress"));
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
    public void ConfigureKestrel_should_handle_plus_bind_address_without_throwing()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        var certManager = Substitute.For<ICertificateManager>();

        configProvider.BindAddress.Returns("+");
        configProvider.Port.Returns(8989);
        configProvider.SslPort.Returns(9899);
        configProvider.EnableSsl.Returns(false);

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

    [TestCase("::")]
    [TestCase("[::]")]
    [TestCase(" :: ")]
    public void ConfigureKestrel_should_use_dual_stack_listen_for_ipv6_any_bind_address(string bindAddress)
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        var certManager = Substitute.For<ICertificateManager>();

        configProvider.BindAddress.Returns(bindAddress);
        configProvider.Port.Returns(8989);
        configProvider.SslPort.Returns(9899);
        configProvider.EnableSsl.Returns(false);

        var serverOptions = new KestrelServerOptions();

        Assert.DoesNotThrow(() => Bootstrap.ConfigureKestrel(serverOptions, configProvider, certManager));
    }

    [Test]
    public void ConfigureKestrel_should_configure_http_listener_when_urls_provided()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        var certManager = Substitute.For<ICertificateManager>();

        var serverOptions = new KestrelServerOptions();

        Assert.DoesNotThrow(() => Bootstrap.ConfigureKestrel(serverOptions, configProvider, certManager, new[] { "http://localhost:5000" }));
        certManager.DidNotReceive().GetOrCreateCertificate(Arg.Any<IConfigFileProvider>());
    }

    [Test]
    public void ConfigureKestrel_should_wire_certificate_manager_for_https_url_overrides()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        var certManager = Substitute.For<ICertificateManager>();
        certManager.GetOrCreateCertificate(configProvider).Returns((X509Certificate2)null);
        certManager.GetCertificateChain().Returns(new X509Certificate2Collection());

        var serverOptions = new KestrelServerOptions();

        Assert.DoesNotThrow(() => Bootstrap.ConfigureKestrel(
            serverOptions,
            configProvider,
            certManager,
            new[] { "https://127.0.0.1:8443" }));

        certManager.Received().GetOrCreateCertificate(configProvider);
    }

    [Test]
    public void ValidateListenUrlOverrides_should_allow_null()
    {
        Assert.DoesNotThrow(() => Bootstrap.ValidateListenUrlOverrides(null));
    }

    [Test]
    public void ValidateListenUrlOverrides_should_accept_valid_http_urls()
    {
        Assert.DoesNotThrow(() => Bootstrap.ValidateListenUrlOverrides(new[] { "http://127.0.0.1:0", "http://localhost:5000" }));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void ValidateListenUrlOverrides_should_reject_null_or_blank_entries(string invalidUrl)
    {
        var ex = Assert.Throws<ArgumentException>(() => Bootstrap.ValidateListenUrlOverrides(new[] { invalidUrl }));
        Assert.That(ex.ParamName, Is.EqualTo("urls"));
    }

    [Test]
    public void ValidateListenUrlOverrides_should_reject_empty_array()
    {
        var ex = Assert.Throws<ArgumentException>(() => Bootstrap.ValidateListenUrlOverrides(Array.Empty<string>()));
        Assert.That(ex.ParamName, Is.EqualTo("urls"));
    }

    [TestCase("not-a-uri")]
    [TestCase("/relative/path")]
    [TestCase("ftp://localhost:21")]
    public void ValidateListenUrlOverrides_should_reject_invalid_urls(string invalidUrl)
    {
        var ex = Assert.Throws<ArgumentException>(() => Bootstrap.ValidateListenUrlOverrides(new[] { invalidUrl }));
        Assert.That(ex.ParamName, Is.EqualTo("urls"));
    }

    [Test]
    public void TryPrepareCertificate_should_return_false_when_certificate_load_fails()
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.EnableSsl.Returns(true);
        var certManager = Substitute.For<ICertificateManager>();
        certManager
            .GetOrCreateCertificate(config)
            .Returns(_ => throw new InvalidOperationException("cert unavailable"));

        Assert.That(HttpsListenerAvailability.TryPrepareCertificate(config, certManager), Is.False);
    }

    [Test]
    public void ConfigureKestrel_should_mark_https_inactive_when_certificate_load_fails()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.BindAddress.Returns("127.0.0.1");
        configProvider.Port.Returns(8687);
        configProvider.SslPort.Returns(8443);
        configProvider.EnableSsl.Returns(true);

        var certManager = Substitute.For<ICertificateManager>();
        certManager
            .GetOrCreateCertificate(configProvider)
            .Returns(_ => throw new InvalidOperationException("cert unavailable"));

        var availability = new HttpsListenerAvailability();
        availability.SetActive(HttpsListenerAvailability.TryPrepareCertificate(configProvider, certManager));
        var serverOptions = new KestrelServerOptions();

        Bootstrap.ConfigureKestrel(serverOptions, configProvider, certManager, null, availability);

        Assert.That(availability.IsActive, Is.False);
    }

    [Test]
    public void ConfigureKestrel_should_fallback_to_http_when_port_collision_and_certificate_load_fails()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.BindAddress.Returns("127.0.0.1");
        configProvider.Port.Returns(8687);
        configProvider.SslPort.Returns(8687);
        configProvider.EnableSsl.Returns(true);

        var certManager = Substitute.For<ICertificateManager>();
        certManager
            .GetOrCreateCertificate(configProvider)
            .Returns(_ => throw new InvalidOperationException("cert unavailable"));

        var availability = new HttpsListenerAvailability();
        availability.SetActive(HttpsListenerAvailability.TryPrepareCertificate(configProvider, certManager));
        var serverOptions = new KestrelServerOptions();

        Assert.DoesNotThrow(() => Bootstrap.ConfigureKestrel(serverOptions, configProvider, certManager, null, availability));
        Assert.That(availability.IsActive, Is.False);
    }

    [Test]
    public void ConfigureKestrel_should_fallback_to_http_on_any_ip_when_port_collision_and_certificate_load_fails()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.BindAddress.Returns("*");
        configProvider.Port.Returns(8687);
        configProvider.SslPort.Returns(8687);
        configProvider.EnableSsl.Returns(true);

        var certManager = Substitute.For<ICertificateManager>();
        certManager
            .GetOrCreateCertificate(configProvider)
            .Returns(_ => throw new InvalidOperationException("cert unavailable"));

        var availability = new HttpsListenerAvailability();
        var serverOptions = new KestrelServerOptions();

        Assert.DoesNotThrow(() => Bootstrap.ConfigureKestrel(serverOptions, configProvider, certManager, null, availability));
        Assert.That(availability.IsActive, Is.False);
    }

    [Test]
    public void RegisterBackgroundHostedServices_registers_all_known_background_services()
    {
        KnownTypes.Clear();

        var assemblies = new List<string>
        {
            "Seedarr.Host",
            "Seedarr.Core",
            "Seedarr.Common",
            "Seedarr.SignalR",
            "Seedarr.Http",
            "Seedarr.Api.V1",
        };
        var container = new global::DryIoc.Container(rules => rules.WithNzbDroneRules());
        container.AutoAddServices(assemblies);

        var expected = KnownTypes.GetImplementations(typeof(BackgroundService));
        Assert.That(expected, Does.Contain(typeof(SeedingEngine)),
            "SeedingEngine must be discovered so simulation ticks can run");
        Assert.That(expected.Count, Is.GreaterThan(1),
            "Multiple background workers are expected; a missing registration would freeze the UI");

        var services = new ServiceCollection();
        Startup.RegisterBackgroundHostedServices(services);

        var registeredImplementationTypes = services
            .Where(d => d.ImplementationType != null)
            .Select(d => d.ImplementationType)
            .ToHashSet();

        foreach (var backgroundServiceType in expected)
        {
            Assert.That(
                registeredImplementationTypes,
                Does.Contain(backgroundServiceType),
                $"{backgroundServiceType.Name} must be registered as a hosted service");
        }

        var hostedServiceDescriptors = services
            .Where(d => typeof(IHostedService).IsAssignableFrom(d.ServiceType))
            .ToList();
        Assert.That(hostedServiceDescriptors.Count, Is.GreaterThanOrEqualTo(expected.Count));
    }
}
