using System.Collections.Generic;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.Composition;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Host;

public static class Bootstrap
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly List<string> Assemblies = new()
    {
        "Seedarr.Host",
        "Seedarr.Core",
        "Seedarr.Common",
        "Seedarr.SignalR",
        "Seedarr.Http",
        "Seedarr.Api.V1"
    };

    public static void Start(StartupContext startupContext)
    {
        Logger.Info("Starting Seedarr - {0}", BuildInfo.Version);

        var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterInstance(startupContext);
        container.AutoAddServices(Assemblies);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.AddServerHeader = false;
        });

        builder.Host.UseServiceProviderFactory(
            new DryIocServiceProviderFactory(container));

        var startup = new Startup(container);
        startup.ConfigureServices(builder.Services);

        var app = builder.Build();
        startup.Configure(app);

        var configProvider = app.Services.GetRequiredService<IConfigFileProvider>();
        var url = $"http://{configProvider.BindAddress}:{configProvider.Port}";

        Logger.Info("Listening on {0}", url);
        app.Urls.Add(url);

        app.Run();
    }
}
