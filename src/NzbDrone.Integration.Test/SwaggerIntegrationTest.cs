using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class SwaggerIntegrationTest : IntegrationTestBase
{
    [Test]
    public async Task SwaggerJson_returns_200_and_valid_openapi_v3_schema()
    {
        var response = await GetAsync("/swagger/v1/swagger.json");
        var jsonBody = await response.Content.ReadAsStringAsync();
        TestContext.WriteLine($"Swagger response: {response.StatusCode} -> {jsonBody}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), jsonBody);
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        Assert.That(response.Headers.Contains("X-Frame-Options"), Is.True);
        Assert.That(response.Headers.GetValues("X-Frame-Options").First(), Is.EqualTo("SAMEORIGIN"));
        Assert.That(response.Headers.Contains("Content-Security-Policy"), Is.True);
        Assert.That(response.Headers.GetValues("Content-Security-Policy").First(), Does.Contain("frame-ancestors 'self'"));

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        Assert.That(root.TryGetProperty("openapi", out var openapiProp), Is.True, "Missing 'openapi' field");
        Assert.That(openapiProp.GetString(), Does.StartWith("3."));

        Assert.That(root.TryGetProperty("info", out var infoProp), Is.True, "Missing 'info' field");
        Assert.That(infoProp.TryGetProperty("title", out var titleProp), Is.True, "Missing 'info.title' field");
        Assert.That(titleProp.GetString(), Does.Contain("Seedarr"));

        Assert.That(root.TryGetProperty("paths", out var pathsProp), Is.True, "Missing 'paths' field");
        Assert.That(pathsProp.EnumerateObject().Any(), Is.True, "Paths should contain registered API endpoints");

        // Verify key endpoints are documented
        Assert.That(pathsProp.TryGetProperty("/api/v1/system/status", out _), Is.True, "Status endpoint missing from OpenAPI paths");
        Assert.That(pathsProp.TryGetProperty("/api/v1/torrent", out _), Is.True, "Torrent endpoint missing from OpenAPI paths");

        // Verify security definitions exist
        Assert.That(root.TryGetProperty("components", out var componentsProp), Is.True, "Missing 'components' field");
        Assert.That(componentsProp.TryGetProperty("securitySchemes", out var secSchemesProp), Is.True, "Missing 'securitySchemes' field");
        Assert.That(secSchemesProp.TryGetProperty("ApiKeyHeader", out var headerScheme), Is.True, "Missing ApiKeyHeader security scheme");
        Assert.That(headerScheme.GetProperty("name").GetString(), Is.EqualTo("X-Api-Key"));
        Assert.That(secSchemesProp.TryGetProperty("ApiKeyQuery", out var queryScheme), Is.True, "Missing ApiKeyQuery security scheme");
        Assert.That(queryScheme.GetProperty("name").GetString(), Is.EqualTo("apikey"));
    }

    [Test]
    public async Task SwaggerUI_returns_200_and_contains_custom_stylesheet()
    {
        var response = await GetAsync("/swagger/index.html");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.Contains("X-Frame-Options"), Is.True);
        Assert.That(response.Headers.GetValues("X-Frame-Options").First(), Is.EqualTo("SAMEORIGIN"));
        Assert.That(response.Headers.Contains("Content-Security-Policy"), Is.True);
        Assert.That(response.Headers.GetValues("Content-Security-Policy").First(), Does.Contain("frame-ancestors 'self'"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.That(html, Does.Contain("swagger-ui"));
        Assert.That(html, Does.Contain("swagger-custom.css"));
    }

    [Test]
    public async Task Swagger_endpoints_require_authentication_when_enabled()
    {
        var configProvider = GlobalSetup.Factory.Services.GetRequiredService<IConfigFileProvider>();
        using var unauthenticatedClient = GlobalSetup.Factory.CreateClient();

        try
        {
            configProvider.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "AuthenticationEnabled", true }
            });

            // 1. Unauthenticated request to /swagger/v1/swagger.json returns 401
            var unauthJsonResponse = await unauthenticatedClient.GetAsync("/swagger/v1/swagger.json");
            Assert.That(unauthJsonResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(unauthJsonResponse.Headers.Contains("X-Frame-Options"), Is.True);
            Assert.That(unauthJsonResponse.Headers.GetValues("X-Frame-Options").First(), Is.EqualTo("SAMEORIGIN"));
            Assert.That(unauthJsonResponse.Headers.Contains("Content-Security-Policy"), Is.True);
            Assert.That(unauthJsonResponse.Headers.GetValues("Content-Security-Policy").First(), Does.Contain("frame-ancestors 'self'"));

            // 2. Unauthenticated request to /swagger/index.html returns 401
            var unauthUiResponse = await unauthenticatedClient.GetAsync("/swagger/index.html");
            Assert.That(unauthUiResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(unauthUiResponse.Headers.Contains("X-Frame-Options"), Is.True);
            Assert.That(unauthUiResponse.Headers.GetValues("X-Frame-Options").First(), Is.EqualTo("SAMEORIGIN"));
            Assert.That(unauthUiResponse.Headers.Contains("Content-Security-Policy"), Is.True);
            Assert.That(unauthUiResponse.Headers.GetValues("Content-Security-Policy").First(), Does.Contain("frame-ancestors 'self'"));

            // 3. Authenticated request with X-Api-Key header returns 200
            var authJsonResponse = await Client.GetAsync("/swagger/v1/swagger.json");
            Assert.That(authJsonResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var authUiResponse = await Client.GetAsync("/swagger/index.html");
            Assert.That(authUiResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            // 4. Authenticated request with query parameter returns 200
            var queryJsonResponse = await unauthenticatedClient.GetAsync($"/swagger/v1/swagger.json?apikey={ApiKey}");
            Assert.That(queryJsonResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var queryUiResponse = await unauthenticatedClient.GetAsync($"/swagger/index.html?apikey={ApiKey}");
            Assert.That(queryUiResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        finally
        {
            configProvider.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "AuthenticationEnabled", false }
            });
        }
    }

    [Test]
    public async Task SwaggerCustomCss_returns_200_and_contains_seedarr_theme()
    {
        var response = await GetAsync("/swagger-custom.css");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var css = await response.Content.ReadAsStringAsync();

        Assert.That(css, Does.Contain("#222018"));
        Assert.That(css, Does.Contain("#c8a84e"));
    }
}
