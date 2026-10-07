// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DryIoc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;
using NzbDrone.SignalR;
using Seedarr.Http.Authentication;
using Seedarr.Http.Security;
using Seedarr.Http.Terminal;

namespace NzbDrone.Host;

public class Startup
{
    public const string SpaFallbackExcludePathRegex = "^(?!(api|signalr|swagger|fixtures|transmission|ws)).*$";

    private readonly IContainer _container;

    public Startup(IContainer container)
    {
        _container = container;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(2));

        services.AddProblemDetails();

        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
        });

        var apiAssembly = Assembly.Load("Seedarr.Api.V1");
        var httpAssembly = Assembly.Load("Seedarr.Http");

        services.AddControllers(options =>
            {
                options.InputFormatters.Insert(0, new Seedarr.Api.V1.Transmission.TransmissionRpcInputFormatter());
            })
            .AddApplicationPart(apiAssembly)
            .AddApplicationPart(httpAssembly)
            .AddJsonOptions(options =>
            {
                var settings = STJson.GetSerializerSettings();
                options.JsonSerializerOptions.PropertyNamingPolicy = settings.PropertyNamingPolicy;
                options.JsonSerializerOptions.DefaultIgnoreCondition = settings.DefaultIgnoreCondition;
                foreach (var converter in settings.Converters)
                {
                    options.JsonSerializerOptions.Converters.Add(converter);
                }
            });

        services.AddSignalR(options =>
        {
            options.StreamBufferCapacity = 10;
            options.MaximumParallelInvocationsPerClient = 5;
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        });

        var appFolderInfo = this._container?.Resolve<IAppFolderInfo>();
        var keysFolder = Path.Combine(appFolderInfo?.AppDataFolder ?? AppContext.BaseDirectory, "DataProtection-Keys");
        Directory.CreateDirectory(keysFolder);

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
            .SetApplicationName("Seedarr");

        services.AddHttpClient();
        services.AddSingleton<ICertificateManager, CertificateManager>();
        services.AddSingleton<IRpcSessionStore, RpcSessionStore>();
        services.AddSingleton<ISessionRevocationService, SessionRevocationService>();
        services.AddSingleton<ILoginRateLimiter, LoginRateLimiter>();
        services.AddSingleton<Seedarr.Http.Terminal.IPtyTerminalService, Seedarr.Http.Terminal.PtyTerminalService>();

        var configFileProvider = this._container.Resolve<IConfigFileProvider>();
        var httpsListenerAvailability = this._container.IsRegistered<HttpsListenerAvailability>()
            ? this._container.Resolve<HttpsListenerAvailability>()
            : null;
        if (configFileProvider.EnableSsl
            && configFileProvider.RedirectHttpToHttps
            && (httpsListenerAvailability == null || httpsListenerAvailability.IsActive))
        {
            services.AddHttpsRedirection(options =>
            {
                options.HttpsPort = configFileProvider.SslPort;
            });
        }

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "SmartAuth";
            options.DefaultChallengeScheme = "SmartAuth";
        })
        .AddPolicyScheme("SmartAuth", "Smart Authentication Router", options =>
        {
            options.ForwardDefaultSelector = context => SmartAuthForwardDefaultSelector.SelectScheme(context);
        })
        .AddCookie("Cookies", options =>
        {
            options.Cookie.Name = "Seedarr_Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.Cookie.SecurePolicy = configFileProvider.EnableSsl
                ? Microsoft.AspNetCore.Http.CookieSecurePolicy.Always
                : Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            var urlBase = configFileProvider?.UrlBase?.Trim();
            options.Cookie.Path = string.IsNullOrWhiteSpace(urlBase) ? "/" : (urlBase.StartsWith('/') ? urlBase : "/" + urlBase);
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login?accessDenied=true";
            options.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = async context =>
                {
                    var revocationService = context.HttpContext.RequestServices.GetService<ISessionRevocationService>();
                    if (revocationService == null)
                    {
                        return;
                    }

                    if (!context.Properties.IssuedUtc.HasValue)
                    {
                        context.Properties.IssuedUtc = DateTimeOffset.UtcNow;
                    }

                    var issuedUtc = context.Properties.IssuedUtc.Value.UtcDateTime;
                    var sessionId = context.Principal?.FindFirst("SessionId")?.Value;
                    var username = context.Principal?.Identity?.Name;

                    var isRevoked = (!string.IsNullOrWhiteSpace(sessionId) && revocationService.IsSessionRevoked(sessionId, issuedUtc)) ||
                                    (!string.IsNullOrWhiteSpace(username) && revocationService.IsSessionRevoked(username, issuedUtc));

                    if (isRevoked)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync("Cookies");
                    }
                },
            };
        })
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationOptions.DefaultScheme, _ => { })
        .AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>(
            BasicAuthenticationOptions.DefaultScheme, _ => { })
        .AddScheme<ForwardAuthOptions, ForwardAuthHandler>(
            ForwardAuthOptions.DefaultScheme, _ => { });

        services.AddOptions<OpenIdConnectOptions>();
        services.AddSingleton<IDynamicAuthSchemeManager, DynamicAuthSchemeManager>();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(Policies.AdminOnly, policy =>
                policy.RequireRole(Roles.Admin));

            options.AddPolicy(Policies.Operator, policy =>
                policy.RequireRole(Roles.Admin, Roles.User));

            options.AddPolicy(Policies.Reader, policy =>
                policy.RequireRole(Roles.Admin, Roles.User, Roles.ReadOnly));
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
            c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Seedarr REST API v1",
                Version = "v1",
                Description = "BitTorrent Seeding Simulator API",
            });

            c.AddSecurityDefinition("ApiKeyHeader", new OpenApiSecurityScheme
            {
                Name = "X-Api-Key",
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "API Key authentication via X-Api-Key header",
            });

            c.AddSecurityDefinition("ApiKeyQuery", new OpenApiSecurityScheme
            {
                Name = "apikey",
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Query,
                Description = "API Key authentication via apikey query parameter",
            });

            c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("ApiKeyHeader", doc),
                    new List<string>()
                },
                {
                    new OpenApiSecuritySchemeReference("ApiKeyQuery", doc),
                    new List<string>()
                },
            });

            var apiAssembly = Assembly.Load("Seedarr.Api.V1");
            var xmlFile = $"{apiAssembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath);
            }

            var hostXmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var hostXmlPath = Path.Combine(AppContext.BaseDirectory, hostXmlFile);
            if (File.Exists(hostXmlPath))
            {
                c.IncludeXmlComments(hostXmlPath);
            }
        });

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder.SetIsOriginAllowed(origin => CorsSecurityHelper.IsOriginAllowed(origin, configFileProvider))
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        services.AddHostedService<AppLifetime>();
    }

    public static bool IsOriginAllowed(string origin, IConfigFileProvider configFileProvider)
    {
        return CorsSecurityHelper.IsOriginAllowed(origin, configFileProvider);
    }

    public static bool IsOriginAllowed(string origin, string allowedOrigins)
    {
        return CorsSecurityHelper.IsOriginAllowed(origin, allowedOrigins);
    }

    internal static void PrepareWwwrootStaticFileResponse(StaticFileResponseContext ctx)
    {
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";
        }
        else
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }

    internal static async Task<bool> TryEnsureAuthenticatedWhenRequiredAsync(HttpContext context, IConfigFileProvider config)
    {
        if (!config.AuthenticationEnabled)
        {
            return true;
        }

        var isAuth = context.User?.Identity?.IsAuthenticated == true;
        if (!isAuth)
        {
            var authResult = await context.AuthenticateAsync("Cookies");
            if (authResult.Succeeded)
            {
                isAuth = true;
                context.User = authResult.Principal;
            }
            else
            {
                var apiKeyResult = await context.AuthenticateAsync(ApiKeyAuthenticationOptions.DefaultScheme);
                if (apiKeyResult.Succeeded)
                {
                    isAuth = true;
                    context.User = apiKeyResult.Principal;
                }
            }
        }

        return isAuth;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "ASP.NET Core convention expects instance Configure method")]
    public void Configure(WebApplication app)
    {
        app.UseExceptionHandler();

        var configFileProvider = app.Services.GetRequiredService<IConfigFileProvider>();

        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            ForwardLimit = null,
        };
        ForwardedHeadersSecurityHelper.ApplyTrustedProxyConfiguration(forwardedHeadersOptions, configFileProvider.TrustedProxies);
        app.UseForwardedHeaders(forwardedHeadersOptions);

        app.UseResponseCompression();

        app.UseCors();

        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<HostHeaderValidationMiddleware>();
        app.UseMiddleware<CsrfProtectionMiddleware>();

        var urlBase = configFileProvider.UrlBase?.Trim().Trim('/');
        var pathBase = !string.IsNullOrEmpty(urlBase) ? "/" + urlBase : string.Empty;
        if (!string.IsNullOrEmpty(pathBase))
        {
            app.UsePathBase(pathBase);
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/" && !context.Request.PathBase.HasValue)
                {
                    context.Response.Redirect(pathBase + "/");
                    return;
                }

                await next();
            });
        }

        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            var wwwrootProvider = new PhysicalFileProvider(wwwroot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = wwwrootProvider });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = wwwrootProvider,
                OnPrepareResponse = PrepareWwwrootStaticFileResponse,
            });
        }
        else
        {
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = PrepareWwwrootStaticFileResponse,
            });
        }

        var fixturesPath = Path.Combine(System.AppContext.BaseDirectory, "fixtures");
        Directory.CreateDirectory(fixturesPath);
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/fixtures"))
            {
                var config = context.RequestServices.GetRequiredService<IConfigFileProvider>();
                if (!await TryEnsureAuthenticatedWhenRequiredAsync(context, config))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Authentication required to access fixtures.");
                    return;
                }
            }

            await next();
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(fixturesPath),
            RequestPath = "/fixtures",
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
        });

        var httpsListenerAvailability = this._container.IsRegistered<HttpsListenerAvailability>()
            ? this._container.Resolve<HttpsListenerAvailability>()
            : null;
        if (configFileProvider.EnableSsl
            && configFileProvider.RedirectHttpToHttps
            && (httpsListenerAvailability == null || httpsListenerAvailability.IsActive))
        {
            app.UseHttpsRedirection();
        }

        app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/swagger"), swaggerApp =>
        {
            swaggerApp.Use(async (context, next) =>
            {
                context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'";

                var config = context.RequestServices.GetRequiredService<IConfigFileProvider>();
                if (!await TryEnsureAuthenticatedWhenRequiredAsync(context, config))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Authentication required to access API documentation.");
                    return;
                }

                await next();
            });

            swaggerApp.UseSwagger();
            swaggerApp.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint($"{pathBase}/swagger/v1/swagger.json", "Seedarr REST API v1");
                c.RoutePrefix = "swagger";
                c.InjectStylesheet($"{pathBase}/swagger-custom.css");
                c.ConfigObject.PersistAuthorization = true;
            });
        });

        app.Use(async (context, next) =>
        {
            if (string.Equals(context.Request.Path.Value, "/transmission/rpc/", StringComparison.OrdinalIgnoreCase))
            {
                context.Request.Path = "/transmission/rpc";
            }
            await next();
        });

        app.UseRouting();

        app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<MessageHub>("/signalr/messages");
        app.MapHub<TerminalHub>("/signalr/terminal");

        app.Use(async (context, next) =>
        {
            if ((context.Request.Path == "/ws/terminal" || context.Request.Path == "/api/v1/terminal/ws") &&
                context.WebSockets.IsWebSocketRequest)
            {
                var configFileProvider = context.RequestServices.GetRequiredService<IConfigFileProvider>();
                if (configFileProvider.AuthenticationEnabled)
                {
                    var user = context.User;
                    if (user?.Identity?.IsAuthenticated == true)
                    {
                        if (!user.IsInRole("Admin") && !user.HasClaim(global::System.Security.Claims.ClaimTypes.Role, "Admin"))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsync("Admin role required for terminal access.");
                            await context.Response.CompleteAsync();
                            return;
                        }
                    }
                    else if (!Seedarr.Http.Security.RpcAuthenticationHelper.IsAuthenticated(context, configFileProvider))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync("Authentication required for terminal access.");
                        await context.Response.CompleteAsync();
                        return;
                    }
                }

                var ptyService = context.RequestServices.GetRequiredService<Seedarr.Http.Terminal.IPtyTerminalService>();
                var configService = context.RequestServices.GetRequiredService<IConfigService>();
                await Seedarr.Http.Terminal.TerminalWebSocketHandler.HandleWebSocket(context, ptyService, configService, configFileProvider);
                return;
            }

            await next();
        });

        var terminalHandler = async (HttpContext context) =>
        {
            var configProvider = context.RequestServices.GetRequiredService<IConfigFileProvider>();
            if (configProvider.AuthenticationEnabled)
            {
                var isAuthenticated = (context.User?.Identity?.IsAuthenticated == true) ||
                    Seedarr.Http.Security.RpcAuthenticationHelper.IsAuthenticated(context, configProvider);

                if (!isAuthenticated)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Authentication required for terminal access.");
                    await context.Response.CompleteAsync();
                    return;
                }
            }

            var ptyService = context.RequestServices.GetRequiredService<Seedarr.Http.Terminal.IPtyTerminalService>();
            var configService = context.RequestServices.GetRequiredService<IConfigService>();
            await Seedarr.Http.Terminal.TerminalWebSocketHandler.HandleWebSocket(context, ptyService, configService, configProvider);
        };

        app.Map("/ws/terminal", terminalHandler);
        app.Map("/api/v1/terminal/ws", terminalHandler);

        app.MapGet("/swagger-custom.css", () => Microsoft.AspNetCore.Http.Results.Content(SwaggerTheme.Css, "text/css")).AllowAnonymous();

        app.MapFallbackToFile($"{{*path:nonfile:regex({SpaFallbackExcludePathRegex})}}", "index.html");
    }
}
