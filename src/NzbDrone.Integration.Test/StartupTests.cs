using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Host;

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

    [Test]
    public void DataProtection_keys_folder_is_properly_configured_and_persisted()
    {
        var options = GlobalSetup.Factory.Services.GetService<IOptions<KeyManagementOptions>>();
        Assert.That(options, Is.Not.Null);
        Assert.That(options.Value.XmlRepository, Is.Not.Null);
        Assert.That(options.Value.XmlRepository.GetType().Name, Does.Contain("FileSystemXmlRepository"));

        var appFolderInfo = GlobalSetup.Factory.Services.GetRequiredService<IAppFolderInfo>();
        var keysFolder = Path.Combine(appFolderInfo.AppDataFolder, "DataProtection-Keys");
        Assert.That(Directory.Exists(keysFolder), Is.True);
    }

    [Test]
    public void Startup_ConfigureServices_persists_DataProtection_keys_to_AppFolderInfo_AppDataFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr-dp-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var appFolderInfo = Substitute.For<IAppFolderInfo>();
            appFolderInfo.AppDataFolder.Returns(tempDir);

            var configFileProvider = Substitute.For<IConfigFileProvider>();

            var container = new DryIoc.Container();
            container.RegisterInstance(appFolderInfo);
            container.RegisterInstance(configFileProvider);

            var startup = new Startup(container);
            var services = new ServiceCollection();
            startup.ConfigureServices(services);

            var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<KeyManagementOptions>>();

            Assert.That(options.Value.XmlRepository, Is.Not.Null);
            Assert.That(options.Value.XmlRepository.GetType().Name, Does.Contain("FileSystemXmlRepository"));

            var keysFolder = Path.Combine(tempDir, "DataProtection-Keys");
            Assert.That(Directory.Exists(keysFolder), Is.True);
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
