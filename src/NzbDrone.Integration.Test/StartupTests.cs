using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class StartupTests : IntegrationTestBase
{
    [Test]
    public async Task Unmatched_api_v1_route_returns_404_not_found()
    {
        var response = await GetAsync("/api/v1/nonexistent");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Not.Contain("<!doctype html>"));
        Assert.That(content, Does.Not.Contain("<html"));
    }

    [Test]
    public async Task Unmatched_api_route_returns_404_not_found()
    {
        var response = await GetAsync("/api/nonexistent");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Not.Contain("<!doctype html>"));
        Assert.That(content, Does.Not.Contain("<html"));
    }

    [Test]
    public async Task Unmatched_signalr_route_returns_404_not_found()
    {
        var response = await GetAsync("/signalr/nonexistent");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Not.Contain("<!doctype html>"));
        Assert.That(content, Does.Not.Contain("<html"));
    }

    [Test]
    public async Task Unmatched_static_asset_with_file_extension_returns_404_not_found()
    {
        var response = await GetAsync("/assets/chunk-nonexistent.js");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Not.Contain("<!doctype html>"));
        Assert.That(content, Does.Not.Contain("<html"));
    }

    [Test]
    public void Response_compression_service_is_registered()
    {
        var provider = GlobalSetup.Factory.Services.GetService<IResponseCompressionProvider>();

        Assert.That(provider, Is.Not.Null);
    }

    [Test]
    public void Response_compression_options_enable_https()
    {
        var options = GlobalSetup.Factory.Services.GetService<IOptions<ResponseCompressionOptions>>();

        Assert.That(options, Is.Not.Null);
        Assert.That(options.Value.EnableForHttps, Is.True);
    }
}
