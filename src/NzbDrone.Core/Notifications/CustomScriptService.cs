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
using NzbDrone.Core.Processes;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public interface ICustomScriptService
{
    Task<bool> ExecuteScriptAsync(string scriptPath, Torrent torrent, string eventType, string arguments = null);

    Task<CustomScriptTestResult> TestScriptAsync(string scriptPath, string arguments = null, string eventType = "Test");
}

public class CustomScriptService : ICustomScriptService, IDisposable
{
    private readonly ITorrentMediaMetadataRepository _mediaMetadataRepository;
    private readonly ITagService _tagService;
    private readonly ISidecarProcessSupervisor _processSupervisor;
    private readonly SemaphoreSlim _concurrencyThrottle;
    private readonly TimeSpan _scriptTimeout;
    private readonly TimeSpan _streamDrainTimeout;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public CustomScriptService(
        ITorrentMediaMetadataRepository mediaMetadataRepository = null,
        IConfigService configService = null,
        IConfigFileProvider configFileProvider = null,
        ITagService tagService = null,
        ISidecarProcessSupervisor processSupervisor = null,
        TimeSpan? scriptTimeout = null,
        TimeSpan? streamDrainTimeout = null,
        int maxConcurrentScripts = 4)
    {
        _mediaMetadataRepository = mediaMetadataRepository;
        _tagService = tagService;
        _processSupervisor = processSupervisor;
        var timeoutSec = configService != null && configService.CustomScriptTimeoutSeconds > 0
            ? Math.Clamp(configService.CustomScriptTimeoutSeconds, 5, 3600)
            : 60;

        _scriptTimeout = scriptTimeout ?? TimeSpan.FromSeconds(timeoutSec);
        _streamDrainTimeout = streamDrainTimeout ?? TimeSpan.FromSeconds(3);
        var concurrency = maxConcurrentScripts > 0 ? maxConcurrentScripts : 4;
        _concurrencyThrottle = new SemaphoreSlim(concurrency, concurrency);
    }

    public TimeSpan ScriptTimeout => _scriptTimeout;

    public SemaphoreSlim ConcurrencyThrottle => _concurrencyThrottle;

    public ISidecarProcessSupervisor ProcessSupervisor => _processSupervisor;

    public static string CleanScriptPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var cleaned = path.Trim();
        while ((cleaned.StartsWith('"') && cleaned.EndsWith('"')) ||
            (cleaned.StartsWith('\'') && cleaned.EndsWith('\'')))
        {
            if (cleaned.Length < 2)
            {
                break;
            }

            cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
        }

        return cleaned;
    }

    internal static (string FileName, string Arguments) ResolveInterpreter(string scriptPath, string arguments)
    {
        var cleanPath = CleanScriptPath(scriptPath);
        var ext = Path.GetExtension(cleanPath).ToLowerInvariant();
        var args = arguments?.Trim() ?? string.Empty;

        if (OperatingSystem.IsWindows())
        {
            switch (ext)
            {
                case ".bat":
                case ".cmd":
                    return ("cmd.exe", $"/c \"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".py":
                case ".pyw":
                    return ("python", $"\"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".ps1":
                    return ("powershell.exe", $"-ExecutionPolicy Bypass -File \"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".rb":
                    return ("ruby", $"\"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".js":
                    return ("node", $"\"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                default:
                    return (cleanPath, args);
            }
        }
        else
        {
            switch (ext)
            {
                case ".sh":
                    return ("/bin/sh", $"\"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".bash":
                    return ("/bin/bash", $"\"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".py":
                case ".pyw":
                    return ("python3", $"\"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".ps1":
                    return ("pwsh", $"-File \"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".rb":
                    return ("ruby", $"\"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                case ".js":
                    return ("node", $"\"{cleanPath}\" {(string.IsNullOrWhiteSpace(args) ? string.Empty : args)}".TrimEnd());
                default:
                    return (cleanPath, args);
            }
        }
    }

    internal static async Task TerminateProcessTreeAsync(Process process, string scriptPath, Logger logger)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            var pid = -1;
            try
            {
                pid = process.Id;
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Could not retrieve PID for process of script {0}", scriptPath);
            }

            if (pid > 0)
            {
                // Kill process tree first using built-in framework support
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Failed to kill process tree for script {0} (PID {1})", scriptPath, pid);
                }

                // If on POSIX, terminate the process group to ensure detached child processes are terminated
                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        using var killProc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "kill",
                            Arguments = $"-9 -{pid}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        });

                        if (killProc != null)
                        {
                            using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await killProc.WaitForExitAsync(killCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, "Failed to send kill -9 to process group -{0} for script {1}", pid, scriptPath);
                    }
                }
                else
                {
                    // On Windows, taskkill /F /T terminates process and all child processes started by it
                    try
                    {
                        using var taskkillProc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/F /T /PID {pid}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        });

                        if (taskkillProc != null)
                        {
                            using var taskkillCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await taskkillProc.WaitForExitAsync(taskkillCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, "Failed to run taskkill /F /T for PID {0} for script {1}", pid, scriptPath);
                    }
                }
            }

            // Structured reaping: wait for the root process to exit
            try
            {
                using var reapCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(reapCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Timed out or failed while reaping process {0} for script {1}", pid, scriptPath);
            }
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "Exception during TerminateProcessTreeAsync for script {0}", scriptPath);
        }
    }

    internal static void SanitizeEnvironment(System.Collections.Specialized.StringDictionary environmentVariables)
    {
        var keysToRemove = new List<string>();
        var keysToUpdate = new Dictionary<string, string>();

        foreach (string key in environmentVariables.Keys)
        {
            if (IsSensitiveEnvironmentVariable(key))
            {
                keysToRemove.Add(key);
                continue;
            }

            var val = environmentVariables[key];
            var keyHasNull = key != null && key.Contains('\0');
            var valHasNull = val != null && val.Contains('\0');

            if (keyHasNull || valHasNull)
            {
                keysToRemove.Add(key);
                var cleanKey = SanitizeEnvKey(key);
                var cleanVal = SanitizeEnvValue(val);
                if (!string.IsNullOrEmpty(cleanKey))
                {
                    keysToUpdate[cleanKey] = cleanVal;
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            environmentVariables.Remove(key);
        }

        foreach (var kvp in keysToUpdate)
        {
            environmentVariables[kvp.Key] = kvp.Value;
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
            key.Equals("SEEDARR_EVENTTYPE", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("LEECHARR_TORRENT_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("LEECHARR_MEDIA_", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("LEECHARR_EVENT_TYPE", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("LEECHARR_EVENTTYPE", StringComparison.OrdinalIgnoreCase) ||
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

    public static string SanitizeEnvValue(string value, int maxLength = -1)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = value
            .Replace("\0", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", " ");

        if (maxLength > 0 && sanitized.Length > maxLength)
        {
            sanitized = sanitized.Substring(0, maxLength);
        }

        return sanitized;
    }

    public static string SanitizeEnvKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return key
            .Replace("\0", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);
    }

    public static Dictionary<string, string> BuildEnvironmentVariables(
        string eventType,
        Torrent torrent,
        TorrentMediaMetadata meta = null,
        ITagService tagService = null,
        IEnumerable<string> tagLabels = null)
    {
        var rawEnv = new Dictionary<string, string>
        {
            ["SEEDARR_EVENT_TYPE"] = eventType ?? string.Empty,
            ["SEEDARR_EVENTTYPE"] = eventType ?? string.Empty,
            ["LEECHARR_EVENT_TYPE"] = eventType ?? string.Empty,
            ["LEECHARR_EVENTTYPE"] = eventType ?? string.Empty,
        };

        if (torrent != null)
        {
            rawEnv["TORRENT_ID"] = torrent.Id.ToString(CultureInfo.InvariantCulture);
            rawEnv["TORRENT_NAME"] = torrent.Name ?? string.Empty;
            rawEnv["TORRENT_INFOHASH"] = torrent.InfoHash ?? string.Empty;
            rawEnv["TORRENT_CATEGORY"] = torrent.Category ?? torrent.Label ?? string.Empty;
            rawEnv["TORRENT_PATH"] = torrent.SavePath ?? torrent.SourcePath ?? string.Empty;
            rawEnv["TORRENT_SAVEPATH"] = torrent.SavePath ?? torrent.SourcePath ?? string.Empty;
            rawEnv["TORRENT_SIZE"] = torrent.TotalSize.ToString(CultureInfo.InvariantCulture);
            rawEnv["TORRENT_SIZE_BYTES"] = torrent.TotalSize.ToString(CultureInfo.InvariantCulture);
            rawEnv["TORRENT_RATIO"] = torrent.Ratio.ToString("F2", CultureInfo.InvariantCulture);
            rawEnv["TORRENT_STATUS"] = torrent.Status.ToString();

            if (torrent.TagIds != null && torrent.TagIds.Count > 0)
            {
                var resolvedLabels = tagLabels?.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (resolvedLabels == null && tagService != null)
                {
                    resolvedLabels = tagService.GetLabelsForTagIds(torrent.TagIds);
                }

                var tagsString = resolvedLabels != null
                    ? string.Join(",", resolvedLabels)
                    : string.Join(",", torrent.TagIds);

                rawEnv["TORRENT_TAGS"] = tagsString;
                rawEnv["SEEDARR_TORRENT_TAGS"] = tagsString;
                rawEnv["LEECHARR_TORRENT_TAGS"] = tagsString;
            }

            rawEnv["SEEDARR_TORRENT_ID"] = torrent.Id.ToString(CultureInfo.InvariantCulture);
            rawEnv["SEEDARR_TORRENT_NAME"] = torrent.Name ?? string.Empty;
            rawEnv["SEEDARR_TORRENT_INFOHASH"] = torrent.InfoHash ?? string.Empty;
            rawEnv["SEEDARR_TORRENT_CATEGORY"] = torrent.Category ?? torrent.Label ?? string.Empty;
            rawEnv["SEEDARR_TORRENT_PATH"] = torrent.SavePath ?? torrent.SourcePath ?? string.Empty;
            rawEnv["SEEDARR_TORRENT_SAVEPATH"] = torrent.SavePath ?? torrent.SourcePath ?? string.Empty;
            rawEnv["SEEDARR_TORRENT_SIZE"] = torrent.TotalSize.ToString(CultureInfo.InvariantCulture);
            rawEnv["SEEDARR_TORRENT_SIZE_BYTES"] = torrent.TotalSize.ToString(CultureInfo.InvariantCulture);
            rawEnv["SEEDARR_TORRENT_RATIO"] = torrent.Ratio.ToString("F2", CultureInfo.InvariantCulture);
            rawEnv["SEEDARR_TORRENT_STATUS"] = torrent.Status.ToString();

            rawEnv["LEECHARR_TORRENT_ID"] = torrent.Id.ToString(CultureInfo.InvariantCulture);
            rawEnv["LEECHARR_TORRENT_NAME"] = torrent.Name ?? string.Empty;
            rawEnv["LEECHARR_TORRENT_INFOHASH"] = torrent.InfoHash ?? string.Empty;
            rawEnv["LEECHARR_TORRENT_CATEGORY"] = torrent.Category ?? torrent.Label ?? string.Empty;
            rawEnv["LEECHARR_TORRENT_PATH"] = torrent.SavePath ?? torrent.SourcePath ?? string.Empty;
            rawEnv["LEECHARR_TORRENT_SAVEPATH"] = torrent.SavePath ?? torrent.SourcePath ?? string.Empty;
            rawEnv["LEECHARR_TORRENT_SIZE"] = torrent.TotalSize.ToString(CultureInfo.InvariantCulture);
            rawEnv["LEECHARR_TORRENT_SIZE_BYTES"] = torrent.TotalSize.ToString(CultureInfo.InvariantCulture);
            rawEnv["LEECHARR_TORRENT_RATIO"] = torrent.Ratio.ToString("F2", CultureInfo.InvariantCulture);
            rawEnv["LEECHARR_TORRENT_STATUS"] = torrent.Status.ToString();

            // Transmission compatibility environment variables
            rawEnv["TR_TORRENT_DIR"] = torrent.SavePath ?? torrent.SourcePath ?? string.Empty;
            rawEnv["TR_TORRENT_NAME"] = torrent.Name ?? string.Empty;
            rawEnv["TR_TORRENT_HASH"] = torrent.InfoHash ?? string.Empty;
            rawEnv["TR_TORRENT_ID"] = torrent.Id.ToString(CultureInfo.InvariantCulture);
            rawEnv["TR_TIME_LOCALTIME"] = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            rawEnv["TR_APP_VERSION"] = "4.0.0";

            if (meta != null)
            {
                var overview = SanitizeEnvValue(meta.Overview, 1024);
                rawEnv["SEEDARR_MEDIA_TITLE"] = meta.Title ?? string.Empty;
                rawEnv["SEEDARR_MEDIA_YEAR"] = meta.Year > 0 ? meta.Year.ToString(CultureInfo.InvariantCulture) : string.Empty;
                rawEnv["SEEDARR_MEDIA_OVERVIEW"] = overview;
                rawEnv["SEEDARR_MEDIA_GENRES"] = meta.Genres ?? string.Empty;
                rawEnv["SEEDARR_MEDIA_RATING"] = meta.Rating > 0 ? meta.Rating.ToString("F1", CultureInfo.InvariantCulture) : string.Empty;
                rawEnv["SEEDARR_MEDIA_IMDB_ID"] = meta.ImdbId ?? string.Empty;

                rawEnv["LEECHARR_MEDIA_TITLE"] = meta.Title ?? string.Empty;
                rawEnv["LEECHARR_MEDIA_YEAR"] = meta.Year > 0 ? meta.Year.ToString(CultureInfo.InvariantCulture) : string.Empty;
                rawEnv["LEECHARR_MEDIA_OVERVIEW"] = overview;
                rawEnv["LEECHARR_MEDIA_GENRES"] = meta.Genres ?? string.Empty;
                rawEnv["LEECHARR_MEDIA_RATING"] = meta.Rating > 0 ? meta.Rating.ToString("F1", CultureInfo.InvariantCulture) : string.Empty;
                rawEnv["LEECHARR_MEDIA_IMDB_ID"] = meta.ImdbId ?? string.Empty;
            }
        }

        var env = new Dictionary<string, string>();
        foreach (var kvp in rawEnv)
        {
            var cleanKey = SanitizeEnvKey(kvp.Key);
            var cleanVal = SanitizeEnvValue(kvp.Value);
            if (!string.IsNullOrEmpty(cleanKey))
            {
                env[cleanKey] = cleanVal;
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

                var hasPathProp = false;
                var pathProps = new[] { "path", "Path", "scriptPath", "ScriptPath", "script", "Script", "filename", "Filename" };
                foreach (var prop in pathProps)
                {
                    if (root.TryGetProperty(prop, out var val))
                    {
                        hasPathProp = true;
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

                if (hasPathProp)
                {
                    return (CleanScriptPath(path), string.IsNullOrWhiteSpace(arguments) ? null : arguments);
                }
            }
            catch
            {
                // Fall back to query string / raw string
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

                return (CleanScriptPath(path), string.IsNullOrWhiteSpace(args) ? null : args);
            }
        }

        return (CleanScriptPath(trimmed), null);
    }

    public async Task<bool> ExecuteScriptAsync(string scriptPath, Torrent torrent, string eventType, string arguments = null)
    {
        if (_processSupervisor != null && _processSupervisor.IsShuttingDown)
        {
            _logger.Warn("Skipping custom script execution for '{0}': application is shutting down.", scriptPath);
            return false;
        }

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

        resolvedScriptPath = CleanScriptPath(resolvedScriptPath);

        if (string.IsNullOrWhiteSpace(resolvedScriptPath) || !File.Exists(resolvedScriptPath))
        {
            _logger.Warn("Custom script path does not exist: {0}", resolvedScriptPath);
            return false;
        }

        await _concurrencyThrottle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_processSupervisor != null && _processSupervisor.IsShuttingDown)
            {
                _logger.Warn("Skipping custom script execution for '{0}': application is shutting down.", resolvedScriptPath);
                return false;
            }

            var workingDir = !string.IsNullOrWhiteSpace(torrent?.SavePath) && Directory.Exists(torrent.SavePath)
                ? torrent.SavePath
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

            // Sanitize inherited environment variables
            SanitizeEnvironment(startInfo.EnvironmentVariables);

            // Inject Servarr / Seedarr standard environment variables
            var meta = torrent != null ? _mediaMetadataRepository?.GetByTorrentId(torrent.Id) : null;
            var envVars = BuildEnvironmentVariables(eventType, torrent, meta, _tagService);
            foreach (var kvp in envVars)
            {
                var cleanKey = SanitizeEnvKey(kvp.Key);
                var cleanVal = SanitizeEnvValue(kvp.Value);
                if (!string.IsNullOrEmpty(cleanKey))
                {
                    startInfo.EnvironmentVariables[cleanKey] = cleanVal;
                }
            }

            _logger.Info("Executing custom script '{0}' for event '{1}' in working directory '{2}'...", resolvedScriptPath, eventType, workingDir);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var pid = -1;
            try
            {
                pid = process.Id;
            }
            catch
            {
            }

            if (pid > 0)
            {
                _processSupervisor?.RegisterProcess(process);
            }

            try
            {
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
                    await TerminateProcessTreeAsync(process, resolvedScriptPath, _logger).ConfigureAwait(false);
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
            finally
            {
                if (pid > 0)
                {
                    _processSupervisor?.UnregisterProcess(pid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to execute custom script: {0}", resolvedScriptPath);
            return false;
        }
        finally
        {
            _concurrencyThrottle.Release();
        }
    }

    public async Task<CustomScriptTestResult> TestScriptAsync(string scriptPath, string arguments = null, string eventType = "Test")
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

        resolvedScriptPath = CleanScriptPath(resolvedScriptPath);

        var scriptDir = !string.IsNullOrWhiteSpace(resolvedScriptPath) ? Path.GetDirectoryName(resolvedScriptPath) : null;
        var workingDir = !string.IsNullOrWhiteSpace(scriptDir) && Directory.Exists(scriptDir)
            ? scriptDir
            : Environment.CurrentDirectory;

        if (string.IsNullOrWhiteSpace(resolvedScriptPath) || !File.Exists(resolvedScriptPath))
        {
            var msg = string.IsNullOrWhiteSpace(resolvedScriptPath)
                ? "Script path is required."
                : $"Script file does not exist: '{resolvedScriptPath}'";
            _logger.Warn(msg);
            return new CustomScriptTestResult
            {
                Success = false,
                ExitCode = -1,
                Stdout = string.Empty,
                Stderr = msg,
                ExecutionTimeMs = 0,
                TimedOut = false,
                ResolvedInterpreter = string.Empty,
                WorkingDirectory = workingDir,
            };
        }

        if (_processSupervisor != null && _processSupervisor.IsShuttingDown)
        {
            var msg = "Application is shutting down.";
            _logger.Warn(msg);
            return new CustomScriptTestResult
            {
                Success = false,
                ExitCode = -1,
                Stdout = string.Empty,
                Stderr = msg,
                ExecutionTimeMs = 0,
                TimedOut = false,
                ResolvedInterpreter = string.Empty,
                WorkingDirectory = workingDir,
            };
        }

        await _concurrencyThrottle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_processSupervisor != null && _processSupervisor.IsShuttingDown)
            {
                var msg = "Application is shutting down.";
                _logger.Warn(msg);
                return new CustomScriptTestResult
                {
                    Success = false,
                    ExitCode = -1,
                    Stdout = string.Empty,
                    Stderr = msg,
                    ExecutionTimeMs = 0,
                    TimedOut = false,
                    ResolvedInterpreter = string.Empty,
                    WorkingDirectory = workingDir,
                };
            }

            var (resolvedFileName, resolvedArgs) = ResolveInterpreter(resolvedScriptPath, resolvedArguments);

            var stopwatch = Stopwatch.StartNew();
            try
            {
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

                // Sanitize inherited environment variables
                SanitizeEnvironment(startInfo.EnvironmentVariables);

                // Inject Servarr / Seedarr standard environment variables
                var envVars = BuildEnvironmentVariables(eventType ?? "Test", null, null, _tagService);
                foreach (var kvp in envVars)
                {
                    var cleanKey = SanitizeEnvKey(kvp.Key);
                    var cleanVal = SanitizeEnvValue(kvp.Value);
                    if (!string.IsNullOrEmpty(cleanKey))
                    {
                        startInfo.EnvironmentVariables[cleanKey] = cleanVal;
                    }
                }

                _logger.Info("Testing custom script '{0}' for event '{1}' in working directory '{2}'...", resolvedScriptPath, eventType, workingDir);

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var pid = -1;
                try
                {
                    pid = process.Id;
                }
                catch
                {
                }

                if (pid > 0)
                {
                    _processSupervisor?.RegisterProcess(process);
                }

                try
                {
                    using var timeoutCts = new CancellationTokenSource(_scriptTimeout);
                    var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                    var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

                    var timedOut = false;
                    try
                    {
                        await process.WaitForExitAsync(timeoutCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        timedOut = true;
                        _logger.Error("Custom script test timed out after {0}s: {1}", _scriptTimeout.TotalSeconds, resolvedScriptPath);
                        await TerminateProcessTreeAsync(process, resolvedScriptPath, _logger).ConfigureAwait(false);
                    }

                    stopwatch.Stop();
                    var elapsedMs = stopwatch.ElapsedMilliseconds > 0 ? stopwatch.ElapsedMilliseconds : (stopwatch.Elapsed.TotalMilliseconds > 0 ? 1L : 0L);

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
                            _logger.Debug("Custom script test stream draining timed out after process exit: {0}", resolvedScriptPath);
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
                        _logger.Debug(ex, "Exception while draining custom script test streams: {0}", resolvedScriptPath);
                    }

                    var exitCode = timedOut ? -1 : process.ExitCode;
                    if (timedOut && string.IsNullOrWhiteSpace(stderr))
                    {
                        stderr = $"Script execution timed out after {_scriptTimeout.TotalSeconds} seconds.";
                    }

                    if (!string.IsNullOrWhiteSpace(stdout))
                    {
                        _logger.Debug("Custom script test stdout: {0}", stdout.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        _logger.Warn("Custom script test stderr: {0}", stderr.Trim());
                    }

                    _logger.Info("Custom script test '{0}' completed with exit code: {1}", resolvedScriptPath, exitCode);

                    return new CustomScriptTestResult
                    {
                        Success = !timedOut && exitCode == 0,
                        ExitCode = exitCode,
                        Stdout = stdout ?? string.Empty,
                        Stderr = stderr ?? string.Empty,
                        ExecutionTimeMs = elapsedMs,
                        TimedOut = timedOut,
                        ResolvedInterpreter = resolvedFileName,
                        WorkingDirectory = workingDir,
                    };
                }
                finally
                {
                    if (pid > 0)
                    {
                        _processSupervisor?.UnregisterProcess(pid);
                    }
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var elapsedMs = stopwatch.ElapsedMilliseconds > 0 ? stopwatch.ElapsedMilliseconds : (stopwatch.Elapsed.TotalMilliseconds > 0 ? 1L : 0L);
                _logger.Error(ex, "Failed to execute custom script test: {0}", resolvedScriptPath);
                return new CustomScriptTestResult
                {
                    Success = false,
                    ExitCode = ex is System.ComponentModel.Win32Exception win32Ex ? win32Ex.NativeErrorCode : -1,
                    Stdout = string.Empty,
                    Stderr = ex.Message,
                    ExecutionTimeMs = elapsedMs,
                    TimedOut = false,
                    ResolvedInterpreter = resolvedFileName,
                    WorkingDirectory = workingDir,
                };
            }
        }
        finally
        {
            _concurrencyThrottle.Release();
        }
    }

    public void Dispose()
    {
        _concurrencyThrottle?.Dispose();
    }
}
