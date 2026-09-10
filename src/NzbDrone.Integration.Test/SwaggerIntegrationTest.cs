using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

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
        var html = await response.Content.ReadAsStringAsync();

        Assert.That(html, Does.Contain("swagger-ui"));
        Assert.That(html, Does.Contain("swagger-custom.css"));
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
