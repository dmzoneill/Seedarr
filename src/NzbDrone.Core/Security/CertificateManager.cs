// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Security;

public class CertificateManager : ICertificateManager, IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly object _syncLock = new();

    private X509Certificate2 _cachedCustomCert;
    private X509Certificate2Collection _cachedCustomChain = new();
    private string _cachedCustomCertPath;
    private string _cachedCustomKeyPath;
    private string _cachedCustomPassword;
    private DateTime _cachedCustomCertLastWriteTimeUtc;
    private DateTime _cachedCustomKeyLastWriteTimeUtc;

    private X509Certificate2 _cachedSelfSignedCert;
    private string _cachedSelfSignedPath;
    private DateTime _cachedSelfSignedLastWriteTimeUtc;
    private bool _disposed;

    public CertificateManager(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo ?? throw new ArgumentNullException(nameof(appFolderInfo));
    }

    public X509Certificate2 GetOrCreateCertificate(IConfigFileProvider config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_syncLock)
        {
            if (!string.IsNullOrWhiteSpace(config.SslCertPath))
            {
                var certPath = config.SslCertPath.Trim();
                var keyPath = config.SslKeyPath?.Trim() ?? string.Empty;
                var password = config.SslCertPassword ?? string.Empty;

                if (File.Exists(certPath))
                {
                    var currentCertWriteTime = File.GetLastWriteTimeUtc(certPath);
                    var currentKeyWriteTime = (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath))
                        ? File.GetLastWriteTimeUtc(keyPath)
                        : DateTime.MinValue;

                    if (_cachedCustomCert != null &&
                        string.Equals(_cachedCustomCertPath, certPath, StringComparison.Ordinal) &&
                        string.Equals(_cachedCustomKeyPath, keyPath, StringComparison.Ordinal) &&
                        string.Equals(_cachedCustomPassword, password, StringComparison.Ordinal) &&
                        currentCertWriteTime <= _cachedCustomCertLastWriteTimeUtc &&
                        currentKeyWriteTime <= _cachedCustomKeyLastWriteTimeUtc &&
                        DateTime.UtcNow < _cachedCustomCert.NotAfter.ToUniversalTime())
                    {
                        return _cachedCustomCert;
                    }

                    try
                    {
                        var (loadedCert, loadedChain) = LoadCustomCertificate(certPath, keyPath, password);
                        if (loadedCert != null)
                        {
                            var oldCert = _cachedCustomCert;
                            var oldChain = _cachedCustomChain;

                            _cachedCustomCert = loadedCert;
                            _cachedCustomChain = loadedChain ?? new X509Certificate2Collection();
                            _cachedCustomCertPath = certPath;
                            _cachedCustomKeyPath = keyPath;
                            _cachedCustomPassword = password;
                            _cachedCustomCertLastWriteTimeUtc = currentCertWriteTime;
                            _cachedCustomKeyLastWriteTimeUtc = currentKeyWriteTime;

                            if (oldCert != null && !ReferenceEquals(oldCert, loadedCert))
                            {
                                SafeDisposeCertificate(oldCert);
                                SafeDisposeChain(oldChain);
                            }

                            if (_cachedSelfSignedCert != null)
                            {
                                SafeDisposeCertificate(_cachedSelfSignedCert);
                                _cachedSelfSignedCert = null;
                                _cachedSelfSignedPath = null;
                            }

                            Logger.Info("Successfully loaded custom SSL certificate from '{0}'", certPath);
                            return loadedCert;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_cachedCustomCert != null)
                        {
                            Logger.Warn(ex, "Failed to reload custom SSL certificate from '{0}'. Retaining previously loaded valid certificate.", certPath);
                            return _cachedCustomCert;
                        }

                        Logger.Error(ex, "Failed to load custom SSL certificate from '{0}'. Falling back to self-signed certificate.", certPath);
                    }
                }
                else
                {
                    if (_cachedCustomCert != null)
                    {
                        Logger.Warn("Configured SSL certificate path '{0}' was not found. Retaining previously loaded valid certificate.", certPath);
                        return _cachedCustomCert;
                    }

                    Logger.Warn("Configured SSL certificate path '{0}' was not found. Falling back to self-signed certificate.", certPath);
                }
            }
            else
            {
                if (_cachedCustomCert != null)
                {
                    SafeDisposeCertificate(_cachedCustomCert);
                    _cachedCustomCert = null;
                    SafeDisposeChain(_cachedCustomChain);
                    _cachedCustomChain = new X509Certificate2Collection();
                    _cachedCustomCertPath = null;
                    _cachedCustomKeyPath = null;
                    _cachedCustomPassword = null;
                }
            }

            return GetOrCreateSelfSignedCertificate(config);
        }
    }

    public X509Certificate2Collection GetCertificateChain()
    {
        lock (_syncLock)
        {
            return new X509Certificate2Collection(_cachedCustomChain);
        }
    }

    public async Task<SslCertificateValidationResult> ValidateCertificateAsync(
        string certPath,
        string keyPath,
        string password,
        string bindAddress,
        int sslPort,
        bool testTlsHandshake = true)
    {
        var result = new SslCertificateValidationResult();
        X509Certificate2 cert = null;
        X509Certificate2Collection loadedChain = null;
        var shouldDisposeCert = false;

        try
        {
            if (!string.IsNullOrWhiteSpace(certPath))
            {
                var trimmedPath = certPath.Trim();
                if (!File.Exists(trimmedPath))
                {
                    result.IsValid = false;
                    result.Message = $"Certificate file not found at '{trimmedPath}'.";
                    return result;
                }

                var (loadedCert, chain) = LoadCustomCertificate(trimmedPath, keyPath, password);
                cert = loadedCert;
                loadedChain = chain;
                shouldDisposeCert = true;
            }
            else
            {
                var dummyConfig = new DummySslConfig(bindAddress);
                cert = GetOrCreateSelfSignedCertificate(dummyConfig);
                shouldDisposeCert = false;
            }

            if (cert == null)
            {
                result.IsValid = false;
                result.Message = "Failed to load or generate a valid certificate.";
                return result;
            }

            result.Subject = cert.Subject;
            result.Issuer = cert.Issuer;
            result.ValidFrom = cert.NotBefore.ToUniversalTime();
            result.ValidTo = cert.NotAfter.ToUniversalTime();
            result.Thumbprint = cert.Thumbprint;
            result.HasPrivateKey = cert.HasPrivateKey;

            foreach (var ext in cert.Extensions)
            {
                if (ext is X509SubjectAlternativeNameExtension sanExt)
                {
                    foreach (var dns in sanExt.EnumerateDnsNames())
                    {
                        result.SubjectAlternativeNames.Add(dns);
                    }

                    foreach (var ip in sanExt.EnumerateIPAddresses())
                    {
                        result.SubjectAlternativeNames.Add(ip.ToString());
                    }
                }
            }

            if (!cert.HasPrivateKey)
            {
                result.IsValid = false;
                result.Message = "The certificate was loaded, but it does not contain a private key.";
                return result;
            }

            if (DateTime.UtcNow > cert.NotAfter.ToUniversalTime())
            {
                result.IsValid = false;
                result.Message = $"The certificate has expired on {cert.NotAfter.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC.";
                return result;
            }

            if (DateTime.UtcNow < cert.NotBefore.ToUniversalTime())
            {
                result.IsValid = false;
                result.Message = $"The certificate is not yet valid (valid starting {cert.NotBefore.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC).";
                return result;
            }

            result.IsValid = true;
            result.Message = "Certificate is valid and cryptographically verified.";

            if (testTlsHandshake && sslPort is >= 1 and <= 65535)
            {
                try
                {
                    using var handler = new SocketsHttpHandler
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(2),
                        SslOptions = new SslClientAuthenticationOptions
                        {
                            RemoteCertificateValidationCallback = (sender, serverCert, chain, errors) => true,
                        },
                    };

                    using var httpClient = new HttpClient(handler)
                    {
                        Timeout = TimeSpan.FromSeconds(2),
                    };

                    var probeHost = bindAddress switch
                    {
                        null or "" or "*" or "0.0.0.0" => "127.0.0.1",
                        "::" => "[::1]",
                        var addr => addr,
                    };

                    var testUrl = $"https://{probeHost}:{sslPort}/";
                    using var response = await httpClient.GetAsync(testUrl);
                    result.HandshakeSucceeded = true;
                    result.Message = $"Certificate is valid and active on HTTPS port {sslPort} (TLS handshake succeeded).";
                }
                catch
                {
                    result.HandshakeSucceeded = false;
                    result.Message = $"Certificate is valid. (HTTPS listener will activate on port {sslPort} once the server is restarted).";
                }
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Message = $"Certificate validation failed: {ex.Message}";
        }
        finally
        {
            if (shouldDisposeCert)
            {
                SafeDisposeCertificate(cert);
                SafeDisposeChain(loadedChain);
            }
        }

        return result;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                lock (_syncLock)
                {
                    SafeDisposeCertificate(_cachedCustomCert);
                    _cachedCustomCert = null;
                    SafeDisposeChain(_cachedCustomChain);
                    _cachedCustomChain = new X509Certificate2Collection();
                    _cachedCustomCertPath = null;
                    _cachedCustomKeyPath = null;
                    _cachedCustomPassword = null;

                    SafeDisposeCertificate(_cachedSelfSignedCert);
                    _cachedSelfSignedCert = null;
                    _cachedSelfSignedPath = null;
                }
            }

            _disposed = true;
        }
    }

    internal static (X509Certificate2 Leaf, X509Certificate2Collection Chain) LoadCustomCertificate(string certPath, string keyPath, string password)
    {
        var ext = Path.GetExtension(certPath).ToLowerInvariant();

        if (ext is ".pfx" or ".p12")
        {
            var pass = string.IsNullOrEmpty(password) ? null : password;
            var loadedCollection = X509CertificateLoader.LoadPkcs12CollectionFromFile(
                certPath,
                pass,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

            if (loadedCollection.Count == 0)
            {
                throw new InvalidOperationException($"PKCS#12 file '{certPath}' does not contain any certificates.");
            }

            X509Certificate2 leaf = null;
            foreach (var cert in loadedCollection)
            {
                if (cert.HasPrivateKey)
                {
                    leaf = cert;
                    break;
                }
            }

            leaf ??= loadedCollection[0];

            var chain = new X509Certificate2Collection();
            foreach (var cert in loadedCollection)
            {
                if (!cert.Thumbprint.Equals(leaf.Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    chain.Add(cert);
                }
            }

            return (leaf, chain);
        }

        var collection = new X509Certificate2Collection();
        collection.ImportFromPemFile(certPath);

        if (collection.Count == 0)
        {
            throw new InvalidOperationException($"Certificate file '{certPath}' does not contain any valid certificates.");
        }

        var hasExplicitKey = !string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath.Trim());
        var effectiveKeyPath = hasExplicitKey ? keyPath.Trim() : certPath;

        if (!hasExplicitKey)
        {
            var pemContent = File.ReadAllText(certPath);
            if (!pemContent.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Certificate file '{certPath}' does not contain a private key and no private key file was provided.");
            }
        }

        X509Certificate2 leafWithKey;
        if (!string.IsNullOrEmpty(password))
        {
            try
            {
                leafWithKey = X509Certificate2.CreateFromEncryptedPemFile(certPath, password, effectiveKeyPath);
            }
            catch
            {
                leafWithKey = X509Certificate2.CreateFromPemFile(certPath, effectiveKeyPath);
            }
        }
        else
        {
            leafWithKey = X509Certificate2.CreateFromPemFile(certPath, effectiveKeyPath);
        }

        var fullCollection = new X509Certificate2Collection { leafWithKey };
        foreach (var cert in collection)
        {
            if (!cert.Thumbprint.Equals(leafWithKey.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                fullCollection.Add(cert);
            }
        }

        var pfxBytes = fullCollection.Export(X509ContentType.Pkcs12, string.Empty);
        var loadedPemCollection = X509CertificateLoader.LoadPkcs12Collection(
            pfxBytes,
            string.Empty,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

        X509Certificate2 loadedLeaf = null;
        foreach (var cert in loadedPemCollection)
        {
            if (cert.HasPrivateKey)
            {
                loadedLeaf = cert;
                break;
            }
        }

        loadedLeaf ??= loadedPemCollection[0];

        var intermediateChain = new X509Certificate2Collection();
        foreach (var cert in loadedPemCollection)
        {
            if (!cert.Thumbprint.Equals(loadedLeaf.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                intermediateChain.Add(cert);
            }
        }

        if (intermediateChain.Count == 0 && fullCollection.Count > 1)
        {
            foreach (var cert in fullCollection)
            {
                if (!cert.Thumbprint.Equals(loadedLeaf.Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    intermediateChain.Add(cert);
                }
            }
        }

        return (loadedLeaf, intermediateChain);
    }

    private X509Certificate2 GetOrCreateSelfSignedCertificate(IConfigFileProvider config)
    {
        var cachePath = Path.Combine(_appFolderInfo.AppDataFolder, "seedarr-selfsigned.pfx");
        var passwordPath = Path.Combine(_appFolderInfo.AppDataFolder, "seedarr-selfsigned.pwd");

        if (_cachedSelfSignedCert != null &&
            string.Equals(_cachedSelfSignedPath, cachePath, StringComparison.Ordinal) &&
            File.Exists(cachePath))
        {
            var currentWriteTime = File.GetLastWriteTimeUtc(cachePath);
            if (currentWriteTime <= _cachedSelfSignedLastWriteTimeUtc &&
                DateTime.UtcNow < _cachedSelfSignedCert.NotAfter.ToUniversalTime().AddDays(-30))
            {
                return _cachedSelfSignedCert;
            }
        }

        var pfxPassword = GetOrGenerateSelfSignedPassword(config, passwordPath);

        if (File.Exists(cachePath))
        {
            try
            {
                var cached = X509CertificateLoader.LoadPkcs12FromFile(
                    cachePath,
                    pfxPassword,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

                if (DateTime.UtcNow < cached.NotAfter.ToUniversalTime().AddDays(-30))
                {
                    Logger.Info("Using existing cached self-signed SSL certificate: {0} (Expires: {1:yyyy-MM-dd})", cached.Subject, cached.NotAfter);
                    var oldCert = _cachedSelfSignedCert;
                    _cachedSelfSignedCert = cached;
                    _cachedSelfSignedPath = cachePath;
                    _cachedSelfSignedLastWriteTimeUtc = File.GetLastWriteTimeUtc(cachePath);

                    if (oldCert != null && !ReferenceEquals(oldCert, cached))
                    {
                        SafeDisposeCertificate(oldCert);
                    }

                    return cached;
                }

                cached.Dispose();
                Logger.Info("Cached self-signed SSL certificate is expired or expiring soon. Regenerating.");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to load cached self-signed certificate from '{0}'. Regenerating.", cachePath);
            }
        }

        var generated = GenerateAndSaveSelfSignedCertificate(config, cachePath, pfxPassword);
        var prevCert = _cachedSelfSignedCert;
        _cachedSelfSignedCert = generated;
        _cachedSelfSignedPath = cachePath;
        _cachedSelfSignedLastWriteTimeUtc = File.GetLastWriteTimeUtc(cachePath);

        if (prevCert != null && !ReferenceEquals(prevCert, generated))
        {
            SafeDisposeCertificate(prevCert);
        }

        return generated;
    }

    private static string GetOrGenerateSelfSignedPassword(IConfigFileProvider config, string passwordPath)
    {
        if (!string.IsNullOrWhiteSpace(config.SslCertPassword))
        {
            return config.SslCertPassword;
        }

        if (File.Exists(passwordPath))
        {
            try
            {
                var storedPassword = File.ReadAllText(passwordPath).Trim();
                if (!string.IsNullOrEmpty(storedPassword))
                {
                    return storedPassword;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not read self-signed password file at '{0}'. Generating a new one.", passwordPath);
            }
        }

        var randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var generatedPassword = Convert.ToBase64String(randomBytes);

        try
        {
            var dir = Path.GetDirectoryName(passwordPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(passwordPath, generatedPassword);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Could not persist self-signed SSL password to disk at '{0}'", passwordPath);
        }

        return generatedPassword;
    }

    private static X509Certificate2 GenerateAndSaveSelfSignedCertificate(IConfigFileProvider config, string cachePath, string pfxPassword)
    {
        Logger.Info("Generating new self-signed RSA-2048 SSL certificate for Seedarr...");

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=Seedarr, O=Seedarr, OU=BitTorrent Seeding Simulator",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        try
        {
            sanBuilder.AddDnsName("seedarr");
        }
        catch
        {
            // Ignore invalid DNS name characters
        }

        try
        {
            sanBuilder.AddDnsName("host.docker.internal");
        }
        catch
        {
            // Ignore invalid DNS name characters
        }

        try
        {
            sanBuilder.AddDnsName(Environment.MachineName);
        }
        catch
        {
            // Ignore invalid hostname characters
        }

        sanBuilder.AddIpAddress(IPAddress.Loopback);
        sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);

        var addedIps = new HashSet<IPAddress>
        {
            IPAddress.Loopback,
            IPAddress.IPv6Loopback,
        };

        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces.Where(i => i.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up))
            {
                try
                {
                    foreach (var ipInfo in ni.GetIPProperties().UnicastAddresses)
                    {
                        var ip = ipInfo.Address;
                        if (!IPAddress.IsLoopback(ip) && !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal && addedIps.Add(ip))
                        {
                            sanBuilder.AddIpAddress(ip);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Could not read IP properties for interface '{0}'", ni.Name);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Could not discover local network interfaces for SSL SAN extension");
        }

        if (!string.IsNullOrWhiteSpace(config.BindAddress) && config.BindAddress != "*")
        {
            if (IPAddress.TryParse(config.BindAddress, out var bindIp))
            {
                if (!IPAddress.Any.Equals(bindIp) &&
                    !IPAddress.IPv6Any.Equals(bindIp) &&
                    !IPAddress.Loopback.Equals(bindIp) &&
                    !IPAddress.IPv6Loopback.Equals(bindIp) &&
                    addedIps.Add(bindIp))
                {
                    sanBuilder.AddIpAddress(bindIp);
                }
            }
            else
            {
                try
                {
                    sanBuilder.AddDnsName(config.BindAddress);
                }
                catch
                {
                    // Ignore invalid DNS name characters
                }
            }
        }

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        req.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(
                req.PublicKey,
                critical: false));

        req.CertificateExtensions.Add(sanBuilder.Build());

        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddDays(397);

        using var cert = req.CreateSelfSigned(notBefore, notAfter);
        var pfxBytes = cert.Export(X509ContentType.Pfx, pfxPassword);

        try
        {
            var dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(cachePath, pfxBytes);
            Logger.Info("Saved generated self-signed SSL certificate to '{0}'", cachePath);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Could not persist self-signed SSL certificate to disk at '{0}'", cachePath);
        }

        return X509CertificateLoader.LoadPkcs12(
            pfxBytes,
            pfxPassword,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    private static void SafeDisposeCertificate(X509Certificate2 cert)
    {
        if (cert != null)
        {
            try
            {
                cert.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error disposing certificate");
            }
        }
    }

    private static void SafeDisposeChain(X509Certificate2Collection chain)
    {
        if (chain != null)
        {
            foreach (var cert in chain)
            {
                SafeDisposeCertificate(cert);
            }

            chain.Clear();
        }
    }

    private sealed class DummySslConfig : IConfigFileProvider
    {
        public DummySslConfig(string bindAddress)
        {
            BindAddress = string.IsNullOrWhiteSpace(bindAddress) ? "*" : bindAddress;
        }

        public string BindAddress { get; }

        public int Port => 9898;

        public bool EnableSsl => true;

        public int SslPort => 9899;

        public string SslCertPath => string.Empty;

        public string SslKeyPath => string.Empty;

        public string SslCertPassword => string.Empty;

        public bool RedirectHttpToHttps => false;

        public string ApiKey => string.Empty;

        public bool AuthenticationEnabled => false;

        public bool TerminalAccessEnabled => true;

        public string LogLevel => "info";

        public string UrlBase => string.Empty;

        public string PostgresHost => string.Empty;

        public int PostgresPort => 5432;

        public string PostgresMainDb => string.Empty;

        public string PostgresUser => string.Empty;

        public string PostgresPassword => string.Empty;

        public string BindInterface => string.Empty;

        public bool EnableVpnKillSwitch => false;

        public int VpnStabilizationDelaySeconds => 8;

        public bool ForceProxy => false;

        public bool AnonymousMode => false;

        public bool EnableIPv6 => true;

        public int MaxConnectionsPerIp => 5;

        public int MaximumHalfOpenConnections => 50;

        public int PeerDscp => 0;

        public int PeerTos => 0;

        public void SaveConfigDictionary(Dictionary<string, object> configValues)
        {
        }
    }
}
