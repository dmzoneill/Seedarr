using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Notifications.CustomScript;

[TestFixture]
public class CustomScriptServiceTest
{
    [Test]
    public void SanitizeEnvValue_should_return_empty_string_for_null_or_empty()
    {
        Assert.That(CustomScriptService.SanitizeEnvValue(null), Is.EqualTo(string.Empty));
        Assert.That(CustomScriptService.SanitizeEnvValue(string.Empty), Is.EqualTo(string.Empty));
    }

    [Test]
    public void SanitizeEnvValue_should_strip_null_bytes()
    {
        var input = "Torrent\0Name\0WithNulls";
        var result = CustomScriptService.SanitizeEnvValue(input);

        Assert.That(result, Is.EqualTo("TorrentNameWithNulls"));
        Assert.That(result.Contains('\0'), Is.False);
    }

    [Test]
    public void SanitizeEnvValue_should_strip_carriage_returns_and_replace_newlines_with_space()
    {
        var input = "Line1\r\nLine2\nLine3\rLine4";
        var result = CustomScriptService.SanitizeEnvValue(input);

        Assert.That(result, Is.EqualTo("Line1 Line2 Line3Line4"));
        Assert.That(result.Contains('\r'), Is.False);
        Assert.That(result.Contains('\n'), Is.False);
    }

    [Test]
    public void SanitizeEnvValue_should_truncate_to_max_length()
    {
        var input = new string('a', 2000);
        var result = CustomScriptService.SanitizeEnvValue(input, 1024);

        Assert.That(result.Length, Is.EqualTo(1024));
        Assert.That(result, Is.EqualTo(new string('a', 1024)));
    }

    [Test]
    public void SanitizeEnvValue_should_not_truncate_when_max_length_is_negative()
    {
        var input = new string('b', 2000);
        var result = CustomScriptService.SanitizeEnvValue(input, -1);

        Assert.That(result.Length, Is.EqualTo(2000));
    }

    [Test]
    public void SanitizeEnvKey_should_return_empty_string_for_null_or_empty()
    {
        Assert.That(CustomScriptService.SanitizeEnvKey(null), Is.EqualTo(string.Empty));
        Assert.That(CustomScriptService.SanitizeEnvKey(string.Empty), Is.EqualTo(string.Empty));
    }

    [Test]
    public void SanitizeEnvKey_should_strip_null_bytes_carriage_returns_and_newlines()
    {
        var input = "MY\0KEY\r\n_VAR\n";
        var result = CustomScriptService.SanitizeEnvKey(input);

        Assert.That(result, Is.EqualTo("MYKEY_VAR"));
        Assert.That(result.Contains('\0'), Is.False);
        Assert.That(result.Contains('\r'), Is.False);
        Assert.That(result.Contains('\n'), Is.False);
    }

    [Test]
    public void BuildEnvironmentVariables_should_sanitize_values_and_truncate_overview()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Inception\0 2010\r\n1080p",
            SavePath = "/downloads/Inception\0",
            Category = "movies\n",
            InfoHash = "abcdef1234567890abcdef1234567890abcdef12",
            TotalSize = 1073741824,
            Ratio = 1.5,
            Status = TorrentStatus.Seeding,
            TagIds = new List<int> { 1, 2 }
        };

        var longOverview = "A thief who steals corporate secrets through the use of dream-sharing technology. " + new string('x', 2000);
        var meta = new TorrentMediaMetadata
        {
            TorrentId = 42,
            Title = "Inception\0",
            Overview = longOverview,
            Year = 2010,
            Rating = 8.8,
            Genres = "Action\r\nSci-Fi"
        };

        var env = CustomScriptService.BuildEnvironmentVariables("OnDownloadComplete", torrent, meta);

        Assert.That(env["SEEDARR_TORRENT_NAME"], Is.EqualTo("Inception 2010 1080p"));
        Assert.That(env["TORRENT_NAME"], Is.EqualTo("Inception 2010 1080p"));
        Assert.That(env["TORRENT_PATH"], Is.EqualTo("/downloads/Inception"));
        Assert.That(env["SEEDARR_TORRENT_CATEGORY"], Is.EqualTo("movies "));
        Assert.That(env["SEEDARR_MEDIA_OVERVIEW"].Length, Is.EqualTo(1024));
        Assert.That(env["LEECHARR_MEDIA_OVERVIEW"].Length, Is.EqualTo(1024));
        Assert.That(env["SEEDARR_MEDIA_GENRES"], Is.EqualTo("Action Sci-Fi"));

        foreach (var kvp in env)
        {
            Assert.That(kvp.Key.Contains('\0'), Is.False, $"Key {kvp.Key} contains null char");
            Assert.That(kvp.Key.Contains('\r'), Is.False, $"Key {kvp.Key} contains carriage return");
            Assert.That(kvp.Key.Contains('\n'), Is.False, $"Key {kvp.Key} contains newline");

            Assert.That(kvp.Value.Contains('\0'), Is.False, $"Value for {kvp.Key} contains null char");
            Assert.That(kvp.Value.Contains('\r'), Is.False, $"Value for {kvp.Key} contains carriage return");
            Assert.That(kvp.Value.Contains('\n'), Is.False, $"Value for {kvp.Key} contains newline");
        }
    }

    [Test]
    public void BuildEnvironmentVariables_should_handle_null_torrent_and_meta()
    {
        var env = CustomScriptService.BuildEnvironmentVariables("TestEvent", null, null);

        Assert.That(env["SEEDARR_EVENT_TYPE"], Is.EqualTo("TestEvent"));
        Assert.That(env["SEEDARR_EVENTTYPE"], Is.EqualTo("TestEvent"));
        Assert.That(env["LEECHARR_EVENT_TYPE"], Is.EqualTo("TestEvent"));
        Assert.That(env["LEECHARR_EVENTTYPE"], Is.EqualTo("TestEvent"));
        Assert.That(env.ContainsKey("TORRENT_NAME"), Is.False);
    }

    [TestCase(2, 5)]
    [TestCase(4, 5)]
    [TestCase(5, 5)]
    [TestCase(60, 60)]
    [TestCase(3600, 3600)]
    [TestCase(3601, 3600)]
    [TestCase(100000, 3600)]
    [TestCase(0, 60)]
    [TestCase(-10, 60)]
    public void Constructor_should_clamp_timeout_between_5_and_3600(int configuredTimeout, int expectedTimeoutSec)
    {
        var configMock = Substitute.For<IConfigService>();
        configMock.CustomScriptTimeoutSeconds.Returns(configuredTimeout);

        var service = new CustomScriptService(configService: configMock);

        Assert.That(service.ScriptTimeout, Is.EqualTo(TimeSpan.FromSeconds(expectedTimeoutSec)));
    }

    [Test]
    public void ParseSettings_should_parse_json_and_plain_settings()
    {
        var (plainPath, plainArgs) = CustomScriptService.ParseSettings("/usr/bin/script.sh");
        Assert.That(plainPath, Is.EqualTo("/usr/bin/script.sh"));
        Assert.That(plainArgs, Is.Null);

        var (jsonPath, jsonArgs) = CustomScriptService.ParseSettings("{\"path\": \"/usr/bin/script.sh\", \"arguments\": \"--foo bar\"}");
        Assert.That(jsonPath, Is.EqualTo("/usr/bin/script.sh"));
        Assert.That(jsonArgs, Is.EqualTo("--foo bar"));
    }

    [Test]
    public async Task TestScriptAsync_should_return_success_and_stdout_for_valid_script()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "test.bat" : "test.sh");
            var scriptContent = isWindows
                ? "@echo off\necho Hello from Seedarr script"
                : "#!/bin/sh\necho \"Hello from Seedarr script\"\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            var service = new CustomScriptService();
            var result = await service.TestScriptAsync(scriptFile);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.True);
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.Stdout, Does.Contain("Hello from Seedarr script"));
            Assert.That(result.ExecutionTimeMs, Is.GreaterThan(0));
            Assert.That(result.TimedOut, Is.False);
            Assert.That(result.ResolvedInterpreter, Is.Not.Empty);
            Assert.That(result.WorkingDirectory, Is.Not.Empty);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TestScriptAsync_should_capture_stderr_and_failure_on_non_zero_exit_code()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "fail.bat" : "fail.sh");
            var scriptContent = isWindows
                ? "@echo off\n>&2 echo Test failure message\nexit /b 42"
                : "#!/bin/sh\necho \"Test failure message\" >&2\nexit 42\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            var service = new CustomScriptService();
            var result = await service.TestScriptAsync(scriptFile);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(42));
            Assert.That(result.Stderr, Does.Contain("Test failure message"));
            Assert.That(result.TimedOut, Is.False);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TestScriptAsync_should_return_graceful_error_for_missing_file()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "seedarr-nonexistent-" + Guid.NewGuid().ToString("N") + ".sh");

        var service = new CustomScriptService();
        var result = await service.TestScriptAsync(missingPath);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ExitCode, Is.Not.EqualTo(0));
        Assert.That(result.Stderr, Does.Contain("does not exist"));
        Assert.That(result.TimedOut, Is.False);
    }
}
