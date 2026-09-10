// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;

namespace NzbDrone.Core.Test.Security;

[TestFixture]
public class CertificateManagerTest
{
    private string _tempDir;
    private IAppFolderInfo _appFolderInfo;
    private IConfigFileProvider _config;
    private CertificateManager _certificateManager;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"seedarr-cert-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempDir);

        _config = Substitute.For<IConfigFileProvider>();
        _config.BindAddress.Returns("127.0.0.1");
        _config.Port.Returns(9898);
        _config.EnableSsl.Returns(true);
        _config.SslPort.Returns(9899);
        _config.SslCertPath.Returns(string.Empty);
        _config.SslKeyPath.Returns(string.Empty);
        _config.SslCertPassword.Returns(string.Empty);

        _certificateManager = new CertificateManager(_appFolderInfo);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void Constructor_WhenAppFolderInfoNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CertificateManager(null));
    }

    [Test]
    public void GetOrCreateCertificate_WhenCertPathEmpty_GeneratesAndCachesSelfSignedCert()
    {
        var cert = _certificateManager.GetOrCreateCertificate(_config);

        Assert.That(cert, Is.Not.Null);
        Assert.That(cert.HasPrivateKey, Is.True);
        Assert.That(cert.Subject, Does.Contain("Seedarr"));
        Assert.That(cert.NotAfter.ToUniversalTime(), Is.LessThanOrEqualTo(DateTime.UtcNow.AddDays(398)));
        Assert.That(cert.NotAfter.ToUniversalTime(), Is.GreaterThan(DateTime.UtcNow.AddDays(365)));

        var basicConstraints = cert.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
        Assert.That(basicConstraints, Is.Not.Null);
        Assert.That(basicConstraints.CertificateAuthority, Is.False);
        Assert.That(basicConstraints.Critical, Is.True);

        var ski = cert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().SingleOrDefault();
        Assert.That(ski, Is.Not.Null);
        Assert.That(string.IsNullOrWhiteSpace(ski.SubjectKeyIdentifier), Is.False);

        var cachedPfx = Path.Combine(_tempDir, "seedarr-selfsigned.pfx");
        Assert.That(File.Exists(cachedPfx), Is.True);

        var cachedPwd = Path.Combine(_tempDir, "seedarr-selfsigned.pwd");
        Assert.That(File.Exists(cachedPwd), Is.True);
        Assert.That(string.IsNullOrWhiteSpace(File.ReadAllText(cachedPwd)), Is.False);
    }

    [Test]
    public void GetOrCreateCertificate_WhenCustomPasswordConfigured_GeneratesWithConfiguredPassword()
    {
        _config.SslCertPassword.Returns("custom-secret-password-123");

        var cert = _certificateManager.GetOrCreateCertificate(_config);

        Assert.That(cert, Is.Not.Null);
        Assert.That(cert.HasPrivateKey, Is.True);

        var cachedPfx = Path.Combine(_tempDir, "seedarr-selfsigned.pfx");
        Assert.That(File.Exists(cachedPfx), Is.True);

        // Verify PFX can be loaded with the configured password
        var loaded = X509CertificateLoader.LoadPkcs12FromFile(cachedPfx, "custom-secret-password-123", X509KeyStorageFlags.Exportable);
        Assert.That(loaded.Thumbprint, Is.EqualTo(cert.Thumbprint));
    }

    [Test]
    public void GetOrCreateCertificate_WhenSelfSignedCertCached_ReusesExistingCert()
    {
        var cert1 = _certificateManager.GetOrCreateCertificate(_config);
        var cert2 = _certificateManager.GetOrCreateCertificate(_config);

        Assert.That(cert1.Thumbprint, Is.EqualTo(cert2.Thumbprint));
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenSelfSigned_ReturnsValidResultWithSans()
    {
        var result = await _certificateManager.ValidateCertificateAsync(
            certPath: string.Empty,
            keyPath: string.Empty,
            password: string.Empty,
            bindAddress: "127.0.0.1",
            sslPort: 9899,
            testTlsHandshake: false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.True, result.Message);
        Assert.That(result.HasPrivateKey, Is.True);
        Assert.That(result.Subject, Does.Contain("Seedarr"));
        Assert.That(result.SubjectAlternativeNames, Does.Contain("localhost"));
        Assert.That(result.SubjectAlternativeNames, Does.Contain("127.0.0.1"));
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenCertPathNotFound_ReturnsInvalidResult()
    {
        var result = await _certificateManager.ValidateCertificateAsync(
            certPath: Path.Combine(_tempDir, "missing-cert.pfx"),
            keyPath: string.Empty,
            password: string.Empty,
            bindAddress: "127.0.0.1",
            sslPort: 9899,
            testTlsHandshake: false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenCustomPfxProvided_ValidatesSuccessfully()
    {
        var customPfxPath = Path.Combine(_tempDir, "custom-test.pfx");
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=CustomHost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var testCert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pfxBytes = testCert.Export(X509ContentType.Pfx, "testpass");
        await File.WriteAllBytesAsync(customPfxPath, pfxBytes);

        var result = await _certificateManager.ValidateCertificateAsync(
            certPath: customPfxPath,
            keyPath: string.Empty,
            password: "testpass",
            bindAddress: "127.0.0.1",
            sslPort: 9899,
            testTlsHandshake: false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Subject, Does.Contain("CustomHost"));
        Assert.That(result.HasPrivateKey, Is.True);
    }

    [Test]
    public void GetOrCreateCertificate_WhenPemWithIntermediateChainAndSeparateKey_LoadsFullChainAndNormalizesToPkcs12()
    {
        var (fullChainPem, keyPem, _, _) = GenerateTestChain();
        var certPemPath = Path.Combine(_tempDir, "fullchain.pem");
        var keyPemPath = Path.Combine(_tempDir, "privkey.pem");

        File.WriteAllText(certPemPath, fullChainPem);
        File.WriteAllText(keyPemPath, keyPem);

        _config.SslCertPath.Returns(certPemPath);
        _config.SslKeyPath.Returns(keyPemPath);

        var cert = _certificateManager.GetOrCreateCertificate(_config);

        Assert.That(cert, Is.Not.Null);
        Assert.That(cert.HasPrivateKey, Is.True);
        Assert.That(cert.Subject, Does.Contain("seedarr-server.local"));
        Assert.That(cert.Issuer, Does.Contain("Test Intermediate CA"));
    }

    [Test]
    public void GetOrCreateCertificate_WhenPemWithIntermediateChainAndKeyInSingleFile_LoadsFullChainAndNormalizesToPkcs12()
    {
        var (fullChainPem, keyPem, _, _) = GenerateTestChain();
        var combinedPem = $"{fullChainPem}\n{keyPem}";
        var bundlePemPath = Path.Combine(_tempDir, "bundle.pem");

        File.WriteAllText(bundlePemPath, combinedPem);

        _config.SslCertPath.Returns(bundlePemPath);
        _config.SslKeyPath.Returns(string.Empty);

        var cert = _certificateManager.GetOrCreateCertificate(_config);

        Assert.That(cert, Is.Not.Null);
        Assert.That(cert.HasPrivateKey, Is.True);
        Assert.That(cert.Subject, Does.Contain("seedarr-server.local"));
        Assert.That(cert.Issuer, Does.Contain("Test Intermediate CA"));
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenPemWithFullChain_ReturnsValidWithSansAndPrivateKey()
    {
        var (fullChainPem, keyPem, _, _) = GenerateTestChain();
        var certPemPath = Path.Combine(_tempDir, "fullchain.pem");
        var keyPemPath = Path.Combine(_tempDir, "privkey.pem");

        await File.WriteAllTextAsync(certPemPath, fullChainPem);
        await File.WriteAllTextAsync(keyPemPath, keyPem);

        var result = await _certificateManager.ValidateCertificateAsync(
            certPath: certPemPath,
            keyPath: keyPemPath,
            password: string.Empty,
            bindAddress: "127.0.0.1",
            sslPort: 9899,
            testTlsHandshake: false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.True, result.Message);
        Assert.That(result.HasPrivateKey, Is.True);
        Assert.That(result.Subject, Does.Contain("seedarr-server.local"));
        Assert.That(result.SubjectAlternativeNames, Does.Contain("seedarr-server.local"));
        Assert.That(result.SubjectAlternativeNames, Does.Contain("127.0.0.1"));
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenPemMissingPrivateKey_ReturnsInvalidResult()
    {
        var (fullChainPem, _, _, _) = GenerateTestChain();
        var certPemPath = Path.Combine(_tempDir, "certonly.pem");

        await File.WriteAllTextAsync(certPemPath, fullChainPem);

        var result = await _certificateManager.ValidateCertificateAsync(
            certPath: certPemPath,
            keyPath: string.Empty,
            password: string.Empty,
            bindAddress: "127.0.0.1",
            sslPort: 9899,
            testTlsHandshake: false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Message, Does.Contain("does not contain a private key"));
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenPemWithEncryptedPrivateKey_ValidatesSuccessfullyWithPassword()
    {
        using var caRsa = RSA.Create(2048);
        var caReq = new CertificateRequest("CN=Test Intermediate CA", caRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var caCert = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddYears(2));

        using var leafRsa = RSA.Create(2048);
        var leafReq = new CertificateRequest("CN=encrypted-leaf.local", leafRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

        var serial = new byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(serial);
        using var leafCert = leafReq.Create(caCert, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddYears(1), serial);

        var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000);
        var encryptedKeyPem = leafRsa.ExportEncryptedPkcs8PrivateKeyPem("secretpassword".AsSpan(), pbeParams);
        var fullChainPem = $"{leafCert.ExportCertificatePem()}\n{caCert.ExportCertificatePem()}";

        var certPemPath = Path.Combine(_tempDir, "fullchain-enc.pem");
        var keyPemPath = Path.Combine(_tempDir, "privkey-enc.pem");

        await File.WriteAllTextAsync(certPemPath, fullChainPem);
        await File.WriteAllTextAsync(keyPemPath, encryptedKeyPem);

        var result = await _certificateManager.ValidateCertificateAsync(
            certPath: certPemPath,
            keyPath: keyPemPath,
            password: "secretpassword",
            bindAddress: "127.0.0.1",
            sslPort: 9899,
            testTlsHandshake: false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.True, result.Message);
        Assert.That(result.HasPrivateKey, Is.True);
        Assert.That(result.Subject, Does.Contain("encrypted-leaf.local"));
    }

    [Test]
    public void GetOrCreateCertificate_SelfSignedCert_HasBasicConstraintsAndSubjectKeyIdentifierAndValidLifetime()
    {
        var cert = _certificateManager.GetOrCreateCertificate(_config);

        Assert.That(cert, Is.Not.Null);

        // 398-day browser limit compliance (capping to 397 days)
        var totalDays = (cert.NotAfter.ToUniversalTime() - cert.NotBefore.ToUniversalTime()).TotalDays;
        Assert.That(totalDays, Is.LessThanOrEqualTo(398));
        Assert.That(cert.NotAfter.ToUniversalTime(), Is.LessThanOrEqualTo(DateTime.UtcNow.AddDays(398)));
        Assert.That(cert.NotAfter.ToUniversalTime(), Is.GreaterThan(DateTime.UtcNow.AddDays(365)));

        // Basic Constraints: CA = false, Critical = true (RFC 5280)
        var basicConstraints = cert.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
        Assert.That(basicConstraints, Is.Not.Null);
        Assert.That(basicConstraints.CertificateAuthority, Is.False);
        Assert.That(basicConstraints.Critical, Is.True);

        // Subject Key Identifier (RFC 5280)
        var ski = cert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().SingleOrDefault();
        Assert.That(ski, Is.Not.Null);
        Assert.That(string.IsNullOrWhiteSpace(ski.SubjectKeyIdentifier), Is.False);

        // Key Usage
        var keyUsage = cert.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
        Assert.That(keyUsage, Is.Not.Null);
        Assert.That(keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature), Is.True);
        Assert.That(keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment), Is.True);

        // Enhanced Key Usage (Server Authentication)
        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
        Assert.That(eku, Is.Not.Null);
        Assert.That(eku.EnhancedKeyUsages["1.3.6.1.5.5.7.3.1"], Is.Not.Null);
    }

    [Test]
    public void GetOrCreateCertificate_WhenBindAddressIsHostname_IncludesDnsSan()
    {
        _config.BindAddress.Returns("seedarr.local");

        var cert = _certificateManager.GetOrCreateCertificate(_config);

        Assert.That(cert, Is.Not.Null);
        var sanExt = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().SingleOrDefault();
        Assert.That(sanExt, Is.Not.Null);

        var dnsNames = sanExt.EnumerateDnsNames().ToList();
        Assert.That(dnsNames, Does.Contain("seedarr.local"));
        Assert.That(dnsNames, Does.Contain("localhost"));
    }

    [Test]
    public void GetOrCreateCertificate_WhenBindAddressIsWildcard_DoesNotIncludeWildcardSan()
    {
        _config.BindAddress.Returns("*");

        var cert = _certificateManager.GetOrCreateCertificate(_config);

        Assert.That(cert, Is.Not.Null);
        var sanExt = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().SingleOrDefault();
        Assert.That(sanExt, Is.Not.Null);

        var dnsNames = sanExt.EnumerateDnsNames().ToList();
        Assert.That(dnsNames, Does.Not.Contain("*"));
    }

    [Test]
    public async Task ValidateCertificateAsync_WhenBindAddressIsHostname_IncludesHostnameInSubjectAlternativeNames()
    {
        var result = await _certificateManager.ValidateCertificateAsync(
            certPath: string.Empty,
            keyPath: string.Empty,
            password: string.Empty,
            bindAddress: "media.home.arpa",
            sslPort: 9899,
            testTlsHandshake: false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.True, result.Message);
        Assert.That(result.SubjectAlternativeNames, Does.Contain("media.home.arpa"));
    }

    private static (string FullChainPem, string KeyPem, string LeafPem, string CaPem) GenerateTestChain(
        string subjectName = "CN=seedarr-server.local",
        string caSubject = "CN=Test Intermediate CA")
    {
        using var caRsa = RSA.Create(2048);
        var caReq = new CertificateRequest(caSubject, caRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var caCert = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddYears(2));

        using var leafRsa = RSA.Create(2048);
        var leafReq = new CertificateRequest(subjectName, leafRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("seedarr-server.local");
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        leafReq.CertificateExtensions.Add(sanBuilder.Build());

        var serial = new byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(serial);
        using var leafCert = leafReq.Create(caCert, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddYears(1), serial);

        var caPem = caCert.ExportCertificatePem();
        var leafPem = leafCert.ExportCertificatePem();
        var keyPem = leafRsa.ExportPkcs8PrivateKeyPem();
        var fullChainPem = $"{leafPem}\n{caPem}";

        return (fullChainPem, keyPem, leafPem, caPem);
    }
}
