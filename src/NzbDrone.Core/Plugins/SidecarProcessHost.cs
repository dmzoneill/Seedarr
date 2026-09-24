using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Mcp;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Processes;

namespace NzbDrone.Core.Plugins;

public class SidecarProcessHost : ISidecarProcessHost
{
    private readonly PluginManifest _manifest;
    private readonly string _pluginDirectory;
    private readonly ISidecarProcessSupervisor _supervisor;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly object _stateLock = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
    private readonly List<DateTime> _crashTimestamps = new();

    private Process _process;
    private StreamWriter _stdinWriter;
    private CancellationTokenSource _ioCts;
    private CancellationTokenSource _restartCts;
    private long _requestIdCounter;
    private bool _isStopping;
    private bool _disposed;

    public SidecarProcessHost(
        PluginManifest manifest,
        string pluginDirectory,
        ISidecarProcessSupervisor supervisor = null)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _pluginDirectory = pluginDirectory ?? throw new ArgumentNullException(nameof(pluginDirectory));
        _supervisor = supervisor;

        PluginInfo = new PluginInfo
        {
            Manifest = manifest,
            State = PluginState.Stopped,
            Enabled = false,
            PluginDirectory = pluginDirectory
        };
    }

    public event EventHandler<(string Method, string RawMessage)> NotificationReceived;

    public PluginInfo PluginInfo { get; }

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _process != null && !_process.HasExited && PluginInfo.State == PluginState.Running;
            }
        }
    }

    public Func<ProcessStartInfo, Process> ProcessStarter { get; set; } = psi => Process.Start(psi);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SidecarProcessHost));
            }

            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            PluginInfo.State = PluginState.Starting;

            try
            {
                var entrypointPath = _manifest.ValidateAndResolveEntrypoint(_pluginDirectory);
                var (fileName, arguments) = ResolveStartInfo(entrypointPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = _pluginDirectory,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _process = ProcessStarter(startInfo);
                if (_process == null)
                {
                    throw new InvalidOperationException($"Failed to launch sidecar process for plugin '{_manifest.Id}'.");
                }

                _stdinWriter = _process.StandardInput;
                PluginInfo.ProcessId = _process.Id;
                PluginInfo.StartedAt = DateTime.UtcNow;
                PluginInfo.State = PluginState.Running;

                _supervisor?.RegisterProcess(_process);

                try
                {
                    _process.EnableRaisingEvents = true;
                    _process.Exited += OnProcessExited;
                }
                catch (Exception ex)
                {
                    _logger.Trace(ex, "Could not attach Exited event to plugin process {0}", _manifest.Id);
                }

                _ioCts = new CancellationTokenSource();
                var ioToken = _ioCts.Token;
                Task.Run(() => ReadStandardOutputAsync(_process.StandardOutput, ioToken), ioToken);
                Task.Run(() => ReadStandardErrorAsync(_process.StandardError, ioToken), ioToken);

                _logger.Info("Plugin '{0}' ({1}) started with PID {2}", _manifest.Name, _manifest.Id, _process.Id);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                PluginInfo.State = PluginState.Errored;
                PluginInfo.LastError = ex.Message;
                _logger.Error(ex, "Failed to start plugin '{0}'", _manifest.Id);
                return Task.CompletedTask;
            }
        }
    }

    public async Task StopAsync(TimeSpan? timeout = null)
    {
        Process processToStop;
        StreamWriter writerToClose;

        lock (_stateLock)
        {
            _isStopping = true;
            _restartCts?.Cancel();

            processToStop = _process;
            writerToClose = _stdinWriter;
        }

        if (processToStop == null || processToStop.HasExited)
        {
            lock (_stateLock)
            {
                PluginInfo.State = PluginState.Stopped;
                PluginInfo.ProcessId = null;
                _isStopping = false;
            }
            return;
        }

        try
        {
            if (writerToClose != null)
            {
                try
                {
                    var shutdownMsg = JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "shutdown" }, McpJsonOptions.Default);
                    await writerToClose.WriteLineAsync(shutdownMsg);
                    await writerToClose.FlushAsync();
                    writerToClose.Close();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to send clean shutdown message to plugin '{0}' stdin", _manifest.Id);
                }
            }

            var stopTimeout = timeout ?? TimeSpan.FromSeconds(3);
            var exited = processToStop.WaitForExit((int)stopTimeout.TotalMilliseconds);
            if (!exited && !processToStop.HasExited)
            {
                _logger.Warn("Plugin '{0}' did not exit within {1}s; terminating process...", _manifest.Id, stopTimeout.TotalSeconds);
                try
                {
                    processToStop.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error while killing process tree for plugin '{0}'", _manifest.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error stopping plugin process '{0}'", _manifest.Id);
        }
        finally
        {
            if (_supervisor != null && processToStop != null)
            {
                try
                {
                    _supervisor.UnregisterProcess(processToStop.Id);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to unregister process {0} from supervisor", processToStop.Id);
                }
            }

            _ioCts?.Cancel();

            lock (_stateLock)
            {
                PluginInfo.State = PluginState.Stopped;
                PluginInfo.ProcessId = null;
                _process = null;
                _stdinWriter = null;
                _isStopping = false;
            }
        }
    }

    public void Kill()
    {
        lock (_stateLock)
        {
            _isStopping = true;
            _restartCts?.Cancel();

            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to kill plugin process '{0}'", _manifest.Id);
                }
            }

            _ioCts?.Cancel();
            PluginInfo.State = PluginState.Stopped;
            PluginInfo.ProcessId = null;
            _process = null;
            _stdinWriter = null;
            _isStopping = false;
        }
    }

    public async Task<JsonRpcResponse> SendRequestAsync(
        string method,
        object @params = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var requestId = Interlocked.Increment(ref _requestIdCounter).ToString();
        var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        try
        {
            var request = new JsonRpcRequest
            {
                JsonRpc = "2.0",
                Id = requestId,
                Method = method,
                Params = @params != null ? JsonSerializer.SerializeToElement(@params, McpJsonOptions.Default) : null
            };

            var json = JsonSerializer.Serialize(request, McpJsonOptions.Default);

            StreamWriter writer;
            lock (_stateLock)
            {
                if (_process == null || _process.HasExited || _stdinWriter == null)
                {
                    throw new InvalidOperationException($"Plugin '{_manifest.Id}' is not running.");
                }

                writer = _stdinWriter;
            }

            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);

            var waitTimeout = timeout ?? TimeSpan.FromSeconds(10);
            using var timeoutCts = new CancellationTokenSource(waitTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }

            throw new TimeoutException($"Request '{method}' to plugin '{_manifest.Id}' timed out after {waitTimeout.TotalSeconds}s.");
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    public async Task SendNotificationAsync(
        string method,
        object @params = null,
        CancellationToken cancellationToken = default)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params
        };

        var json = JsonSerializer.Serialize(notification, McpJsonOptions.Default);

        StreamWriter writer;
        lock (_stateLock)
        {
            if (_process == null || _process.HasExited || _stdinWriter == null)
            {
                return;
            }

            writer = _stdinWriter;
        }

        try
        {
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to send notification '{0}' to plugin '{1}'", method, _manifest.Id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _restartCts?.Cancel();
        _ioCts?.Cancel();

        try
        {
            Kill();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to kill plugin process on dispose for '{0}'", _manifest.Id);
        }
    }

    internal TimeSpan CalculateBackoff(int crashCount)
    {
        if (crashCount <= 0)
        {
            return TimeSpan.Zero;
        }

        var seconds = Math.Min(30, (int)Math.Pow(2, Math.Min(crashCount - 1, 5)));
        return TimeSpan.FromSeconds(seconds);
    }

    internal TimeSpan RecordCrash(DateTime? timestamp = null)
    {
        lock (_stateLock)
        {
            var now = timestamp ?? DateTime.UtcNow;
            _crashTimestamps.Add(now);
            _crashTimestamps.RemoveAll(t => (now - t).TotalSeconds > 60);

            PluginInfo.CrashCount = _crashTimestamps.Count;
            PluginInfo.LastCrashTime = now;

            if (_crashTimestamps.Count >= 5)
            {
                PluginInfo.State = PluginState.Errored;
                PluginInfo.Enabled = false;
                PluginInfo.LastError = $"Plugin '{_manifest.Id}' exceeded maximum crash threshold (5 crashes in 60 seconds) and was disabled.";
                _logger.Error(PluginInfo.LastError);
                return TimeSpan.Zero;
            }

            PluginInfo.State = PluginState.Crashed;
            return CalculateBackoff(_crashTimestamps.Count);
        }
    }

    internal void ProcessStdoutLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idProp))
            {
                var idStr = idProp.ToString();
                if (_pendingRequests.TryRemove(idStr, out var tcs))
                {
                    var response = JsonSerializer.Deserialize<JsonRpcResponse>(line, McpJsonOptions.Default);
                    tcs.TrySetResult(response);
                }
            }
            else if (root.TryGetProperty("method", out var methodProp))
            {
                var method = methodProp.GetString();
                NotificationReceived?.Invoke(this, (method, line));
            }
        }
        catch (Exception ex)
        {
            _logger.Trace(ex, "Failed to parse JSON-RPC message from plugin '{0}': {1}", _manifest.Id, line);
        }
    }

    private (string FileName, string Arguments) ResolveStartInfo(string entrypointPath)
    {
        CustomScriptService.EnsureExecutablePermissions(entrypointPath, _logger);

        if (entrypointPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return ("dotnet", $"\"{entrypointPath}\"");
        }

        return CustomScriptService.ResolveInterpreter(entrypointPath, string.Empty);
    }

    private void OnProcessExited(object sender, EventArgs e)
    {
        lock (_stateLock)
        {
            if (_isStopping)
            {
                PluginInfo.State = PluginState.Stopped;
                PluginInfo.ProcessId = null;
                return;
            }

            PluginInfo.ProcessId = null;
        }

        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetException(new InvalidOperationException($"Plugin '{_manifest.Id}' process terminated unexpectedly."));
        }

        _pendingRequests.Clear();

        var backoff = RecordCrash();
        if (PluginInfo.State == PluginState.Errored || !PluginInfo.Enabled)
        {
            return;
        }

        _logger.Warn("Plugin '{0}' crashed (crash #{1} in 60s). Restarting in {2}s...",
            _manifest.Id, PluginInfo.CrashCount, backoff.TotalSeconds);

        _restartCts?.Cancel();
        _restartCts = new CancellationTokenSource();
        var token = _restartCts.Token;

        Task.Delay(backoff, token).ContinueWith(t =>
        {
            if (!token.IsCancellationRequested && PluginInfo.Enabled)
            {
                _ = StartAsync(token);
            }
        }, TaskScheduler.Default);
    }

    private async Task ReadStandardOutputAsync(StreamReader reader, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line == null)
                {
                    break;
                }

                ProcessStdoutLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Reading stdout canceled for plugin '{0}'", _manifest.Id);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error reading stdout from plugin '{0}'", _manifest.Id);
        }
    }

    private async Task ReadStandardErrorAsync(StreamReader reader, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line == null)
                {
                    break;
                }

                _logger.Warn("[Plugin {0} stderr] {1}", _manifest.Id, line);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Reading stderr canceled for plugin '{0}'", _manifest.Id);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error reading stderr from plugin '{0}'", _manifest.Id);
        }
    }
}
