using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class IdentityProviderServiceTest
{
    private IIdentityProviderRepository _repository;
    private MockHttpMessageHandler _httpHandler;
    private IdentityProviderService _service;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IIdentityProviderRepository>();
        _httpHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(_httpHandler);
        _service = new IdentityProviderService(_repository, httpClient);
    }

    [Test]
    public void GetAll_ReturnsAllProviders()
    {
        var providers = new List<IdentityProviderDefinition>
        {
            new() { Id = 1, ProviderId = "oidc1", Name = "OIDC 1" },
            new() { Id = 2, ProviderId = "oidc2", Name = "OIDC 2" },
        };

        _repository.All().Returns(providers);

        var result = _service.GetAll();

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0].ProviderId, Is.EqualTo("oidc1"));
    }

    [Test]
    public void GetEnabled_ReturnsEnabledOnly()
    {
        var providers = new List<IdentityProviderDefinition>
        {
            new() { Id = 1, ProviderId = "oidc1", Name = "OIDC 1", IsEnabled = true },
        };

        _repository.GetEnabled().Returns(providers);

        var result = _service.GetEnabled();

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].IsEnabled, Is.True);
    }

    [Test]
    public void GetById_ReturnsProvider()
    {
        var provider = new IdentityProviderDefinition { Id = 1, ProviderId = "test", Name = "Test" };
        _repository.Get(1).Returns(provider);

        var result = _service.GetById(1);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("Test"));
    }

    [Test]
    public void Add_SetsTimestampsAndInserts()
    {
        var provider = new IdentityProviderDefinition { ProviderId = "new", Name = "New" };
        _repository.Insert(provider).Returns(provider);

        var result = _service.Add(provider);

        Assert.That(result, Is.Not.Null);
        _repository.Received(1).Insert(Arg.Is<IdentityProviderDefinition>(p => p.ProviderId == "new"));
    }

    [Test]
    public void Delete_CallsRepositoryDelete()
    {
        _service.Delete(5);

        _repository.Received(1).Delete(5);
    }

    [Test]
    public async Task TestConnectionAsync_WhenNoIssuerOrMetadata_ReturnsFalse()
    {
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test",
            Name = "Test",
            IssuerUrl = null,
            MetadataUrl = null,
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.False);
    }

    [Test]
    [TestCase("")]
    [TestCase("   ")]
    public async Task TestConnectionAsync_WhenIssuerUrlIsEmptyOrWhitespace_ReturnsFalse(string url)
    {
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test",
            Name = "Test",
            IssuerUrl = url,
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task TestConnectionAsync_WhenProviderNull_ReturnsFalse()
    {
        var result = await _service.TestConnectionAsync(null);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task TestConnectionAsync_WhenForwardAuthWithoutIssuerUrl_ReturnsTrueWithoutHttpProbe()
    {
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "forward-auth",
            Name = "Forward Auth",
            ProviderType = IdentityProviderType.ForwardAuth,
            IssuerUrl = null,
            MetadataUrl = null,
            TrustedProxies = "10.0.0.1",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.True);
        Assert.That(_httpHandler.LastRequest, Is.Null);
    }

    [Test]
    public async Task TestConnectionAsync_WhenForwardAuthWithEmptyTrustedProxies_ReturnsTrueWithoutHttpProbe()
    {
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "forward-auth",
            Name = "Forward Auth",
            ProviderType = IdentityProviderType.ForwardAuth,
            TrustedProxies = "",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.True);
        Assert.That(_httpHandler.LastRequest, Is.Null);
    }

    [TestCase("http://169.254.169.254/latest/meta-data")]
    [TestCase("https://169.254.169.254/latest/meta-data")]
    [TestCase("http://169.254.1.1/metadata")]
    [TestCase("https://169.254.1.1/metadata")]
    [TestCase("http://127.0.0.1/auth")]
    [TestCase("https://127.0.0.1/auth")]
    [TestCase("http://localhost/auth")]
    [TestCase("https://localhost/auth")]
    [TestCase("https://sub.localhost/auth")]
    [TestCase("http://[::1]/auth")]
    [TestCase("https://[::1]/auth")]
    [TestCase("http://0.0.0.0/auth")]
    [TestCase("https://0.0.0.0/auth")]
    [TestCase("http://[::]/auth")]
    [TestCase("https://[::]/auth")]
    [TestCase("https://[::ffff:127.0.0.1]/auth")]
    [TestCase("http://10.0.0.1/auth")]
    [TestCase("https://10.0.0.1/auth")]
    [TestCase("https://[::ffff:10.0.0.1]/auth")]
    [TestCase("http://172.16.0.1/auth")]
    [TestCase("https://172.16.0.1/auth")]
    [TestCase("http://192.168.1.1/auth")]
    [TestCase("https://192.168.1.1/auth")]
    [TestCase("https://[::ffff:192.168.1.1]/auth")]
    [TestCase("https://[fd00::1]/auth")]
    [TestCase("https://[fe80::1]/auth")]
    [TestCase("https://100.64.0.1/auth")]
    [TestCase("https://100.127.255.255/auth")]
    [TestCase("http://metadata.google.internal/computeMetadata/v1")]
    [TestCase("https://metadata.google.internal/computeMetadata/v1")]
    [TestCase("http://instance-data/latest/meta-data")]
    [TestCase("https://instance-data/latest/meta-data")]
    [TestCase("http://8.8.8.8/auth")]
    [TestCase("ftp://8.8.8.8/auth")]
    [TestCase("not-a-valid-url")]
    public async Task TestConnectionAsync_WhenUrlIsUnsafeOrNonHttps_ReturnsFalse(string url)
    {
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test",
            Name = "Test",
            IssuerUrl = url,
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.False);
    }

    [TestCase("https://169.254.169.254/metadata.xml")]
    [TestCase("http://169.254.169.254/metadata.xml")]
    [TestCase("https://127.0.0.1/metadata.xml")]
    [TestCase("http://127.0.0.1/metadata.xml")]
    [TestCase("https://localhost/metadata.xml")]
    [TestCase("http://localhost/metadata.xml")]
    [TestCase("https://10.0.0.1/metadata.xml")]
    [TestCase("http://10.0.0.1/metadata.xml")]
    [TestCase("https://172.16.0.1/metadata.xml")]
    [TestCase("https://192.168.1.1/metadata.xml")]
    [TestCase("https://[::1]/metadata.xml")]
    [TestCase("http://[::1]/metadata.xml")]
    [TestCase("https://[fd00::1]/metadata.xml")]
    [TestCase("https://[fe80::1]/metadata.xml")]
    [TestCase("https://100.64.0.1/metadata.xml")]
    [TestCase("https://metadata.google.internal/metadata.xml")]
    [TestCase("https://instance-data/metadata.xml")]
    public async Task TestConnectionAsync_WhenSamlMetadataUrlIsUnsafe_ReturnsFalse(string url)
    {
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "saml-test",
            Name = "SAML Test",
            ProviderType = IdentityProviderType.Saml,
            MetadataUrl = url,
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.False);
    }

    [Test]
    public void SharedHandler_AllowAutoRedirect_IsFalse()
    {
        Assert.That(IdentityProviderService.SharedHandler.AllowAutoRedirect, Is.False);
    }

    [Test]
    public async Task TestConnectionAsync_WhenSamlMetadataUrlIsValidHttps_ReturnsTrue()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "<EntityDescriptor></EntityDescriptor>");

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "valid-saml",
            Name = "Valid SAML",
            ProviderType = IdentityProviderType.Saml,
            MetadataUrl = "https://8.8.8.8/saml/metadata.xml",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.True);
        Assert.That(_httpHandler.LastRequest, Is.Not.Null);
        Assert.That(_httpHandler.LastRequest.RequestUri.ToString(), Is.EqualTo("https://8.8.8.8/saml/metadata.xml"));
    }

    [Test]
    public async Task TestConnectionAsync_WhenValidHttpsDiscoveryUrlAndEndpointReturnsOk_ReturnsTrue()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{\"issuer\":\"https://8.8.8.8\"}");

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "valid-oidc",
            Name = "Valid OIDC",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://8.8.8.8",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.True);
        Assert.That(_httpHandler.LastRequest, Is.Not.Null);
        Assert.That(_httpHandler.LastRequest.RequestUri.ToString(), Is.EqualTo("https://8.8.8.8/.well-known/openid-configuration"));
    }

    [Test]
    public async Task TestConnectionAsync_WhenValidHttpsUrlAlreadyContainsWellKnown_ReturnsTrue()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{\"issuer\":\"https://8.8.8.8\"}");

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "valid-oidc",
            Name = "Valid OIDC",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://8.8.8.8/.well-known/openid-configuration",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.True);
        Assert.That(_httpHandler.LastRequest, Is.Not.Null);
        Assert.That(_httpHandler.LastRequest.RequestUri.ToString(), Is.EqualTo("https://8.8.8.8/.well-known/openid-configuration"));
    }

    [Test]
    public async Task TestConnectionAsync_WhenValidHttpsDiscoveryUrlHasWhitespace_TrimsAndReturnsTrue()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{\"issuer\":\"https://8.8.8.8\"}");

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "valid-oidc",
            Name = "Valid OIDC",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "   https://8.8.8.8   ",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.True);
        Assert.That(_httpHandler.LastRequest, Is.Not.Null);
        Assert.That(_httpHandler.LastRequest.RequestUri.ToString(), Is.EqualTo("https://8.8.8.8/.well-known/openid-configuration"));
    }

    [Test]
    public async Task TestConnectionAsync_WhenValidHttpsUrlReturnsError_ReturnsFalse()
    {
        _httpHandler.Enqueue(HttpStatusCode.NotFound, "Not Found");

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "valid-oidc",
            Name = "Valid OIDC",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://8.8.8.8",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.False);
    }

    [TestCase(HttpStatusCode.MovedPermanently)]
    [TestCase(HttpStatusCode.Found)]
    [TestCase(HttpStatusCode.TemporaryRedirect)]
    [TestCase(HttpStatusCode.PermanentRedirect)]
    public async Task TestConnectionAsync_WhenRedirectToSafeHttpsEndpoint_ReturnsTrue(HttpStatusCode redirectStatus)
    {
        var discoveryUrl = "https://8.8.8.8/.well-known/openid-configuration";
        var canonicalUrl = "https://8.8.8.8/.well-known/openid-configuration/";
        _httpHandler.EnqueueWithHeaders(redirectStatus, string.Empty, new Dictionary<string, string> { { "Location", canonicalUrl } });
        _httpHandler.Enqueue(HttpStatusCode.OK, "{\"issuer\":\"https://8.8.8.8\"}");

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "valid-oidc",
            Name = "Valid OIDC",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://8.8.8.8/.well-known/openid-configuration",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.True);
        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(2));
        Assert.That(_httpHandler.Requests[0].RequestUri.ToString(), Is.EqualTo(discoveryUrl));
        Assert.That(_httpHandler.Requests[1].RequestUri.ToString(), Is.EqualTo(canonicalUrl));
    }

    [Test]
    public async Task TestConnectionAsync_WhenRedirectUsesRelativeLocation_ResolvesAgainstRequestUrlAndReturnsTrue()
    {
        _httpHandler.EnqueueWithHeaders(HttpStatusCode.MovedPermanently, string.Empty, new Dictionary<string, string> { { "Location", "metadata.xml" } });
        _httpHandler.Enqueue(HttpStatusCode.OK, "<EntityDescriptor></EntityDescriptor>");

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "valid-saml",
            Name = "Valid SAML",
            ProviderType = IdentityProviderType.Saml,
            MetadataUrl = "https://8.8.8.8/saml/",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.True);
        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(2));
        Assert.That(_httpHandler.Requests[1].RequestUri.ToString(), Is.EqualTo("https://8.8.8.8/saml/metadata.xml"));
    }

    [Test]
    public async Task TestConnectionAsync_WhenRedirectLocationIsUnsafe_ReturnsFalseWithoutFollowUpRequest()
    {
        _httpHandler.EnqueueWithHeaders(HttpStatusCode.Found, string.Empty, new Dictionary<string, string> { { "Location", "https://127.0.0.1/metadata" } });

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "valid-oidc",
            Name = "Valid OIDC",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://8.8.8.8",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.False);
        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task TestConnectionAsync_WhenRedirectMissingLocation_ReturnsFalse()
    {
        _httpHandler.Enqueue(HttpStatusCode.MovedPermanently, string.Empty);

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "valid-oidc",
            Name = "Valid OIDC",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://8.8.8.8",
        };

        var result = await _service.TestConnectionAsync(provider);

        Assert.That(result, Is.False);
        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(1));
    }

    [Test]
    public void Add_WhenClientSecretProvided_EncryptsSecretAtRest()
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var service = new IdentityProviderService(_repository, null, dataProtection);

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test_encrypt",
            Name = "Test Encrypt",
            ClientSecretEncrypted = "raw-super-secret-123",
        };

        _repository.Insert(Arg.Any<IdentityProviderDefinition>()).Returns(x => x.Arg<IdentityProviderDefinition>());

        var result = service.Add(provider);

        Assert.That(result.ClientSecretEncrypted, Is.Not.Null);
        Assert.That(result.ClientSecretEncrypted, Is.Not.EqualTo("raw-super-secret-123"));

        var decrypted = service.DecryptClientSecret(result.ClientSecretEncrypted);
        Assert.That(decrypted, Is.EqualTo("raw-super-secret-123"));
    }

    [Test]
    public void Update_WhenCallerOmitsCreatedAt_PreservesExistingCreatedAt()
    {
        var originalCreatedAt = new DateTime(2020, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test_created",
            Name = "Original",
            CreatedAt = originalCreatedAt,
        };

        _repository.Get(1).Returns(existing);

        var provider = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test_created",
            Name = "Updated Name",
        };

        var result = _service.Update(provider);

        Assert.That(result.CreatedAt, Is.EqualTo(originalCreatedAt));
        _repository.Received(1).Update(Arg.Is<IdentityProviderDefinition>(p => p.CreatedAt == originalCreatedAt));
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("********")]
    [TestCase("******")]
    public void Update_WhenClientSecretEmptyOrMasked_PreservesExistingSecret(string incomingSecret)
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var service = new IdentityProviderService(_repository, null, dataProtection);

        var existingEncrypted = service.EncryptClientSecret("stored-secret-789");
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test_preserve",
            Name = "Test Preserve",
            ClientSecretEncrypted = existingEncrypted,
        };

        _repository.Get(1).Returns(existing);

        var provider = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test_preserve",
            Name = "Updated Name",
            ClientSecretEncrypted = incomingSecret,
        };

        var result = service.Update(provider);

        Assert.That(result.ClientSecretEncrypted, Is.EqualTo(existingEncrypted));
        Assert.That(service.DecryptClientSecret(result.ClientSecretEncrypted), Is.EqualTo("stored-secret-789"));
        _repository.Received(1).Update(Arg.Is<IdentityProviderDefinition>(p => p.ClientSecretEncrypted == existingEncrypted));
    }

    [Test]
    public void Update_WhenNewClientSecretProvided_EncryptsSecretAtRest()
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var service = new IdentityProviderService(_repository, null, dataProtection);

        var provider = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test_encrypt",
            Name = "Test Encrypt",
            ClientSecretEncrypted = "updated-raw-secret-456",
        };

        var result = service.Update(provider);

        Assert.That(result.ClientSecretEncrypted, Is.Not.Null);
        Assert.That(result.ClientSecretEncrypted, Is.Not.EqualTo("updated-raw-secret-456"));

        var decrypted = service.DecryptClientSecret(result.ClientSecretEncrypted);
        Assert.That(decrypted, Is.EqualTo("updated-raw-secret-456"));
    }

    [Test]
    public void DecryptClientSecret_WhenLegacyPlaintextSecret_ReturnsPlaintextUnchanged()
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var service = new IdentityProviderService(_repository, null, dataProtection);

        var legacyPlaintext = "unencrypted-legacy-secret-xyz";
        var decrypted = service.DecryptClientSecret(legacyPlaintext);

        Assert.That(decrypted, Is.EqualTo("unencrypted-legacy-secret-xyz"));
    }

    [Test]
    public void EncryptClientSecret_WhenAlreadyEncrypted_DoesNotDoubleEncrypt()
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var service = new IdentityProviderService(_repository, null, dataProtection);

        var encryptedOnce = service.EncryptClientSecret("my-secret");
        var encryptedTwice = service.EncryptClientSecret(encryptedOnce);

        Assert.That(encryptedTwice, Is.EqualTo(encryptedOnce));
        Assert.That(service.DecryptClientSecret(encryptedTwice), Is.EqualTo("my-secret"));
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    public void EncryptAndDecryptClientSecret_WhenNullOrEmpty_ReturnsOriginal(string secret)
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var service = new IdentityProviderService(_repository, null, dataProtection);

        Assert.That(service.EncryptClientSecret(secret), Is.EqualTo(secret));
        Assert.That(service.DecryptClientSecret(secret), Is.EqualTo(secret));
    }

    [Test]
    public void EncryptClientSecret_WhenProtectFails_ThrowsAndDoesNotReturnPlaintext()
    {
        var dataProtection = Substitute.For<IDataProtectionProvider>();
        var protector = Substitute.For<IDataProtector>();
        dataProtection.CreateProtector(IdentityProviderService.DataProtectionPurpose).Returns(protector);
        protector.Protect(Arg.Any<string>()).Returns(_ => throw new CryptographicException("key ring unavailable"));
        protector.Unprotect(Arg.Any<string>()).Returns(_ => throw new CryptographicException("not encrypted"));

        var service = new IdentityProviderService(_repository, null, dataProtection);

        var ex = Assert.Throws<InvalidOperationException>(() => service.EncryptClientSecret("plaintext-secret"));
        Assert.That(ex!.InnerException, Is.TypeOf<CryptographicException>());
    }

    [Test]
    public void Add_WhenProtectFails_DoesNotPersistPlaintextSecret()
    {
        var dataProtection = Substitute.For<IDataProtectionProvider>();
        var protector = Substitute.For<IDataProtector>();
        dataProtection.CreateProtector(IdentityProviderService.DataProtectionPurpose).Returns(protector);
        protector.Protect(Arg.Any<string>()).Returns(_ => throw new CryptographicException("key ring unavailable"));
        protector.Unprotect(Arg.Any<string>()).Returns(_ => throw new CryptographicException("not encrypted"));

        var service = new IdentityProviderService(_repository, null, dataProtection);
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test_encrypt_fail",
            Name = "Test Encrypt Fail",
            ClientSecretEncrypted = "raw-super-secret-123",
        };

        Assert.Throws<InvalidOperationException>(() => service.Add(provider));
        _repository.DidNotReceive().Insert(Arg.Any<IdentityProviderDefinition>());
    }

    [Test]
    public void EncryptClientSecret_WhenProtectedWithDifferentKeyRing_ThrowsWithoutReWrapping()
    {
        var oldProtection = new EphemeralDataProtectionProvider();
        var oldService = new IdentityProviderService(_repository, null, oldProtection);
        var foreignCiphertext = oldService.EncryptClientSecret("stored-secret-789");

        var newProtection = new EphemeralDataProtectionProvider();
        var service = new IdentityProviderService(_repository, null, newProtection);

        var ex = Assert.Throws<InvalidOperationException>(() => service.EncryptClientSecret(foreignCiphertext));
        Assert.That(ex!.Message, Does.Contain("data protection key ring"));
    }

    [Test]
    public void Update_WhenClientSecretCiphertextFromDifferentKeyRing_ThrowsWithoutPersisting()
    {
        var oldProtection = new EphemeralDataProtectionProvider();
        var oldService = new IdentityProviderService(_repository, null, oldProtection);
        var foreignCiphertext = oldService.EncryptClientSecret("stored-secret-789");

        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test_key_rotation",
            Name = "Test Key Rotation",
            ClientSecretEncrypted = foreignCiphertext,
        };
        _repository.Get(1).Returns(existing);

        var newProtection = new EphemeralDataProtectionProvider();
        var service = new IdentityProviderService(_repository, null, newProtection);

        var provider = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test_key_rotation",
            Name = "Updated Name",
            ClientSecretEncrypted = foreignCiphertext,
        };

        Assert.Throws<InvalidOperationException>(() => service.Update(provider));
        _repository.DidNotReceive().Update(Arg.Any<IdentityProviderDefinition>());
    }

    [Test]
    public void Add_WhenClientSecretCiphertextFromDifferentKeyRing_ThrowsWithoutPersisting()
    {
        var oldProtection = new EphemeralDataProtectionProvider();
        var oldService = new IdentityProviderService(_repository, null, oldProtection);
        var foreignCiphertext = oldService.EncryptClientSecret("stored-secret-789");

        var newProtection = new EphemeralDataProtectionProvider();
        var service = new IdentityProviderService(_repository, null, newProtection);

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test_key_rotation_add",
            Name = "Test Key Rotation Add",
            ClientSecretEncrypted = foreignCiphertext,
        };

        Assert.Throws<InvalidOperationException>(() => service.Add(provider));
        _repository.DidNotReceive().Insert(Arg.Any<IdentityProviderDefinition>());
    }

    [Test]
    public void Update_WhenProtectFails_DoesNotPersistPlaintextSecret()
    {
        var dataProtection = Substitute.For<IDataProtectionProvider>();
        var protector = Substitute.For<IDataProtector>();
        dataProtection.CreateProtector(IdentityProviderService.DataProtectionPurpose).Returns(protector);
        protector.Protect(Arg.Any<string>()).Returns(_ => throw new CryptographicException("key ring unavailable"));
        protector.Unprotect(Arg.Any<string>()).Returns(_ => throw new CryptographicException("not encrypted"));

        var service = new IdentityProviderService(_repository, null, dataProtection);
        var provider = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test_encrypt_fail",
            Name = "Test Encrypt Fail",
            ClientSecretEncrypted = "updated-raw-secret-456",
        };

        Assert.Throws<InvalidOperationException>(() => service.Update(provider));
        _repository.DidNotReceive().Update(Arg.Any<IdentityProviderDefinition>());
    }
}
