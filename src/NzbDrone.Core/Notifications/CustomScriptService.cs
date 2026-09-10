using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public interface ICustomScriptService
{
    Task<bool> ExecuteScriptAsync(string scriptPath, Torrent torrent, string eventType, string arguments = null);
}

public class CustomScriptService : ICustomScriptService
{
    private readonly IMediaEnrichmentService _mediaEnrichmentService;
    private readonly TimeSpan _scriptTimeout;
    private readonly TimeSpan _streamDrainTimeout;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public CustomScriptService(
        IMediaEnrichmentService mediaEnrichmentService = null,
        IConfigService configService = null,
        IConfigFileProvider configFileProvider = null,
        TimeSpan? scriptTimeout = null,
        TimeSpan? streamDrainTimeout = null)
    {
        _mediaEnrichmentService = mediaEnrichmentService;
        var timeoutSec = 60;
        if (configService != null)
        {
            var val = configService.GetValueInt("CustomScriptTimeoutSeconds", 0);
            if (val > 0)
            {
                timeoutSec = val;
            }
        }

        _scriptTimeout = scriptTimeout ?? TimeSpan.FromSeconds(timeoutSec);
        _streamDrainTimeout = streamDrainTimeout ?? TimeSpan.FromSeconds(3);
    }

    public TimeSpan ScriptTimeout => _scriptTimeout;

    internal static (string FileName, string Arguments) ResolveInterpreter(string scriptPath, string arguments)
    {
        var ext = Path.GetExtension(scriptPath).ToLowerInvariant();
        var args = arguments ?? string.Empty;

        if (OperatingSystem.IsWindows())
        {
            switch (ext)
            {
                case ".bat":
                case ".cmd":
                    return ("cmd.exe", $"/c \"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".py":
                case ".pyw":
                    return ("python", $"\"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".ps1":
                    return ("powershell.exe", $"-ExecutionPolicy Bypass -File \"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                default:
                    return (scriptPath, args);
            }
        }
        else
        {
            switch (ext)
            {
                case ".sh":
                    return ("/bin/sh", $"\"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".bash":
                    return ("/bin/bash", $"\"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".py":
                case ".pyw":
                    return ("python3", $"\"{scriptPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                default:
                    return (scriptPath, args);
            }
        }
    }

    internal static void SanitizeEnvironment(System.Collections.Specialized.StringDictionary environmentVariables)
    {
        var keysToRemove = new List<string>();
        foreach (string key in environmentVariables.Keys)
        {
            if (IsSensitiveEnvironmentVariable(key))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            environmentVariables.Remove(key);
        }
    }

    internal static bool IsSensitiveEnvironmentVariable(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (key.StartsWith("SEEDARR_TORRENT_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("SEEDARR_MEDIA_", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("SEEDARR_EVENT_TYPE", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("LEECHARR_TORRENT_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("LEECHARR_MEDIA_", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("LEECHARR_EVENT_TYPE", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("TR_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("TORRENT_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var upper = key.ToUpperInvariant();

        if (upper.StartsWith("SEEDARR__") ||
            upper.StartsWith("LEECHARR__") ||
            upper.StartsWith("DATABASE_") ||
            upper.StartsWith("POSTGRES_") ||
            upper.StartsWith("DB_") ||
            upper.StartsWith("REDIS_") ||
            upper.StartsWith("SECRET_") ||
            upper.StartsWith("API_KEY") ||
            upper.StartsWith("PROXY_"))
        {
            return true;
        }

        var sensitiveKeywords = new[]
        {
            "PASSWORD", "PASSWD", "SECRET", "API_KEY", "APIKEY", "TOKEN", "CREDENTIAL", "AUTH",
            "CONNECTIONSTRING", "PRIVATE_KEY",
        };

        return sensitiveKeywords.Any(k => upper.Contains(k));
    }

    internal static Dictionary<string, string> BuildEnvironmentVariables(string eventType, Torrent torrent, TorrentMediaMetadata meta = null)
    {
        var env = new Dictionary<string, string>
        {
            ["SEEDARR_EVENT_TYPE"] = eventType ?? string.Empty,
            ["LEECHARR_EVENT_TYPE"] = eventType ?? string.Empty,
        };

        if (torrent != null)
        {
            env["TORRENT_ID"] = torrent.Id.ToString(CultureInfo.InvariantCulture);
            env["TORRENT_NAME"] = torrent.Name ?? string.Empty;
            env["TORRENT_INFOHASH"] = torrent.InfoHash ?? string.Empty;
            env["TORRENT_CATEGORY"] = torrent.Label ?? string.Empty;
            env["TORRENT_PATH"] = torrent.SourcePath ?? string.Empty;
            env["TORRENT_SIZE"] = torrent.TotalSize.ToString(CultureInfo.InvariantCulture);
            env["TORRENT_RATIO"] = torrent.Ratio.ToString("F2", CultureInfo.InvariantCulture);
            env["TORRENT_STATUS"] = torrent.Status.ToString();

            env["SEEDARR_TORRENT_ID"] = torrent.Id.ToString(CultureInfo.InvariantCulture);
            env["SEEDARR_TORRENT_NAME"] = torrent.Name ?? string.Empty;
            env["SEEDARR_TORRENT_INFOHASH"] = torrent.InfoHash ?? string.Empty;
            env["SEEDARR_TORRENT_CATEGORY"] = torrent.Label ?? string.Empty;
            env["SEEDARR_TORRENT_PATH"] = torrent.SourcePath ?? string.Empty;
            env["SEEDARR_TORRENT_SIZE"] = torrent.TotalSize.ToString(CultureInfo.InvariantCulture);
            env["SEEDARR_TORRENT_RATIO"] = torrent.Ratio.ToString("F2", CultureInfo.InvariantCulture);
            env["SEEDARR_TORRENT_STATUS"] = torrent.Status.ToString();

            // Transmission compatibility environment variables
            env["TR_TORRENT_DIR"] = torrent.SourcePath ?? string.Empty;
            env["TR_TORRENT_NAME"] = torrent.Name ?? string.Empty;
            env["TR_TORRENT_HASH"] = torrent.InfoHash ?? string.Empty;
            env["TR_TORRENT_ID"] = torrent.Id.ToString(CultureInfo.InvariantCulture);
            env["TR_TIME_LOCALTIME"] = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            env["TR_APP_VERSION"] = "4.0.0";

            if (meta != null)
            {
                env["SEEDARR_MEDIA_TITLE"] = meta.Title ?? string.Empty;
                env["SEEDARR_MEDIA_YEAR"] = meta.Year > 0 ? meta.Year.ToString(CultureInfo.InvariantCulture) : string.Empty;
                env["SEEDARR_MEDIA_OVERVIEW"] = meta.Overview ?? string.Empty;
                env["SEEDARR_MEDIA_GENRES"] = meta.Genres ?? string.Empty;
                env["SEEDARR_MEDIA_RATING"] = meta.Rating > 0 ? meta.Rating.ToString("F1", CultureInfo.InvariantCulture) : string.Empty;
                env["SEEDARR_MEDIA_IMDB_ID"] = meta.ImdbId ?? string.Empty;
            }
        }

        return env;
    }

    public static (string ScriptPath, string Arguments) ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return (string.Empty, null);
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                string path = null;
                string arguments = null;

                var pathProps = new[] { "path", "Path", "scriptPath", "ScriptPath", "script", "Script", "filename", "Filename" };
                foreach (var prop in pathProps)
                {
                    if (root.TryGetProperty(prop, out var val))
                    {
                        path = val.GetString() ?? val.ToString();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            break;
                        }
                    }
                }

                var argProps = new[] { "arguments", "Arguments", "args", "Args", "extraArguments", "ExtraArguments" };
                foreach (var prop in argProps)
                {
                    if (root.TryGetProperty(prop, out var val))
                    {
                        arguments = val.GetString() ?? val.ToString();
                        if (!string.IsNullOrWhiteSpace(arguments))
                        {
                            break;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(path))
                {
                    return (path, string.IsNullOrWhiteSpace(arguments) ? null : arguments);
                }
            }
            catch
            {
            }
        }

        if (trimmed.Contains("path=", StringComparison.OrdinalIgnoreCase))
        {
            var matchPath = System.Text.RegularExpressions.Regex.Match(trimmed, @"path=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (matchPath.Success)
            {
                var path = Uri.UnescapeDataString(matchPath.Groups[1].Value);
                string args = null;
                var matchArgs = System.Text.RegularExpressions.Regex.Match(trimmed, @"arguments=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (matchArgs.Success)
                {
                    args = Uri.UnescapeDataString(matchArgs.Groups[1].Value);
                }

                return (path, string.IsNullOrWhiteSpace(args) ? null : args);
            }
        }

        return (trimmed, null);
    }

    public async Task<bool> ExecuteScriptAsync(string scriptPath, Torrent torrent, string eventType, string arguments = null)
    {
        var resolvedScriptPath = scriptPath;
        var resolvedArguments = arguments;

        if (!string.IsNullOrWhiteSpace(scriptPath) && scriptPath.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            var (parsedPath, parsedArgs) = ParseSettings(scriptPath);
            if (!string.IsNullOrWhiteSpace(parsedPath))
            {
                resolvedScriptPath = parsedPath;
                if (string.IsNullOrWhiteSpace(resolvedArguments))
                {
                    resolvedArguments = parsedArgs;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedScriptPath) || !File.Exists(resolvedScriptPath))
        {
            _logger.Warn("Custom script path does not exist: {0}", resolvedScriptPath);
            return false;
        }

        try
        {
            var workingDir = !string.IsNullOrWhiteSpace(torrent?.SourcePath) && Directory.Exists(torrent.SourcePath)
                ? torrent.SourcePath
                : (Path.GetDirectoryName(resolvedScriptPath) ?? Environment.CurrentDirectory);

            var (resolvedFileName, resolvedArgs) = ResolveInterpreter(resolvedScriptPath, resolvedArguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedFileName,
                Arguments = resolvedArgs,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            SanitizeEnvironment(startInfo.EnvironmentVariables);

            var meta = torrent != null ? _mediaEnrichmentService?.GetMetadata(torrent.Id) : null;
            var envVars = BuildEnvironmentVariables(eventType, torrent, meta);
            foreach (var kvp in envVars)
            {
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }

            _logger.Info("Executing custom script '{0}' for event '{1}' in working directory '{2}'...", resolvedScriptPath, eventType, workingDir);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            using var timeoutCts = new CancellationTokenSource(_scriptTimeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.Error("Custom script timed out after {0}s: {1}", _scriptTimeout.TotalSeconds, resolvedScriptPath);
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                }

                return false;
            }

            var stdout = string.Empty;
            var stderr = string.Empty;

            try
            {
                using var drainCts = new CancellationTokenSource(_streamDrainTimeout);
                using var linkedDrainCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, drainCts.Token);

                try
                {
                    await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(linkedDrainCts.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
                {
                    _logger.Debug("Custom script stream draining timed out after process exit: {0}", resolvedScriptPath);
                }

                if (stdoutTask.IsCompletedSuccessfully)
                {
                    stdout = await stdoutTask.ConfigureAwait(false);
                }

                if (stderrTask.IsCompletedSuccessfully)
                {
                    stderr = await stderrTask.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Exception while draining custom script streams: {0}", resolvedScriptPath);
            }

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                _logger.Debug("Custom script stdout: {0}", stdout.Trim());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                _logger.Warn("Custom script stderr: {0}", stderr.Trim());
            }

            _logger.Info("Custom script '{0}' completed with exit code: {1}", resolvedScriptPath, process.ExitCode);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to execute custom script: {0}", resolvedScriptPath);
            return false;
        }
    }
}
