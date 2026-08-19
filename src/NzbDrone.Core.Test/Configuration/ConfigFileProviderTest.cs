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
}
