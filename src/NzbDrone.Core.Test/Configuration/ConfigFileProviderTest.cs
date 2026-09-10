using System;
using System.IO;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Test.Configuration;

[TestFixture]
public class ConfigFileProviderTest
{
    private ConfigFileProvider _subject;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _subject = new ConfigFileProvider(new TestAppFolderInfo(_tempDir));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private sealed class TestAppFolderInfo : IAppFolderInfo
    {
        public TestAppFolderInfo(string appDataFolder) => AppDataFolder = appDataFolder;
        public string AppDataFolder { get; }
        public string StartUpFolder => AppDataFolder;
    }

    [Test]
    public void Constructor_should_generate_api_key_when_empty()
    {
        Assert.That(_subject.ApiKey, Is.Not.Empty);
    }

    [Test]
    public void ApiKey_should_be_32_characters()
    {
        Assert.That(_subject.ApiKey.Length, Is.EqualTo(32));
    }

    [Test]
    public void ApiKey_should_be_valid_guid_format()
    {
        Assert.That(Guid.TryParse(_subject.ApiKey, out _), Is.True);
    }

    [Test]
    public void ApiKey_should_persist_across_instances()
    {
        var firstKey = _subject.ApiKey;
        var second = new ConfigFileProvider(new TestAppFolderInfo(_tempDir));
        Assert.That(second.ApiKey, Is.EqualTo(firstKey));
    }

    [Test]
    public void BindAddress_should_default_to_wildcard()
    {
        Assert.That(_subject.BindAddress, Is.EqualTo("*"));
    }

    [Test]
    public void Port_should_default_to_9898()
    {
        Assert.That(_subject.Port, Is.EqualTo(9898));
    }

    [Test]
    public void EnableSsl_should_default_to_false()
    {
        Assert.That(_subject.EnableSsl, Is.False);
    }

    [Test]
    public void SslPort_should_default_to_9899()
    {
        Assert.That(_subject.SslPort, Is.EqualTo(9899));
    }

    [Test]
    public void SslCertPath_should_default_to_empty()
    {
        Assert.That(_subject.SslCertPath, Is.EqualTo(string.Empty));
    }

    [Test]
    public void SslKeyPath_should_default_to_empty()
    {
        Assert.That(_subject.SslKeyPath, Is.EqualTo(string.Empty));
    }

    [Test]
    public void SslCertPassword_should_default_to_empty()
    {
        Assert.That(_subject.SslCertPassword, Is.EqualTo(string.Empty));
    }

    [Test]
    public void RedirectHttpToHttps_should_default_to_false()
    {
        Assert.That(_subject.RedirectHttpToHttps, Is.False);
    }

    [Test]
    public void AuthenticationEnabled_should_default_to_false()
    {
        Assert.That(_subject.AuthenticationEnabled, Is.False);
    }

    [Test]
    public void LogLevel_should_default_to_info()
    {
        Assert.That(_subject.LogLevel, Is.EqualTo("info"));
    }

    [Test]
    public void UrlBase_should_default_to_empty_string()
    {
        Assert.That(_subject.UrlBase, Is.EqualTo(string.Empty));
    }

    [Test]
    public void PostgresPort_should_default_to_5432()
    {
        Assert.That(_subject.PostgresPort, Is.EqualTo(5432));
    }

    [Test]
    public void PostgresHost_should_default_to_empty_string()
    {
        Assert.That(_subject.PostgresHost, Is.EqualTo(string.Empty));
    }

    [Test]
    public void PostgresMainDb_should_default_to_empty_string()
    {
        Assert.That(_subject.PostgresMainDb, Is.EqualTo(string.Empty));
    }

    [Test]
    public void SaveConfigDictionary_should_update_values()
    {
        _subject.SaveConfigDictionary(new System.Collections.Generic.Dictionary<string, object>
        {
            { "Port", 9000 },
            { "LogLevel", "debug" },
            { "EnableSsl", true },
            { "SslPort", 9900 },
            { "SslCertPath", "/path/to/cert.pfx" },
            { "RedirectHttpToHttps", true }
        });

        Assert.That(_subject.Port, Is.EqualTo(9000));
        Assert.That(_subject.LogLevel, Is.EqualTo("debug"));
        Assert.That(_subject.EnableSsl, Is.True);
        Assert.That(_subject.SslPort, Is.EqualTo(9900));
        Assert.That(_subject.SslCertPath, Is.EqualTo("/path/to/cert.pfx"));
        Assert.That(_subject.RedirectHttpToHttps, Is.True);
    }

    [Test]
    public void Environment_variable_should_override_config_file()
    {
        Environment.SetEnvironmentVariable("SEEDARR__SSL_PORT", "9911");
        try
        {
            Assert.That(_subject.SslPort, Is.EqualTo(9911));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SEEDARR__SSL_PORT", null);
        }
    }

    [Test]
    public void Concurrent_reads_and_writes_should_not_throw()
    {
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        const int iterations = 50;

        System.Threading.Tasks.Parallel.For(0, iterations, i =>
        {
            try
            {
                _subject.SaveConfigDictionary(new System.Collections.Generic.Dictionary<string, object>
                {
                    { $"CustomKey_{i % 5}", $"Value_{i}" },
                    { "Port", 9000 + (i % 10) }
                });

                _ = _subject.Port;
                _ = _subject.ApiKey;
                _ = _subject.BindAddress;
                _ = _subject.LogLevel;
                _ = _subject.EnableSsl;
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.That(exceptions, Is.Empty);
    }
}
