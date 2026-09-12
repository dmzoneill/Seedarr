// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DryIoc;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;
using NzbDrone.SignalR;
using Seedarr.Http.Authentication;
using Seedarr.Http.Security;

namespace NzbDrone.Host;

public class Startup
{
    private readonly IContainer _container;

    public Startup(IContainer container)
    {
        _container = container;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var apiAssembly = Assembly.Load("Seedarr.Api.V1");
        var httpAssembly = Assembly.Load("Seedarr.Http");

        services.AddControllers()
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

        services.AddSignalR();
        services.AddDataProtection();
        services.AddHttpClient();
        services.AddSingleton<ICertificateManager, CertificateManager>();

        var configFileProvider = this._container.Resolve<IConfigFileProvider>();
        if (configFileProvider.EnableSsl && configFileProvider.RedirectHttpToHttps)
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
            options.ForwardDefaultSelector = context =>
            {
                var req = context.Request;
                var configFileProvider = context.RequestServices.GetService<NzbDrone.Core.Configuration.IConfigFileProvider>();

                // 0. When authentication is disabled, automatically grant local access
                if (configFileProvider != null && !configFileProvider.AuthenticationEnabled)
                {
                    return ApiKeyAuthenticationOptions.DefaultScheme;
                }

                // 1. API Key present in header, query parameter, or Bearer token
                var hasApiKeyHeader = (req.Headers.TryGetValue("X-Api-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey)) ||
                    (req.Headers.TryGetValue("ApiKey", out var headerKey2) && !string.IsNullOrWhiteSpace(headerKey2));
                var hasApiKeyQuery = (req.Query.TryGetValue("apikey", out var qKey) && !string.IsNullOrWhiteSpace(qKey)) ||
                    (req.Query.TryGetValue("access_token", out var qToken) && !string.IsNullOrWhiteSpace(qToken)) ||
                    (req.Query.TryGetValue("api_key", out var qApiKey) && !string.IsNullOrWhiteSpace(qApiKey));
                var hasBearerToken = req.Headers.TryGetValue("Authorization", out var authHeader) &&
                    authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(authHeader.ToString()["Bearer ".Length..].Trim());

                if (hasApiKeyHeader || hasApiKeyQuery || hasBearerToken)
                {
                    return ApiKeyAuthenticationOptions.DefaultScheme;
                }

                // 2. HTTP Basic Auth header
                if (req.Headers.TryGetValue("Authorization", out var basicHeader) &&
                    basicHeader.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(basicHeader.ToString()["Basic ".Length..].Trim()))
                {
                    return ApiKeyAuthenticationOptions.DefaultScheme;
                }

                // 3. Forward-Auth reverse proxy headers
                if ((req.Headers.TryGetValue("Remote-User", out var rUser) && !string.IsNullOrWhiteSpace(rUser)) ||
                    (req.Headers.TryGetValue("X-authentik-username", out var aUser) && !string.IsNullOrWhiteSpace(aUser)) ||
                    (req.Headers.TryGetValue("X-Forwarded-User", out var fUser) && !string.IsNullOrWhiteSpace(fUser)))
                {
                    return ApiKeyAuthenticationOptions.DefaultScheme;
                }

                // 4. Cookie for interactive browser session or login flow
                if (req.Cookies.ContainsKey("Seedarr_Auth") ||
                    req.Path.StartsWithSegments("/login") ||
                    req.Path.StartsWithSegments("/auth"))
                {
                    return "Cookies";
                }

                // 5. Default to ApiKey handler
                return ApiKeyAuthenticationOptions.DefaultScheme;
            };
        })
        .AddCookie("Cookies", options =>
        {
            options.Cookie.Name = "Seedarr_Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login?accessDenied=true";
        })
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationOptions.DefaultScheme, _ => { });

        services.AddOptions<OpenIdConnectOptions>();
        services.AddSingleton<IDynamicAuthSchemeManager, DynamicAuthSchemeManager>();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
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
                builder.SetIsOriginAllowed(_ => true)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        services.AddHostedService<AppLifetime>();
    }

    public void Configure(WebApplication app)
    {
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        });

        var configFileProvider = app.Services.GetRequiredService<IConfigFileProvider>();

        app.UseCors();

        app.UseMiddleware<HostHeaderValidationMiddleware>();
        app.UseMiddleware<CsrfProtectionMiddleware>();

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
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
            },
        });

        var wwwroot = Path.Combine(System.AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot),
                OnPrepareResponse = ctx =>
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
                },
            });
        }

        var fixturesPath = Path.Combine(System.AppContext.BaseDirectory, "fixtures");
        Directory.CreateDirectory(fixturesPath);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(fixturesPath),
            RequestPath = "/fixtures",
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
        });

        if (configFileProvider.EnableSsl && configFileProvider.RedirectHttpToHttps)
        {
            app.UseHttpsRedirection();
        }

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Seedarr REST API v1");
            c.RoutePrefix = "swagger";
            c.InjectStylesheet("/swagger-custom.css");
        });

        app.MapControllers();
        app.MapHub<MessageHub>("/signalr/messages");
        app.MapGet("/swagger-custom.css", () => Microsoft.AspNetCore.Http.Results.Content(SwaggerTheme.Css, "text/css")).AllowAnonymous();

        app.MapFallbackToFile("index.html");
    }
}
