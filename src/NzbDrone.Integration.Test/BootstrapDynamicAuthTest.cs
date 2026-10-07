using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Host;
using Seedarr.Http.Authentication;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class BootstrapDynamicAuthTest
{
    [Test]
    public async Task InitializeDynamicAuthSchemesAsync_should_invoke_manager_when_registered()
    {
        var manager = Substitute.For<IDynamicAuthSchemeManager>();
        manager.InitializeConfiguredProvidersAsync().Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var provider = services.BuildServiceProvider();

        await Bootstrap.InitializeDynamicAuthSchemesAsync(provider);

        await manager.Received(1).InitializeConfiguredProvidersAsync();
    }

    [Test]
    public async Task InitializeDynamicAuthSchemesAsync_should_noop_when_manager_missing()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        Assert.DoesNotThrowAsync(async () => await Bootstrap.InitializeDynamicAuthSchemesAsync(provider));
    }

    [Test]
    public async Task InitializeDynamicAuthSchemesAsync_should_noop_when_services_null()
    {
        Assert.DoesNotThrowAsync(async () => await Bootstrap.InitializeDynamicAuthSchemesAsync(null));
    }

    [Test]
    public async Task InitializeDynamicAuthSchemesAsync_should_not_throw_when_manager_fails()
    {
        var manager = Substitute.For<IDynamicAuthSchemeManager>();
        manager.InitializeConfiguredProvidersAsync().Returns<Task>(_ => throw new InvalidOperationException("IdP offline"));

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var provider = services.BuildServiceProvider();

        Assert.DoesNotThrowAsync(async () => await Bootstrap.InitializeDynamicAuthSchemesAsync(provider));
    }
}
