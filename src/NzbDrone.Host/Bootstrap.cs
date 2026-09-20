// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Common.Composition;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Security;

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
        "Seedarr.Api.V1",
    };

    public static WebApplication CreateApplication(StartupContext startupContext, string[] urls = null)
    {
        if (LogManager.Configuration == null)
        {
            NzbDroneLogger.Register(startupContext);
        }

        Logger.Info("Starting Seedarr - {0}", BuildInfo.Version);

        var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterInstance(startupContext);
        container.AutoAddServices(Assemblies);

        var builder = WebApplication.CreateBuilder();
        var configProvider = container.Resolve<IConfigFileProvider>();
        var certManager = container.Resolve<ICertificateManager>();

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            ConfigureKestrel(serverOptions, configProvider, certManager, urls);
        });

        builder.Host.UseServiceProviderFactory(
            new DryIocServiceProviderFactory(container));

        var startup = new Startup(container);
        startup.ConfigureServices(builder.Services);

        var app = builder.Build();
        startup.Configure(app);

        TableRegistration.RegisterTables();

        var mainDb = app.Services.GetRequiredService<IMainDatabase>();
        Logger.Info("Database initialized: {0}", mainDb.DatabaseType);

        try
        {
            var loggingReconfig = app.Services.GetService<ILoggingReconfigurationService>()
                                  ?? (ILoggingReconfigurationService)app.Services.GetService<LoggingReconfigurationService>();
            loggingReconfig?.Initialize();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to reconfigure logging during bootstrap");
        }

        if (urls != null)
        {
            foreach (var url in urls)
            {
                app.Urls.Add(url);
            }
        }
        else
        {
            var isPortCollision = HasPortCollision(configProvider);
            if (!isPortCollision)
            {
                var httpUrl = $"http://{configProvider.BindAddress}:{configProvider.Port}";
                Logger.Info("Listening on {0}", httpUrl);
            }

            if (configProvider.EnableSsl)
            {
                var httpsUrl = $"https://{configProvider.BindAddress}:{configProvider.SslPort}";
                Logger.Info("Listening with SSL on {0}", httpsUrl);
            }
        }

        return app;
    }

    public static void ConfigureKestrelLimits(KestrelServerOptions serverOptions)
    {
        serverOptions.Limits.MaxRequestBodySize = 500 * 1024 * 1024; // 500 MB
        serverOptions.Limits.MaxConcurrentConnections = 1000;
        serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
        serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    }

    public static IPAddress ResolveBindAddress(string bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return IPAddress.Any;
        }

        var cleanAddress = bindAddress.Trim().Trim('[', ']');

        return cleanAddress switch
        {
            "*" or "0.0.0.0" => IPAddress.Any,
            "localhost" or "127.0.0.1" => IPAddress.Loopback,
            "::1" => IPAddress.IPv6Loopback,
            _ when IPAddress.TryParse(cleanAddress, out var parsed) => parsed,
            _ => IPAddress.Any,
        };
    }

    public static bool HasPortCollision(bool enableSsl, int port, int sslPort)
    {
        return enableSsl && port == sslPort;
    }

    public static bool HasPortCollision(IConfigFileProvider configProvider)
    {
        return configProvider != null && HasPortCollision(configProvider.EnableSsl, configProvider.Port, configProvider.SslPort);
    }

    public static void ConfigureKestrel(
        KestrelServerOptions serverOptions,
        IConfigFileProvider configProvider,
        ICertificateManager certManager,
        string[] urls = null)
    {
        serverOptions.AddServerHeader = false;
        ConfigureKestrelLimits(serverOptions);

        if (urls != null)
        {
            return;
        }

        var isPortCollision = HasPortCollision(configProvider);
        if (isPortCollision)
        {
            Logger.Warn("HTTP port and SSL port are both configured to {0} with SSL enabled. Skipping unencrypted HTTP listener to prevent port collision.", configProvider.Port);
        }

        var bindAddress = configProvider?.BindAddress?.Trim() ?? "*";
        var cleanAddress = bindAddress.Trim().Trim('[', ']');

        if (cleanAddress is "*" or "0.0.0.0" or "")
        {
            if (!isPortCollision)
            {
                serverOptions.ListenAnyIP(configProvider.Port);
            }

            if (configProvider.EnableSsl)
            {
                try
                {
                    _ = certManager.GetOrCreateCertificate(configProvider);
                    serverOptions.ListenAnyIP(configProvider.SslPort, listenOptions =>
                    {
                        listenOptions.UseHttps(httpsOptions =>
                        {
                            httpsOptions.ServerCertificateSelector = (connectionContext, name) =>
                            {
                                var cert = certManager.GetOrCreateCertificate(configProvider);
                                var chain = certManager.GetCertificateChain();
                                if (chain != null && chain.Count > 0)
                                {
                                    httpsOptions.ServerCertificateChain = chain;
                                }

                                return cert;
                            };

                            var initialChain = certManager.GetCertificateChain();
                            if (initialChain != null && initialChain.Count > 0)
                            {
                                httpsOptions.ServerCertificateChain = initialChain;
                            }
                        });
                    });
                    Logger.Info("Configured SSL dual-stack listener on port {0}", configProvider.SslPort);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to initialize SSL listener on port {0}. HTTPS will not be active.", configProvider.SslPort);
                }
            }
        }
        else
        {
            var ip = ResolveBindAddress(bindAddress);

            if (!isPortCollision)
            {
                serverOptions.Listen(ip, configProvider.Port);
            }

            if (configProvider.EnableSsl)
            {
                try
                {
                    _ = certManager.GetOrCreateCertificate(configProvider);
                    serverOptions.Listen(ip, configProvider.SslPort, listenOptions =>
                    {
                        listenOptions.UseHttps(httpsOptions =>
                        {
                            httpsOptions.ServerCertificateSelector = (connectionContext, name) =>
                            {
                                var cert = certManager.GetOrCreateCertificate(configProvider);
                                var chain = certManager.GetCertificateChain();
                                if (chain != null && chain.Count > 0)
                                {
                                    httpsOptions.ServerCertificateChain = chain;
                                }

                                return cert;
                            };

                            var initialChain = certManager.GetCertificateChain();
                            if (initialChain != null && initialChain.Count > 0)
                            {
                                httpsOptions.ServerCertificateChain = initialChain;
                            }
                        });
                    });
                    Logger.Info("Configured SSL on {0}:{1}", ip, configProvider.SslPort);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to initialize SSL listener on port {0}. HTTPS will not be active.", configProvider.SslPort);
                }
            }
        }
    }

    public static void Start(StartupContext startupContext)
    {
        CreateApplication(startupContext).Run();
    }
}
