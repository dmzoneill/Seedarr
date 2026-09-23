#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jint;
using Jint.Native;
using Jint.Runtime;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Automation;

public interface IScriptRunner
{
    AutomationExecutionResult Execute(AutomationScript script, Torrent? torrent = null, List<string>? torrentTags = null, Dictionary<string, object>? customInputs = null);
}

public class JintScriptRunner : IScriptRunner
{
    private readonly IManageCommandQueue? _commandQueue;
    private readonly IConfigFileProvider? _configFileProvider;
    private readonly IConfigService? _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public JintScriptRunner(IManageCommandQueue? commandQueue = null, IConfigFileProvider? configFileProvider = null, IConfigService? configService = null)
    {
        _commandQueue = commandQueue;
        _configFileProvider = configFileProvider;
        _configService = configService;
    }

    public AutomationExecutionResult Execute(
        AutomationScript script,
        Torrent? torrent = null,
        List<string>? torrentTags = null,
        Dictionary<string, object>? customInputs = null)
    {
        var result = new AutomationExecutionResult();
        var logBuilder = new StringBuilder();
        var sw = Stopwatch.StartNew();

        try
        {
            var engine = new Engine(options =>
            {
                options.LimitMemory(32 * 1024 * 1024);
                options.TimeoutInterval(TimeSpan.FromSeconds(15));
                options.LimitRecursion(64);
            });

            // Console logging
            engine.SetValue("console", new ScriptConsoleContext(logBuilder));

            // HTTP context (strict sandboxing: no loopback, no private networks)
            var httpContext = new ScriptHttpContext(allowLoopback: false, allowPrivateNetworks: false);
            engine.SetValue("http", httpContext);

            // API context (local pre-authenticated with loopback allowed for internal Seedarr API)
            var apiHttpContext = new ScriptHttpContext(allowLoopback: true, allowPrivateNetworks: false);
            var apiContext = new ScriptApiContext(_configFileProvider, apiHttpContext);
            engine.SetValue("api", apiContext);

            // System / Command context
            var allowedDir = _configService?.CustomScriptsDirectory;
            var allowExtScripts = _configService?.AllowExternalScriptsInAutomation ?? false;
            var systemContext = new ScriptSystemContext(
                _commandQueue,
                result,
                allowExternalScripts: allowExtScripts,
                allowedScriptDirectory: allowedDir);
            engine.SetValue("system", systemContext);

            // HTML context
            var htmlContext = new ScriptHtmlContext();
            engine.SetValue("html", htmlContext);

            // Sleep helper (clamped to max 2s, non-starving)
            engine.SetValue("sleep", new Action<int>(ms =>
            {
                var clamped = Math.Clamp(ms, 0, 2000);
                if (clamped > 0)
                {
                    Task.Delay(clamped).Wait();
                }
            }));

            // Inputs / Secrets
            var mergedInputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(script.InputsJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(script.InputsJson);
                    if (parsed != null)
                    {
                        foreach (var kvp in parsed)
                        {
                            mergedInputs[kvp.Key] = JsonElementToObject(kvp.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to parse InputsJson for script {0}", script.Name);
                }
            }

            if (customInputs != null)
            {
                foreach (var kvp in customInputs)
                {
                    mergedInputs[kvp.Key] = kvp.Value;
                }
            }

            engine.SetValue("inputs", mergedInputs);
            engine.SetValue("secrets", mergedInputs);

            // Torrent context
            if (torrent != null)
            {
                var torrentContext = new ScriptTorrentContext(torrent, result, torrentTags);
                engine.SetValue("torrent", torrentContext);
            }
            else
            {
                engine.SetValue("torrent", (object?)null);
            }

            engine.Execute(script.Code ?? string.Empty);

            result.Success = true;
        }
        catch (JavaScriptException jse)
        {
            result.Success = false;
            result.Error = $"JavaScript Error: {jse.Message}";
            logBuilder.AppendLine($"[ERROR] {result.Error}");
        }
        catch (TimeoutException)
        {
            result.Success = false;
            result.Error = "Script execution timed out (limit: 15s)";
            logBuilder.AppendLine("[ERROR] Execution timed out.");
        }
        catch (MemoryLimitExceededException)
        {
            result.Success = false;
            result.Error = "Script exceeded memory limit (32MB)";
            logBuilder.AppendLine("[ERROR] Memory limit exceeded.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = $"Execution error: {ex.Message}";
            logBuilder.AppendLine($"[ERROR] {ex.Message}");
        }
        finally
        {
            sw.Stop();
            result.ExecutionTimeMs = sw.ElapsedMilliseconds;
            result.OutputLog = logBuilder.ToString();
        }

        return result;
    }

    private static void AppendLog(StringBuilder sb, string level, object[] args)
    {
        var line = new StringBuilder();
        line.Append($"[{DateTime.UtcNow:HH:mm:ss}] [{level}] ");
        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                line.Append(' ');
            }

            var arg = args[i];
            if (arg is JsValue jsVal)
            {
                line.Append(jsVal.ToString());
            }
            else
            {
                line.Append(arg?.ToString() ?? "null");
            }
        }

        sb.AppendLine(line.ToString());
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.ToString(),
            JsonValueKind.Object => element.ToString(),
            _ => element.ToString(),
        };
    }
}

#pragma warning disable SA1300 // Element should begin with upper-case letter (DSL wrapper)
public class ScriptConsoleContext
{
    public const int MaxLogLines = 1000;
    public const int MaxLogCharacters = 65536; // 64 KB
    public const string TruncationWarning = "\n[WARN] Log truncated: maximum output limit reached.";

    private readonly StringBuilder _logBuilder;
    private int _lineCount;
    private bool _isTruncated;

    public ScriptConsoleContext(StringBuilder logBuilder)
    {
        _logBuilder = logBuilder;
    }

    public void log(params object?[] args) => AppendLog("INFO", args);

    public void info(params object?[] args) => AppendLog("INFO", args);

    public void warn(params object?[] args) => AppendLog("WARN", args);

    public void error(params object?[] args) => AppendLog("ERROR", args);

    private void AppendLog(string level, object?[] args)
    {
        if (_isTruncated)
        {
            return;
        }

        if (_lineCount >= MaxLogLines || _logBuilder.Length >= MaxLogCharacters)
        {
            _isTruncated = true;
            _logBuilder.Append(TruncationWarning);
            return;
        }

        var line = new StringBuilder();
        line.Append($"[{DateTime.UtcNow:HH:mm:ss}] [{level}] ");
        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                line.Append(' ');
            }

            var arg = args[i];
            if (arg is JsValue jsVal)
            {
                line.Append(jsVal.ToString());
            }
            else
            {
                line.Append(arg?.ToString() ?? "null");
            }
        }

        if (_logBuilder.Length + line.Length + Environment.NewLine.Length > MaxLogCharacters)
        {
            var remaining = MaxLogCharacters - _logBuilder.Length;
            if (remaining > 0)
            {
                var lineStr = line.ToString();
                if (lineStr.Length > remaining)
                {
                    lineStr = lineStr.Substring(0, remaining);
                }

                _logBuilder.Append(lineStr);
            }

            _isTruncated = true;
            _logBuilder.Append(TruncationWarning);
            return;
        }

        _lineCount++;
        _logBuilder.AppendLine(line.ToString());

        if (_lineCount >= MaxLogLines || _logBuilder.Length >= MaxLogCharacters)
        {
            _isTruncated = true;
            _logBuilder.Append(TruncationWarning);
        }
    }
}
#pragma warning restore SA1300
