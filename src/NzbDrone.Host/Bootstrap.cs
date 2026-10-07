// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Common.Composition;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Security;
using Seedarr.Http.Authentication;

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
        ValidateListenUrlOverrides(urls);

        if (LogManager.Configuration == null)
        {
            NzbDroneLogger.Register(startupContext);
        }

        Logger.Info("Starting Seedarr - {0}", BuildInfo.Version);

        var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterInstance(startupContext);
        container.AutoAddServices(Assemblies);
        container.Register(
            typeof(IBasicRepository<>),
            typeof(BasicRepository<>),
            Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Keep);
        container.RegisterDelegate<IDatabase>(r => r.Resolve<IMainDatabase>(), Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        // BasicRepository<T> is an open generic, so assembly scanning skips it.
        // Closed repositories replace this fallback for their own model types.
        container.Register(
            typeof(IBasicRepository<>),
            typeof(BasicRepository<>),
            Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Keep);

        var builder = WebApplication.CreateBuilder();
        var configProvider = container.Resolve<IConfigFileProvider>();
        var certManager = container.Resolve<ICertificateManager>();
        var httpsListenerAvailability = new HttpsListenerAvailability();
        container.RegisterInstance(httpsListenerAvailability);

        if (configProvider.EnableSsl && urls == null)
        {
            httpsListenerAvailability.SetActive(
                HttpsListenerAvailability.TryPrepareCertificate(configProvider, certManager));
        }

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            ConfigureKestrel(serverOptions, configProvider, certManager, urls, httpsListenerAvailability);
        });

        builder.Host.UseServiceProviderFactory(
            new DryIocServiceProviderFactory(container));

        var startup = new Startup(container);
        startup.ConfigureServices(builder.Services);

        var outputWwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(outputWwwroot))
        {
            builder.Environment.WebRootPath = outputWwwroot;
        }

        var app = builder.Build();
        startup.Configure(app);

        InitializeDynamicAuthSchemesAsync(app.Services).GetAwaiter().GetResult();

        TableRegistration.RegisterTables();

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

        var mainDb = app.Services.GetRequiredService<IMainDatabase>();
        Logger.Info("Database initialized: {0}", mainDb.DatabaseType);

        if (urls == null)
        {
            var isPortCollision = HasPortCollision(configProvider);
            var shouldLogHttpListener = !isPortCollision
                || (configProvider.EnableSsl && !httpsListenerAvailability.IsActive);
            if (shouldLogHttpListener)
            {
                var httpUrl = $"http://{configProvider.BindAddress}:{configProvider.Port}";
                Logger.Info("Listening on {0}", httpUrl);
            }

            if (configProvider.EnableSsl)
            {
                var httpsUrl = $"https://{configProvider.BindAddress}:{configProvider.SslPort}";
                if (httpsListenerAvailability.IsActive)
                {
                    Logger.Info("Listening with SSL on {0}", httpsUrl);
                }
                else
                {
                    Logger.Warn(
                        "SSL is enabled in configuration but HTTPS is not active; HTTP will not redirect to {0}.",
                        httpsUrl);
                }
            }
        }

        return app;
    }

    public static async Task InitializeDynamicAuthSchemesAsync(IServiceProvider services)
    {
        if (services == null)
        {
            return;
        }

        var manager = services.GetService<IDynamicAuthSchemeManager>();
        if (manager == null)
        {
            return;
        }

        try
        {
            await manager.InitializeConfiguredProvidersAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to initialize dynamic authentication providers during bootstrap");
        }
    }

    public static void ValidateListenUrlOverrides(string[] urls)
    {
        if (urls == null)
        {
            return;
        }

        if (urls.Length == 0)
        {
            throw new ArgumentException("At least one URL must be provided when overriding listen addresses.", nameof(urls));
        }

        for (var i = 0; i < urls.Length; i++)
        {
            var url = urls[i];
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException($"URL at index {i} cannot be null or whitespace.", nameof(urls));
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"URL at index {i} is not a valid absolute HTTP or HTTPS URI: '{url}'.", nameof(urls));
            }
        }
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
            "*" or "+" or "0.0.0.0" => IPAddress.Any,
            "localhost" or "127.0.0.1" => IPAddress.Loopback,
            "::1" => IPAddress.IPv6Loopback,
            _ when cleanAddress.Contains('.') && !IsValidIpv4Literal(cleanAddress) => throw new ArgumentException(
                $"Invalid BindAddress '{bindAddress}'. Allowed values are '*', '+', '0.0.0.0', '::', 'localhost', or a valid IP address.",
                nameof(bindAddress)),
            _ when IPAddress.TryParse(cleanAddress, out var parsed) => parsed,
            _ => throw new ArgumentException(
                $"Invalid BindAddress '{bindAddress}'. Allowed values are '*', '+', '0.0.0.0', '::', 'localhost', or a valid IP address.",
                nameof(bindAddress)),
        };
    }

    private static bool IsValidIpv4Literal(string host)
    {
        var parts = host.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length == 0 || (part.Length > 1 && part[0] == '0'))
            {
                return false;
            }

            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var octet) || octet < 0 || octet > 255)
            {
                return false;
            }
        }

        return true;
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
        string[] urls = null,
        HttpsListenerAvailability httpsListenerAvailability = null)
    {
        serverOptions.AddServerHeader = false;
        ConfigureKestrelLimits(serverOptions);

        if (urls != null)
        {
            ConfigureKestrelUrlOverrides(serverOptions, configProvider, certManager, urls, httpsListenerAvailability);
            return;
        }

        var isPortCollision = HasPortCollision(configProvider);
        if (isPortCollision)
        {
            Logger.Warn("HTTP port and SSL port are both configured to {0} with SSL enabled. Skipping unencrypted HTTP listener to prevent port collision.", configProvider.Port);
        }

        var bindAddress = configProvider?.BindAddress?.Trim() ?? "*";
        var cleanAddress = bindAddress.Trim().Trim('[', ']');

        if (cleanAddress is "*" or "+" or "0.0.0.0" or "::" or "")
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
                            ConfigureHttpsCertificate(httpsOptions, certManager, configProvider));
                    });
                    Logger.Info("Configured SSL dual-stack listener on port {0}", configProvider.SslPort);
                    httpsListenerAvailability?.SetActive(true);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to initialize SSL listener on port {0}. HTTPS will not be active.", configProvider.SslPort);
                    httpsListenerAvailability?.SetActive(false);
                    if (isPortCollision)
                    {
                        Logger.Warn(
                            "HTTP and SSL share port {0}; falling back to unencrypted HTTP because HTTPS initialization failed.",
                            configProvider.Port);
                        serverOptions.ListenAnyIP(configProvider.Port);
                    }
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
                            ConfigureHttpsCertificate(httpsOptions, certManager, configProvider));
                    });
                    Logger.Info("Configured SSL on {0}:{1}", ip, configProvider.SslPort);
                    httpsListenerAvailability?.SetActive(true);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to initialize SSL listener on port {0}. HTTPS will not be active.", configProvider.SslPort);
                    httpsListenerAvailability?.SetActive(false);
                    if (isPortCollision)
                    {
                        Logger.Warn(
                            "HTTP and SSL share port {0}; falling back to unencrypted HTTP because HTTPS initialization failed.",
                            configProvider.Port);
                        serverOptions.Listen(ip, configProvider.Port);
                    }
                }
            }
        }
    }

    private static void ConfigureKestrelUrlOverrides(
        KestrelServerOptions serverOptions,
        IConfigFileProvider configProvider,
        ICertificateManager certManager,
        string[] urls,
        HttpsListenerAvailability httpsListenerAvailability = null)
    {
        foreach (var urlString in urls)
        {
            var uri = new Uri(urlString.Trim());
            var ip = ResolveBindAddress(uri.Host);
            var port = uri.Port;

            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                try
                {
                    _ = certManager.GetOrCreateCertificate(configProvider);
                    serverOptions.Listen(ip, port, listenOptions =>
                    {
                        listenOptions.UseHttps(httpsOptions =>
                            ConfigureHttpsCertificate(httpsOptions, certManager, configProvider));
                    });
                    Logger.Info("Configured SSL listener for URL override {0}", urlString);
                    httpsListenerAvailability?.SetActive(true);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to initialize SSL listener for URL override {0}", urlString);
                    httpsListenerAvailability?.SetActive(false);
                }
            }
            else
            {
                serverOptions.Listen(ip, port);
                Logger.Info("Configured HTTP listener for URL override {0}", urlString);
            }
        }
    }

    private static void ConfigureHttpsCertificate(
        HttpsConnectionAdapterOptions httpsOptions,
        ICertificateManager certManager,
        IConfigFileProvider configProvider)
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
    }

    public static void Start(StartupContext startupContext)
    {
        CreateApplication(startupContext).Run();
    }
}
