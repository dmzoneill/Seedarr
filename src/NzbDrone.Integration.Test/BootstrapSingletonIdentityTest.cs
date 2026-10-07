// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;
using NzbDrone.Host;
using Seedarr.Http.Authentication;
using Seedarr.Http.Security;
using Seedarr.Http.Terminal;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class BootstrapSingletonIdentityTest
{
    private static readonly List<string> Assemblies = new()
    {
        "Seedarr.Host",
        "Seedarr.Core",
        "Seedarr.Common",
        "Seedarr.SignalR",
        "Seedarr.Http",
        "Seedarr.Api.V1",
    };

    [Test]
    public void ConfigureServices_does_not_duplicate_AutoAddServices_singletons()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr-singleton-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var startupContext = new StartupContext("--data=" + tempDir);
            var container = new Container(rules => rules.WithNzbDroneRules());
            container.RegisterInstance(startupContext);
            container.AutoAddServices(Assemblies);

            var preBuildCertManager = container.Resolve<ICertificateManager>();
            var preBuildRevocation = container.Resolve<ISessionRevocationService>();
            var preBuildRateLimiter = container.Resolve<ILoginRateLimiter>();
            var preBuildRpcSessions = container.Resolve<IRpcSessionStore>();
            var preBuildAuthSchemes = container.Resolve<IDynamicAuthSchemeManager>();
            var preBuildPty = container.Resolve<IPtyTerminalService>();

            var builder = WebApplication.CreateBuilder();
            var configProvider = container.Resolve<IConfigFileProvider>();
            var certManager = container.Resolve<ICertificateManager>();
            var httpsListenerAvailability = new HttpsListenerAvailability();
            container.RegisterInstance(httpsListenerAvailability);

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                Bootstrap.ConfigureKestrel(
                    serverOptions,
                    configProvider,
                    certManager,
                    new[] { "http://127.0.0.1:0" },
                    httpsListenerAvailability);
            });

            builder.Host.UseServiceProviderFactory(new DryIocServiceProviderFactory(container));

            var startup = new Startup(container);
            startup.ConfigureServices(builder.Services);

            using var app = builder.Build();

            Assert.That(app.Services.GetRequiredService<ICertificateManager>(), Is.SameAs(preBuildCertManager));
            Assert.That(app.Services.GetRequiredService<ISessionRevocationService>(), Is.SameAs(preBuildRevocation));
            Assert.That(app.Services.GetRequiredService<ILoginRateLimiter>(), Is.SameAs(preBuildRateLimiter));
            Assert.That(app.Services.GetRequiredService<IRpcSessionStore>(), Is.SameAs(preBuildRpcSessions));
            Assert.That(app.Services.GetRequiredService<IDynamicAuthSchemeManager>(), Is.SameAs(preBuildAuthSchemes));
            Assert.That(app.Services.GetRequiredService<IPtyTerminalService>(), Is.SameAs(preBuildPty));
            Assert.That(certManager, Is.SameAs(preBuildCertManager));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
