using DryIoc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Common.Serializer;
using NzbDrone.SignalR;
using Seedarr.Http.Authentication;

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
        services.AddControllers()
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

        services.AddAuthentication(ApiKeyAuthenticationOptions.DefaultScheme)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationOptions.DefaultScheme, _ => { });

        services.AddAuthorization();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Seedarr API",
                Version = "v1"
            });
        });

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });
    }

    public void Configure(WebApplication app)
    {
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        app.UseCors();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseSwaggerUI();

        app.MapControllers();
        app.MapHub<MessageHub>("/signalr/messages");
    }
}
