using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Processes;
using NzbDrone.Core.Tags;
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

    [Test]
    public async Task ExecuteScriptAsync_with_null_bytes_in_torrent_fields_does_not_throw_ArgumentException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "echo.bat" : "echo.sh");
            var scriptContent = isWindows
                ? "@echo off\necho done\n"
                : "#!/bin/sh\necho done\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            var torrent = new Torrent
            {
                Id = 1,
                Name = "Bad\0Name",
                SavePath = tempDir,
                SourcePath = tempDir,
                InfoHash = "12345\067890",
            };

            var service = new CustomScriptService();
            var result = await service.ExecuteScriptAsync(scriptFile, torrent, "Download");

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteScriptAsync_should_terminate_timed_out_process_tree()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "hang.bat" : "hang.sh");
            var scriptContent = isWindows
                ? "@echo off\nping -n 60 127.0.0.1 >nul\n"
                : "#!/bin/sh\nsleep 60\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            var service = new CustomScriptService(scriptTimeout: TimeSpan.FromMilliseconds(500));
            var result = await service.ExecuteScriptAsync(scriptFile, null, "Test");

            Assert.That(result, Is.False);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TestScriptAsync_should_terminate_timed_out_process_tree_and_return_timeout_result()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "hang.bat" : "hang.sh");
            var scriptContent = isWindows
                ? "@echo off\nping -n 60 127.0.0.1 >nul\n"
                : "#!/bin/sh\nsleep 60\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            var service = new CustomScriptService(scriptTimeout: TimeSpan.FromMilliseconds(500));
            var result = await service.TestScriptAsync(scriptFile);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.TimedOut, Is.True);
            Assert.That(result.Success, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(-1));
            Assert.That(result.Stderr, Does.Contain("timed out"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void CleanScriptPath_should_trim_whitespace_and_surrounding_quotes()
    {
        Assert.That(CustomScriptService.CleanScriptPath(null), Is.EqualTo(string.Empty));
        Assert.That(CustomScriptService.CleanScriptPath(string.Empty), Is.EqualTo(string.Empty));
        Assert.That(CustomScriptService.CleanScriptPath("   "), Is.EqualTo(string.Empty));
        Assert.That(CustomScriptService.CleanScriptPath("/scripts/done.sh"), Is.EqualTo("/scripts/done.sh"));
        Assert.That(CustomScriptService.CleanScriptPath("\"/scripts/done.sh\""), Is.EqualTo("/scripts/done.sh"));
        Assert.That(CustomScriptService.CleanScriptPath("'/scripts/done.sh'"), Is.EqualTo("/scripts/done.sh"));
        Assert.That(CustomScriptService.CleanScriptPath("  \"/scripts/done.sh\"  "), Is.EqualTo("/scripts/done.sh"));
        Assert.That(CustomScriptService.CleanScriptPath("\"'/scripts/done.sh'\""), Is.EqualTo("/scripts/done.sh"));
        Assert.That(CustomScriptService.CleanScriptPath("\"C:\\Scripts\\run.bat\""), Is.EqualTo("C:\\Scripts\\run.bat"));
    }

    [Test]
    public void BuildEnvironmentVariables_should_map_tag_ids_to_labels_using_tag_service()
    {
        var torrent = new Torrent
        {
            Id = 101,
            Name = "Test Torrent",
            TotalSize = 5368709120,
            TagIds = new List<int> { 1, 3 }
        };

        var tagService = Substitute.For<ITagService>();
        tagService.GetLabelsForTagIds(torrent.TagIds).Returns(new List<string> { "4k", "verified" });

        var env = CustomScriptService.BuildEnvironmentVariables("OnDownloadComplete", torrent, null, tagService);

        Assert.That(env["SEEDARR_TORRENT_TAGS"], Is.EqualTo("4k,verified"));
        Assert.That(env["LEECHARR_TORRENT_TAGS"], Is.EqualTo("4k,verified"));
        Assert.That(env["TORRENT_TAGS"], Is.EqualTo("4k,verified"));
        Assert.That(env["SEEDARR_TORRENT_SIZE_BYTES"], Is.EqualTo("5368709120"));
        Assert.That(env["LEECHARR_TORRENT_SIZE_BYTES"], Is.EqualTo("5368709120"));
        Assert.That(env["SEEDARR_TORRENT_SIZE"], Is.EqualTo("5368709120"));
        Assert.That(env["LEECHARR_TORRENT_SIZE"], Is.EqualTo("5368709120"));
    }

    [Test]
    public void BuildEnvironmentVariables_should_fallback_to_tag_ids_when_tag_service_is_null()
    {
        var torrent = new Torrent
        {
            Id = 102,
            Name = "Fallback Torrent",
            TotalSize = 1048576,
            TagIds = new List<int> { 7, 9 }
        };

        var env = CustomScriptService.BuildEnvironmentVariables("OnDownloadComplete", torrent, null, null);

        Assert.That(env["SEEDARR_TORRENT_TAGS"], Is.EqualTo("7,9"));
        Assert.That(env["LEECHARR_TORRENT_TAGS"], Is.EqualTo("7,9"));
        Assert.That(env["TORRENT_TAGS"], Is.EqualTo("7,9"));
    }

    [Test]
    public async Task ExecuteScriptAsync_should_succeed_with_quoted_script_path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "success.bat" : "success.sh");
            var scriptContent = isWindows
                ? "@echo off\r\nexit /b 0\r\n"
                : "#!/bin/sh\nexit 0\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            if (!isWindows)
            {
                File.SetUnixFileMode(scriptFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var quotedPath = $"\"{scriptFile}\"";
            var service = new CustomScriptService();
            var result = await service.ExecuteScriptAsync(quotedPath, null, "Test");

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteScriptAsync_should_throttle_concurrent_executions_to_configured_limit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "sleep.bat" : "sleep.sh");
            var scriptContent = isWindows
                ? "@echo off\r\nping 127.0.0.1 -n 2 > nul\r\nexit /b 0\r\n"
                : "#!/bin/sh\nsleep 0.3\nexit 0\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            if (!isWindows)
            {
                File.SetUnixFileMode(scriptFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            using var supervisor = new ProcessSupervisor();
            using var service = new CustomScriptService(processSupervisor: supervisor, maxConcurrentScripts: 2);

            var maxObservedProcesses = 0;
            var lockObj = new object();
            var cts = new CancellationTokenSource();

            var monitorTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var count = supervisor.ActiveProcessIds.Count;
                    lock (lockObj)
                    {
                        if (count > maxObservedProcesses)
                        {
                            maxObservedProcesses = count;
                        }
                    }

                    try
                    {
                        await Task.Delay(10, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });

            var tasks = new List<Task<bool>>();
            for (var i = 0; i < 4; i++)
            {
                tasks.Add(service.ExecuteScriptAsync(scriptFile, null, "Test"));
            }

            var results = await Task.WhenAll(tasks);
            await cts.CancelAsync();
            await monitorTask;

            Assert.That(results, Has.All.True);
            Assert.That(maxObservedProcesses, Is.GreaterThan(0));
            Assert.That(maxObservedProcesses, Is.LessThanOrEqualTo(2), "Concurrency throttle should limit active processes to 2");
            Assert.That(supervisor.ActiveProcessIds, Is.Empty, "All processes should be unregistered after completion");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteScriptAsync_should_abort_immediately_when_supervisor_is_shutting_down()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "test.bat" : "test.sh");
            var scriptContent = isWindows
                ? "@echo off\r\nexit /b 0\r\n"
                : "#!/bin/sh\nexit 0\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            if (!isWindows)
            {
                File.SetUnixFileMode(scriptFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            using var supervisor = new ProcessSupervisor();
            supervisor.Handle(new ApplicationShutdownRequested());

            using var service = new CustomScriptService(processSupervisor: supervisor);
            var result = await service.ExecuteScriptAsync(scriptFile, null, "Test");

            Assert.That(result, Is.False, "ExecuteScriptAsync should return false when application is shutting down");
            Assert.That(supervisor.ActiveProcessIds, Is.Empty);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ExecuteScriptAsync_should_register_and_unregister_child_process_with_supervisor()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "test.bat" : "test.sh");
            var scriptContent = isWindows
                ? "@echo off\r\nexit /b 0\r\n"
                : "#!/bin/sh\nexit 0\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            if (!isWindows)
            {
                File.SetUnixFileMode(scriptFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var mockSupervisor = Substitute.For<ISidecarProcessSupervisor>();
            using var service = new CustomScriptService(processSupervisor: mockSupervisor);

            var result = await service.ExecuteScriptAsync(scriptFile, null, "Test");

            Assert.That(result, Is.True);
            mockSupervisor.Received(1).RegisterProcess(Arg.Any<Process>());
            mockSupervisor.Received(1).UnregisterProcess(Arg.Any<int>());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void SplitArguments_should_split_plain_arguments_and_handle_empty()
    {
        Assert.That(CustomScriptService.SplitArguments(null), Is.Empty);
        Assert.That(CustomScriptService.SplitArguments(string.Empty), Is.Empty);
        Assert.That(CustomScriptService.SplitArguments("    "), Is.Empty);

        var result = CustomScriptService.SplitArguments("--foo bar -v 1");
        Assert.That(result, Is.EqualTo(new[] { "--foo", "bar", "-v", "1" }));
    }

    [Test]
    public void SplitArguments_should_handle_quotes_spaces_and_empty_tokens()
    {
        var input = "--name \"My Cool Movie\" --category 'sci-fi & action' --flag \"\"";
        var result = CustomScriptService.SplitArguments(input);

        Assert.That(result, Is.EqualTo(new[] { "--name", "My Cool Movie", "--category", "sci-fi & action", "--flag", string.Empty }));
    }

    [Test]
    public void SplitArguments_should_preserve_shell_metacharacters_as_literal_tokens()
    {
        var input = "--filter \"a&b|c;d$e\" --redirect \">out.txt\" --pipe \"x|y\"";
        var result = CustomScriptService.SplitArguments(input);

        Assert.That(result, Is.EqualTo(new[] { "--filter", "a&b|c;d$e", "--redirect", ">out.txt", "--pipe", "x|y" }));
    }

    [Test]
    public void BuildProcessStartInfo_should_populate_ArgumentList_for_all_interpreters_on_windows()
    {
        var py = CustomScriptService.BuildProcessStartInfo("C:\\scripts\\test.py", "--name \"My Test\"", isWindows: true);
        Assert.That(py.FileName, Is.EqualTo("python"));
        Assert.That(py.ArgumentList, Is.EqualTo(new[] { "C:\\scripts\\test.py", "--name", "My Test" }));
        Assert.That(py.Arguments, Is.Empty);

        var ps = CustomScriptService.BuildProcessStartInfo("C:\\scripts\\test.ps1", "--foo bar", isWindows: true);
        Assert.That(ps.FileName, Is.EqualTo("powershell.exe"));
        Assert.That(ps.ArgumentList, Is.EqualTo(new[] { "-ExecutionPolicy", "Bypass", "-File", "C:\\scripts\\test.ps1", "--foo", "bar" }));
        Assert.That(ps.Arguments, Is.Empty);

        var rb = CustomScriptService.BuildProcessStartInfo("C:\\scripts\\test.rb", "arg1", isWindows: true);
        Assert.That(rb.FileName, Is.EqualTo("ruby"));
        Assert.That(rb.ArgumentList, Is.EqualTo(new[] { "C:\\scripts\\test.rb", "arg1" }));
        Assert.That(rb.Arguments, Is.Empty);

        var js = CustomScriptService.BuildProcessStartInfo("C:\\scripts\\test.js", "arg1", isWindows: true);
        Assert.That(js.FileName, Is.EqualTo("node"));
        Assert.That(js.ArgumentList, Is.EqualTo(new[] { "C:\\scripts\\test.js", "arg1" }));
        Assert.That(js.Arguments, Is.Empty);

        var exe = CustomScriptService.BuildProcessStartInfo("C:\\scripts\\tool.exe", "--run now", isWindows: true);
        Assert.That(exe.FileName, Is.EqualTo("C:\\scripts\\tool.exe"));
        Assert.That(exe.ArgumentList, Is.EqualTo(new[] { "--run", "now" }));
        Assert.That(exe.Arguments, Is.Empty);
    }

    [Test]
    public void BuildProcessStartInfo_should_populate_ArgumentList_for_all_interpreters_on_non_windows()
    {
        var sh = CustomScriptService.BuildProcessStartInfo("/scripts/test.sh", "--foo \"bar baz\"", isWindows: false);
        Assert.That(sh.FileName, Is.EqualTo("/bin/sh"));
        Assert.That(sh.ArgumentList, Is.EqualTo(new[] { "/scripts/test.sh", "--foo", "bar baz" }));
        Assert.That(sh.Arguments, Is.Empty);

        var bash = CustomScriptService.BuildProcessStartInfo("/scripts/test.bash", "arg1", isWindows: false);
        Assert.That(bash.FileName, Is.EqualTo("/bin/bash"));
        Assert.That(bash.ArgumentList, Is.EqualTo(new[] { "/scripts/test.bash", "arg1" }));
        Assert.That(bash.Arguments, Is.Empty);

        var py = CustomScriptService.BuildProcessStartInfo("/scripts/test.py", "arg1", isWindows: false);
        Assert.That(py.FileName, Is.EqualTo("python3"));
        Assert.That(py.ArgumentList, Is.EqualTo(new[] { "/scripts/test.py", "arg1" }));
        Assert.That(py.Arguments, Is.Empty);

        var ps = CustomScriptService.BuildProcessStartInfo("/scripts/test.ps1", "arg1", isWindows: false);
        Assert.That(ps.FileName, Is.EqualTo("pwsh"));
        Assert.That(ps.ArgumentList, Is.EqualTo(new[] { "-File", "/scripts/test.ps1", "arg1" }));
        Assert.That(ps.Arguments, Is.Empty);

        var rb = CustomScriptService.BuildProcessStartInfo("/scripts/test.rb", "arg1", isWindows: false);
        Assert.That(rb.FileName, Is.EqualTo("ruby"));
        Assert.That(rb.ArgumentList, Is.EqualTo(new[] { "/scripts/test.rb", "arg1" }));
        Assert.That(rb.Arguments, Is.Empty);

        var js = CustomScriptService.BuildProcessStartInfo("/scripts/test.js", "arg1", isWindows: false);
        Assert.That(js.FileName, Is.EqualTo("node"));
        Assert.That(js.ArgumentList, Is.EqualTo(new[] { "/scripts/test.js", "arg1" }));
        Assert.That(js.Arguments, Is.Empty);

        var bin = CustomScriptService.BuildProcessStartInfo("/usr/local/bin/custom", "--flag", isWindows: false);
        Assert.That(bin.FileName, Is.EqualTo("/usr/local/bin/custom"));
        Assert.That(bin.ArgumentList, Is.EqualTo(new[] { "--flag" }));
        Assert.That(bin.Arguments, Is.Empty);
    }

    [Test]
    public void Windows_cmd_batch_file_invocation_and_resolution_should_prevent_quote_stripping()
    {
        var (cmdWithArgs, argsString) = CustomScriptService.ResolveInterpreter("C:\\Program Files\\Seedarr\\script.bat", "\"arg with space\"", isWindows: true);
        Assert.That(cmdWithArgs, Is.EqualTo("cmd.exe"));
        Assert.That(argsString, Is.EqualTo("/c \"\"C:\\Program Files\\Seedarr\\script.bat\" \"arg with space\"\""));

        var (cmdNoArgs, noArgsString) = CustomScriptService.ResolveInterpreter("C:\\Program Files\\Seedarr\\script.cmd", null, isWindows: true);
        Assert.That(cmdNoArgs, Is.EqualTo("cmd.exe"));
        Assert.That(noArgsString, Is.EqualTo("/c \"\"C:\\Program Files\\Seedarr\\script.cmd\"\""));

        var psi = CustomScriptService.BuildProcessStartInfo("C:\\Program Files\\Seedarr\\script.bat", "arg1 \"arg 2\"", isWindows: true);
        Assert.That(psi.FileName, Is.EqualTo("cmd.exe"));
        Assert.That(psi.ArgumentList, Is.EqualTo(new[] { "/c", "C:\\Program Files\\Seedarr\\script.bat", "arg1", "arg 2" }));
        Assert.That(psi.Arguments, Is.Empty);
    }

    [Test]
    public async Task ExecuteScriptAsync_with_arguments_containing_shell_metacharacters_passes_literal_arguments_without_injection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "argtest.bat" : "argtest.sh");
            var outputFile = Path.Combine(tempDir, "output.txt");
            var injectedMarker = Path.Combine(tempDir, "injected.txt");

            var scriptContent = isWindows
                ? $"@echo off\r\necho %~1 > \"{outputFile}\"\r\nexit /b 0\r\n"
                : $"#!/bin/sh\necho \"$1\" > \"{outputFile}\"\nexit 0\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            if (!isWindows)
            {
                File.SetUnixFileMode(scriptFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var maliciousArg = $"safe-value; touch \"{injectedMarker}\" & echo \"injected\"";
            var rawArguments = $"\"{maliciousArg}\"";

            var service = new CustomScriptService();
            var result = await service.ExecuteScriptAsync(scriptFile, null, "Test", rawArguments);

            Assert.That(result, Is.True);
            Assert.That(File.Exists(injectedMarker), Is.False, "Shell injection marker file should NOT be created");
            Assert.That(File.Exists(outputFile), Is.True);

            var recorded = await File.ReadAllTextAsync(outputFile);
            Assert.That(recorded.Trim(), Does.Contain("safe-value"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TestScriptAsync_should_succeed_when_script_path_contains_whitespace()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Folder With Spaces");
        Directory.CreateDirectory(tempDir);
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var scriptFile = Path.Combine(tempDir, isWindows ? "script test.bat" : "script test.sh");
            var scriptContent = isWindows
                ? "@echo off\r\necho Success from spaced path\r\nexit /b 0\r\n"
                : "#!/bin/sh\necho \"Success from spaced path\"\nexit 0\n";
            await File.WriteAllTextAsync(scriptFile, scriptContent);

            if (!isWindows)
            {
                File.SetUnixFileMode(scriptFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var service = new CustomScriptService();
            var result = await service.TestScriptAsync(scriptFile);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.True);
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.Stdout, Does.Contain("Success from spaced path"));
        }
        finally
        {
            var parentDir = Path.GetDirectoryName(tempDir);
            if (Directory.Exists(parentDir))
            {
                Directory.Delete(parentDir, true);
            }
        }
    }

    [Test]
    public async Task TryReadShebang_should_parse_various_shebang_formats()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var bashScript = Path.Combine(tempDir, "bash_script");
            await File.WriteAllTextAsync(bashScript, "#!/bin/bash\necho 1");
            var res1 = CustomScriptService.TryReadShebang(bashScript, out var interp1, out var args1);
            Assert.That(res1, Is.True);
            Assert.That(interp1, Is.EqualTo("/bin/bash"));
            Assert.That(args1, Is.Empty);

            var shScript = Path.Combine(tempDir, "sh_script");
            await File.WriteAllTextAsync(shScript, "#!/bin/sh\necho 2");
            var res2 = CustomScriptService.TryReadShebang(shScript, out var interp2, out var args2);
            Assert.That(res2, Is.True);
            Assert.That(interp2, Is.EqualTo("/bin/sh"));
            Assert.That(args2, Is.Empty);

            var envPyScript = Path.Combine(tempDir, "py_script");
            await File.WriteAllTextAsync(envPyScript, "#!/usr/bin/env python3 -u\nprint(1)");
            var res3 = CustomScriptService.TryReadShebang(envPyScript, out var interp3, out var args3);
            Assert.That(res3, Is.True);
            Assert.That(interp3, Is.EqualTo("python3"));
            Assert.That(args3, Is.EqualTo("-u"));

            var envBashScript = Path.Combine(tempDir, "env_bash");
            await File.WriteAllTextAsync(envBashScript, "#!/usr/bin/env bash\necho 3");
            var res4 = CustomScriptService.TryReadShebang(envBashScript, out var interp4, out var args4);
            Assert.That(res4, Is.True);
            Assert.That(interp4, Is.EqualTo("bash"));
            Assert.That(args4, Is.Empty);

            var envSplitScript = Path.Combine(tempDir, "env_split");
            await File.WriteAllTextAsync(envSplitScript, "#!/usr/bin/env -S node --inspect\nconsole.log(1)");
            var res5 = CustomScriptService.TryReadShebang(envSplitScript, out var interp5, out var args5);
            Assert.That(res5, Is.True);
            Assert.That(interp5, Is.EqualTo("node"));
            Assert.That(args5, Is.EqualTo("--inspect"));

            var plainScript = Path.Combine(tempDir, "plain");
            await File.WriteAllTextAsync(plainScript, "echo no shebang");
            var res6 = CustomScriptService.TryReadShebang(plainScript, out var interp6, out var args6);
            Assert.That(res6, Is.False);
            Assert.That(interp6, Is.Null);
            Assert.That(args6, Is.Null);

            var emptyScript = Path.Combine(tempDir, "empty");
            await File.WriteAllTextAsync(emptyScript, string.Empty);
            var res7 = CustomScriptService.TryReadShebang(emptyScript, out var interp7, out var args7);
            Assert.That(res7, Is.False);
            Assert.That(interp7, Is.Null);
            Assert.That(args7, Is.Null);

            var missingScript = Path.Combine(tempDir, "non_existent");
            var res8 = CustomScriptService.TryReadShebang(missingScript, out var interp8, out var args8);
            Assert.That(res8, Is.False);
            Assert.That(interp8, Is.Null);
            Assert.That(args8, Is.Null);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ResolveInterpreter_and_BuildProcessStartInfo_should_resolve_shebang_for_extensionless_scripts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var scriptPath = Path.Combine(tempDir, "post-process");
            await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\necho done");

            var (resolvedInterp, resolvedArgs) = CustomScriptService.ResolveInterpreter(scriptPath, "--flag test", isWindows: false);
            Assert.That(resolvedInterp, Is.EqualTo("/bin/sh"));
            Assert.That(resolvedArgs, Is.EqualTo($"\"{scriptPath}\" --flag test"));

            var psi = CustomScriptService.BuildProcessStartInfo(scriptPath, "--flag test", isWindows: false);
            Assert.That(psi.FileName, Is.EqualTo("/bin/sh"));
            Assert.That(psi.ArgumentList, Is.EqualTo(new[] { scriptPath, "--flag", "test" }));

            var pyScript = Path.Combine(tempDir, "custom-py");
            await File.WriteAllTextAsync(pyScript, "#!/usr/bin/env python3 -u\nprint('hi')");

            var (pyInterp, pyArgs) = CustomScriptService.ResolveInterpreter(pyScript, "run", isWindows: false);
            Assert.That(pyInterp, Is.EqualTo("python3"));
            Assert.That(pyArgs, Is.EqualTo($"-u \"{pyScript}\" run"));

            var pyPsi = CustomScriptService.BuildProcessStartInfo(pyScript, "run", isWindows: false);
            Assert.That(pyPsi.FileName, Is.EqualTo("python3"));
            Assert.That(pyPsi.ArgumentList, Is.EqualTo(new[] { "-u", pyScript, "run" }));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task EnsureExecutablePermissions_should_grant_UserExecute_and_GroupExecute_on_POSIX()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Pass("POSIX permissions not applicable on Windows");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var scriptPath = Path.Combine(tempDir, "no_exec.sh");
            await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\necho ok");

            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var initialMode = File.GetUnixFileMode(scriptPath);
            Assert.That(initialMode.HasFlag(UnixFileMode.UserExecute), Is.False);

            CustomScriptService.EnsureExecutablePermissions(scriptPath);

            var updatedMode = File.GetUnixFileMode(scriptPath);
            Assert.That(updatedMode.HasFlag(UnixFileMode.UserExecute), Is.True);
            Assert.That(updatedMode.HasFlag(UnixFileMode.GroupExecute), Is.True);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TestScriptAsync_should_auto_grant_execute_permission_and_run_successfully()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Pass("POSIX permissions not applicable on Windows");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var scriptPath = Path.Combine(tempDir, "auto_chmod_test");
            await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\necho \"autochmod works\"\nexit 0\n");

            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.That(File.GetUnixFileMode(scriptPath).HasFlag(UnixFileMode.UserExecute), Is.False);

            var service = new CustomScriptService();
            var result = await service.TestScriptAsync(scriptPath);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.True);
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.Stdout, Does.Contain("autochmod works"));
            Assert.That(File.GetUnixFileMode(scriptPath).HasFlag(UnixFileMode.UserExecute), Is.True);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task TestScriptAsync_and_ExecuteScriptAsync_should_report_actionable_error_on_permission_denied()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Pass("POSIX permissions not applicable on Windows");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var nonExecInterpreter = Path.Combine(tempDir, "bad_interpreter");
            await File.WriteAllTextAsync(nonExecInterpreter, "#!/bin/sh\necho nope\n");
            File.SetUnixFileMode(nonExecInterpreter, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var scriptPath = Path.Combine(tempDir, "script_with_bad_interpreter");
            await File.WriteAllTextAsync(scriptPath, $"#!{nonExecInterpreter}\necho test\n");
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var service = new CustomScriptService();
            var testResult = await service.TestScriptAsync(scriptPath);

            Assert.That(testResult, Is.Not.Null);
            Assert.That(testResult.Success, Is.False);
            Assert.That(testResult.ExitCode, Is.EqualTo(13));
            Assert.That(testResult.Stderr, Does.Contain("Permission denied (EACCES)"));
            Assert.That(testResult.Stderr, Does.Contain("chmod +x"));

            var execResult = await service.ExecuteScriptAsync(scriptPath, null, "Test");
            Assert.That(execResult, Is.False);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ReadBoundedAsync_should_truncate_output_exceeding_max_bytes_and_append_warning()
    {
        var input = new string('x', CustomScriptService.MaxStreamCaptureBytes + 1024);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(input));
        using var reader = new StreamReader(stream);

        var result = await CustomScriptService.ReadBoundedAsync(reader, CustomScriptService.MaxStreamCaptureBytes, CancellationToken.None);

        var expectedPrefix = new string('x', CustomScriptService.MaxStreamCaptureBytes);
        Assert.That(result, Does.StartWith(expectedPrefix));
        Assert.That(result, Does.Contain("[... output truncated after reaching maximum capture limit ...]"));
        Assert.That(result.Length, Is.EqualTo(CustomScriptService.MaxStreamCaptureBytes + "\n[... output truncated after reaching maximum capture limit ...]".Length));
    }

    [Test]
    public async Task ReadBoundedAsync_should_not_truncate_output_within_limit()
    {
        var input = "Short custom script output line 1\nLine 2\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(input));
        using var reader = new StreamReader(stream);

        var result = await CustomScriptService.ReadBoundedAsync(reader, CustomScriptService.MaxStreamCaptureBytes, CancellationToken.None);

        Assert.That(result, Is.EqualTo(input));
        Assert.That(result, Does.Not.Contain("[... output truncated after reaching maximum capture limit ...]"));
    }

    [Test]
    public async Task ReadBoundedAsync_should_respect_cancellation_token()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Some output"));
        using var reader = new StreamReader(stream);

        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            await CustomScriptService.ReadBoundedAsync(reader, 1024, cts.Token);
        });
    }

    [Test]
    public async Task ReadBoundedAsync_should_throw_OperationCanceledException_and_not_ObjectDisposedException_when_stream_disposed_and_token_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var ms = new MemoryStream(new byte[100]);
        var reader = new StreamReader(ms);
        reader.Dispose();
        await cts.CancelAsync();

        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            await CustomScriptService.ReadBoundedAsync(reader, 1024, cts.Token);
        });
    }

    [Test]
    public async Task TestScriptAsync_should_cancel_stream_reader_and_not_escape_ObjectDisposedException_when_stream_drain_times_out()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Pass("POSIX process fork test not applicable on Windows");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var scriptPath = Path.Combine(tempDir, "drain_hang.sh");
            await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\n(sleep 5) &\nexit 0\n");
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var service = new CustomScriptService(
                scriptTimeout: TimeSpan.FromSeconds(5),
                streamDrainTimeout: TimeSpan.FromMilliseconds(100));

            var result = await service.TestScriptAsync(scriptPath);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.True);
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.TimedOut, Is.False);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
