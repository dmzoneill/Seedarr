using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;
using Seedarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Controllers;

[TestFixture]
public class ConfigControllerTests
{
    private IConfigService _configService;
    private IConfigFileProvider _configFileProvider;
    private ICertificateManager _certificateManager;
    private GeneralConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _certificateManager = Substitute.For<ICertificateManager>();

        _controller = new GeneralConfigController(
            _configService,
            _configFileProvider,
            _certificateManager);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    [TestCase(70000)]
    public void SaveConfig_with_invalid_port_returns_bad_request(int invalidPort)
    {
        var resource = new GeneralConfigResource
        {
            Port = invalidPort,
            SslPort = 8443,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _configFileProvider.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    [TestCase(70000)]
    public void SaveConfig_with_invalid_ssl_port_returns_bad_request(int invalidSslPort)
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = invalidSslPort,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _configFileProvider.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public void SaveConfig_with_port_and_ssl_port_collision_returns_bad_request()
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8080,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value?.ToString(), Does.Contain("cannot be the same"));
        _configFileProvider.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [TestCase("not-a-valid-ip")]
    [TestCase("999.999.999.999")]
    [TestCase("http://localhost")]
    [TestCase("256.1.1.1")]
    public void SaveConfig_with_invalid_bind_address_returns_bad_request(string invalidBindAddress)
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = invalidBindAddress,
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value?.ToString(), Does.Contain("Invalid BindAddress"));
        _configFileProvider.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [TestCase("*")]
    [TestCase("0.0.0.0")]
    [TestCase("::")]
    [TestCase("localhost")]
    [TestCase("127.0.0.1")]
    [TestCase("192.168.1.50")]
    [TestCase("::1")]
    [TestCase("")]
    [TestCase(null)]
    public void SaveConfig_with_valid_bind_address_succeeds(string validBindAddress)
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = validBindAddress,
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [TestCase("seedarr/", "/seedarr")]
    [TestCase("/seedarr/", "/seedarr")]
    [TestCase("/seedarr", "/seedarr")]
    [TestCase("seedarr", "/seedarr")]
    [TestCase("  /seedarr/  ", "/seedarr")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase(null, "")]
    public void SaveConfig_normalizes_url_base(string inputUrlBase, string expectedNormalized)
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = "*",
            UrlBase = inputUrlBase,
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.UrlBase, Is.EqualTo(expectedNormalized));
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["UrlBase"] == expectedNormalized));
    }

    [Test]
    public void IsValidBindAddress_validates_correctly()
    {
        Assert.That(GeneralConfigController.IsValidBindAddress("*"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("0.0.0.0"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("::"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("localhost"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("127.0.0.1"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("fe80::1"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("[::1]"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("[fe80::1]"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("[127.0.0.1]"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("[::]"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress(""), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress(null), Is.True);

        Assert.That(GeneralConfigController.IsValidBindAddress("invalid_hostname"), Is.False);
        Assert.That(GeneralConfigController.IsValidBindAddress("1.2.3.4.5"), Is.False);
    }

    [Test]
    public void NormalizeUrlBase_normalizes_correctly()
    {
        Assert.That(GeneralConfigController.NormalizeUrlBase("seedarr/"), Is.EqualTo("/seedarr"));
        Assert.That(GeneralConfigController.NormalizeUrlBase("/seedarr/"), Is.EqualTo("/seedarr"));
        Assert.That(GeneralConfigController.NormalizeUrlBase("seedarr"), Is.EqualTo("/seedarr"));
        Assert.That(GeneralConfigController.NormalizeUrlBase("/"), Is.EqualTo(string.Empty));
        Assert.That(GeneralConfigController.NormalizeUrlBase(""), Is.EqualTo(string.Empty));
        Assert.That(GeneralConfigController.NormalizeUrlBase(null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void SaveConfig_with_new_ssl_cert_password_containing_asterisks_saves_password()
    {
        _configFileProvider.SslCertPassword.Returns("OldPass123");

        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10,
            SslCertPassword = "P@ssw*rd#2026",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.SslCertPassword, Is.EqualTo("P@ssw*rd#2026"));
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["SslCertPassword"] == "P@ssw*rd#2026"));
    }

    [Test]
    public void SaveConfig_with_exact_mask_preserves_existing_ssl_cert_password()
    {
        _configFileProvider.SslCertPassword.Returns("ExistingSecret123");

        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10,
            SslCertPassword = "********",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.SslCertPassword, Is.EqualTo("ExistingSecret123"));
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["SslCertPassword"] == "ExistingSecret123"));
    }

    [Test]
    public void SaveConfig_with_unchanged_sentinel_preserves_existing_ssl_cert_password()
    {
        _configFileProvider.SslCertPassword.Returns("ExistingSecret123");

        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10,
            SslCertPassword = "(unchanged)",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.SslCertPassword, Is.EqualTo("ExistingSecret123"));
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["SslCertPassword"] == "ExistingSecret123"));
    }

    [Test]
    public void SaveConfig_with_null_preserves_existing_ssl_cert_password()
    {
        _configFileProvider.SslCertPassword.Returns("ExistingSecret123");

        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10,
            SslCertPassword = null,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.SslCertPassword, Is.EqualTo("ExistingSecret123"));
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["SslCertPassword"] == "ExistingSecret123"));
    }

    [Test]
    public void SaveConfig_with_new_api_key_containing_asterisks_saves_new_key()
    {
        _configFileProvider.ApiKey.Returns("1234567890abcdef");

        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10,
            ApiKey = "my*custom*api*key*2026",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ApiKey, Is.EqualTo("my*custom*api*key*2026"));
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ApiKey"] == "my*custom*api*key*2026"));
    }

    [Test]
    public void SaveConfig_with_masked_api_key_preserves_existing_api_key()
    {
        _configFileProvider.ApiKey.Returns("1234567890abcdef");
        var masked = GeneralConfigResourceMapper.GetMaskedApiKey("1234567890abcdef");

        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10,
            ApiKey = masked,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ApiKey, Is.EqualTo("1234567890abcdef"));
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ApiKey"] == "1234567890abcdef"));
    }

    [Test]
    public void SaveConfig_with_unchanged_sentinel_preserves_existing_api_key()
    {
        _configFileProvider.ApiKey.Returns("1234567890abcdef");

        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10,
            ApiKey = "(unchanged)",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ApiKey, Is.EqualTo("1234567890abcdef"));
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ApiKey"] == "1234567890abcdef"));
    }

    [Test]
    public async System.Threading.Tasks.Task TestSsl_with_new_password_containing_asterisks_passes_new_password()
    {
        _configFileProvider.SslCertPassword.Returns("OldPass123");
        _certificateManager.ValidateCertificateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>())
            .Returns(new SslCertificateValidationResult { IsValid = true });

        var request = new SslTestRequest
        {
            SslCertPath = "/path/cert.pfx",
            SslKeyPath = "",
            SslCertPassword = "P@ssw*rd#2026",
            BindAddress = "*",
            SslPort = 9899,
        };

        var result = await _controller.TestSsl(request);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        await _certificateManager.Received(1).ValidateCertificateAsync(
            request.SslCertPath,
            request.SslKeyPath,
            "P@ssw*rd#2026",
            request.BindAddress,
            request.SslPort,
            true);
    }

    [Test]
    public async System.Threading.Tasks.Task TestSsl_with_exact_mask_passes_existing_password()
    {
        _configFileProvider.SslCertPassword.Returns("StoredSecret123");
        _certificateManager.ValidateCertificateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>())
            .Returns(new SslCertificateValidationResult { IsValid = true });

        var request = new SslTestRequest
        {
            SslCertPath = "/path/cert.pfx",
            SslKeyPath = "",
            SslCertPassword = "********",
            BindAddress = "*",
            SslPort = 9899,
        };

        var result = await _controller.TestSsl(request);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        await _certificateManager.Received(1).ValidateCertificateAsync(
            request.SslCertPath,
            request.SslKeyPath,
            "StoredSecret123",
            request.BindAddress,
            request.SslPort,
            true);
    }

    [Test]
    public async System.Threading.Tasks.Task TestSsl_with_unchanged_sentinel_passes_existing_password()
    {
        _configFileProvider.SslCertPassword.Returns("StoredSecret123");
        _certificateManager.ValidateCertificateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>())
            .Returns(new SslCertificateValidationResult { IsValid = true });

        var request = new SslTestRequest
        {
            SslCertPath = "/path/cert.pfx",
            SslKeyPath = "",
            SslCertPassword = "(unchanged)",
            BindAddress = "*",
            SslPort = 9899,
        };

        var result = await _controller.TestSsl(request);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        await _certificateManager.Received(1).ValidateCertificateAsync(
            request.SslCertPath,
            request.SslKeyPath,
            "StoredSecret123",
            request.BindAddress,
            request.SslPort,
            true);
    }

    [Test]
    public void SaveConfig_saves_xml_properties_to_file_provider_and_only_non_xml_properties_to_config_service()
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = "0.0.0.0",
            UrlBase = "/seedarr",
            ApiKey = "myapikey12345678901234567890",
            EnableSsl = true,
            AuthenticationEnabled = true,
            TerminalAccessEnabled = false,
            SslCertPath = "/cert.pfx",
            SslKeyPath = "/key.pem",
            SslCertPassword = "password123",
            RedirectHttpToHttps = true,
            AutoStart = true,
            WatchFolderEnabled = true,
            WatchFolderPath = "/watch",
            WatchFolderScanIntervalSeconds = 15,
            ThemeStyle = "dark",
            ColorScheme = "blue"
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());

        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (int)d["Port"] == 8080 &&
            (int)d["SslPort"] == 8443 &&
            (string)d["BindAddress"] == "0.0.0.0" &&
            (string)d["UrlBase"] == "/seedarr" &&
            (string)d["ApiKey"] == "myapikey12345678901234567890" &&
            (bool)d["EnableSsl"] == true &&
            (bool)d["AuthenticationEnabled"] == true &&
            (bool)d["TerminalAccessEnabled"] == false &&
            (string)d["SslCertPath"] == "/cert.pfx" &&
            (string)d["SslKeyPath"] == "/key.pem" &&
            (string)d["SslCertPassword"] == "password123" &&
            (bool)d["RedirectHttpToHttps"] == true));

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            !d.ContainsKey("Port") &&
            !d.ContainsKey("SslPort") &&
            !d.ContainsKey("BindAddress") &&
            !d.ContainsKey("UrlBase") &&
            !d.ContainsKey("ApiKey") &&
            !d.ContainsKey("EnableSsl") &&
            !d.ContainsKey("AuthenticationEnabled") &&
            !d.ContainsKey("TerminalAccessEnabled") &&
            !d.ContainsKey("SslCertPath") &&
            !d.ContainsKey("SslKeyPath") &&
            !d.ContainsKey("SslCertPassword") &&
            !d.ContainsKey("RedirectHttpToHttps") &&
            (bool)d["AutoStart"] == true &&
            (bool)d["WatchFolderEnabled"] == true &&
            (string)d["WatchFolderPath"] == "/watch" &&
            (int)d["WatchFolderScanIntervalSeconds"] == 15));
    }
}
