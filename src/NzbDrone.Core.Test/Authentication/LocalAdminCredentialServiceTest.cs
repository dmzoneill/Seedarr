using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class LocalAdminCredentialServiceTest
{
    private LocalAdminCredentialService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new LocalAdminCredentialService();
    }

    [Test]
    public void HashPassword_should_produce_verifiable_v1_hash()
    {
        var hash = _service.HashPassword("StrongPassword1!");

        Assert.That(hash, Does.StartWith("v1$"));
        Assert.That(_service.VerifyStoredPassword("StrongPassword1!", hash), Is.True);
        Assert.That(_service.VerifyStoredPassword("wrong", hash), Is.False);
    }

    [Test]
    public void VerifyStoredPassword_should_support_legacy_plaintext_values()
    {
        Assert.That(_service.VerifyStoredPassword("legacy-pass", "legacy-pass"), Is.True);
        Assert.That(_service.VerifyStoredPassword("other", "legacy-pass"), Is.False);
    }

    [Test]
    public void IsAdminPasswordCredential_should_require_matching_admin_username()
    {
        var config = Substitute.For<IConfigService>();
        var hash = _service.HashPassword("StrongPassword1!");
        config.GetValue("AdminUsername", string.Empty).Returns("admin");
        config.GetValue("AdminPassword", string.Empty).Returns(hash);

        Assert.That(_service.IsAdminPasswordCredential("admin", "StrongPassword1!", config), Is.True);
        Assert.That(_service.IsAdminPasswordCredential("other", "StrongPassword1!", config), Is.False);
        Assert.That(_service.IsAdminPasswordCredential("admin", "wrong", config), Is.False);
    }

    [Test]
    public void ValidateLocalCredentials_should_accept_api_key_or_admin_password()
    {
        var configFile = Substitute.For<IConfigFileProvider>();
        configFile.ApiKey.Returns("api-key-12345");
        var config = Substitute.For<IConfigService>();
        config.GetValue("AdminUsername", string.Empty).Returns("admin");
        config.GetValue("AdminPassword", string.Empty).Returns(_service.HashPassword("StrongPassword1!"));

        Assert.That(_service.ValidateLocalCredentials("admin", "api-key-12345", configFile, config), Is.True);
        Assert.That(_service.ValidateLocalCredentials("admin", "StrongPassword1!", configFile, config), Is.True);
        Assert.That(_service.ValidateLocalCredentials("admin", "nope", configFile, config), Is.False);
    }
}
