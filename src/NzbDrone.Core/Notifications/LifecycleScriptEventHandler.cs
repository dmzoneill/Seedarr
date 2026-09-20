using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public class LifecycleScriptEventHandler :
    IHandle<TorrentAddedEvent>,
    IHandle<TorrentDownloadCompletedEvent>,
    IHandle<TorrentSeedGoalReachedEvent>,
    IHandle<TorrentDeletedEvent>,
    IHandle<HealthIssueEvent>,
    IHandle<FileMoveCompletedEvent>,
    IHandle<ApplicationUpdatedEvent>,
    IHandle<ApplicationShutdownRequested>,
    IDisposable
{
    private readonly ICustomScriptService _customScriptService;
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly SemaphoreSlim _scriptSemaphore;
    private readonly CancellationTokenSource _cts = new();
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public LifecycleScriptEventHandler(
        ICustomScriptService customScriptService,
        IConfigService configService,
        IEventAggregator eventAggregator = null,
        int maxConcurrency = 4)
    {
        _customScriptService = customScriptService;
        _configService = configService;
        _eventAggregator = eventAggregator;
        var concurrency = maxConcurrency > 0 ? maxConcurrency : 4;
        _scriptSemaphore = new SemaphoreSlim(concurrency, concurrency);
    }

    public SemaphoreSlim ScriptSemaphore => _scriptSemaphore;

    public void Handle(ApplicationShutdownRequested message)
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_configService?.ScriptTorrentAddedFilename))
        {
            ExecuteThrottledScriptAsync(_configService.ScriptTorrentAddedFilename, message.Torrent, "OnGrab");
        }
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        var script1 = _configService?.OnDownloadCompleteScript;
        var script2 = _configService?.ScriptTorrentDoneFilename;

        var hasScript1 = !string.IsNullOrWhiteSpace(script1);
        var hasScript2 = !string.IsNullOrWhiteSpace(script2);

        if (hasScript1)
        {
            ExecuteThrottledScriptAsync(script1, message.Torrent, "OnDownloadComplete");
        }

        if (hasScript2)
        {
            if (!hasScript1 || !AreSameScript(script1, script2))
            {
                ExecuteThrottledScriptAsync(script2, message.Torrent, "OnDownloadComplete");
            }
            else
            {
                _logger.Debug("Skipping duplicate lifecycle script execution for OnDownloadComplete: '{0}' and '{1}' resolve to the same script.", script1, script2);
            }
        }
    }

    public void Handle(TorrentSeedGoalReachedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        var script1 = _configService?.OnSeedGoalReachedScript;
        var script2 = _configService?.ScriptTorrentDoneSeedingFilename;

        var hasScript1 = !string.IsNullOrWhiteSpace(script1);
        var hasScript2 = !string.IsNullOrWhiteSpace(script2);

        if (hasScript1)
        {
            ExecuteThrottledScriptAsync(script1, message.Torrent, "OnSeedGoalReached");
        }

        if (hasScript2)
        {
            if (!hasScript1 || !AreSameScript(script1, script2))
            {
                ExecuteThrottledScriptAsync(script2, message.Torrent, "OnSeedGoalReached");
            }
            else
            {
                _logger.Debug("Skipping duplicate lifecycle script execution for OnSeedGoalReached: '{0}' and '{1}' resolve to the same script.", script1, script2);
            }
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        var script = !string.IsNullOrWhiteSpace(_configService?.OnTorrentDeletedScript)
            ? _configService.OnTorrentDeletedScript
            : _configService?.OnDeleteScript;

        if (!string.IsNullOrWhiteSpace(script))
        {
            ExecuteThrottledScriptAsync(script, message?.Torrent, "OnDelete");
        }

        if (!string.IsNullOrWhiteSpace(_configService?.ScriptTorrentRemovedFilename))
        {
            ExecuteThrottledScriptAsync(_configService.ScriptTorrentRemovedFilename, message?.Torrent, "TorrentRemoved");
        }
    }

    public void Handle(HealthIssueEvent message)
    {
        if (message == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_configService?.OnHealthIssueScript))
        {
            ExecuteThrottledScriptAsync(_configService.OnHealthIssueScript, message.Torrent, "OnHealthIssue");
        }
    }

    public void Handle(FileMoveCompletedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var script = !string.IsNullOrWhiteSpace(_configService?.OnFileMoveScript)
            ? _configService.OnFileMoveScript
            : _configService?.OnRenameScript;

        if (!string.IsNullOrWhiteSpace(script))
        {
            ExecuteThrottledScriptAsync(script, message.Torrent, "OnRename");
        }
    }

    public void Handle(ApplicationUpdatedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var script = !string.IsNullOrWhiteSpace(_configService?.OnApplicationUpdatedScript)
            ? _configService.OnApplicationUpdatedScript
            : _configService?.OnUpgradeScript;

        if (!string.IsNullOrWhiteSpace(script))
        {
            ExecuteThrottledScriptAsync(script, null, "OnUpgrade");
        }
    }

    public Task ExecuteThrottledScriptAsync(string scriptPath, Torrent torrent, string eventType)
    {
        if (string.IsNullOrWhiteSpace(scriptPath) || _cts.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        return Task.Run(async () =>
        {
            try
            {
                await _scriptSemaphore.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (_cts.IsCancellationRequested)
                {
                    return;
                }

                var success = await _customScriptService.ExecuteScriptAsync(scriptPath, torrent, eventType).ConfigureAwait(false);
                if (!success)
                {
                    _logger.Warn("Custom lifecycle script '{0}' for event '{1}' failed or timed out.", scriptPath, eventType);
                    if (!string.Equals(eventType, "OnHealthIssue", StringComparison.OrdinalIgnoreCase) && !_cts.IsCancellationRequested)
                    {
                        _eventAggregator?.PublishEvent(new HealthIssueEvent(
                            torrent,
                            "CustomScript",
                            $"Custom lifecycle script '{scriptPath}' for event '{eventType}' failed or timed out.",
                            isResolved: false));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Exception while executing custom lifecycle script '{0}' for event '{1}'.", scriptPath, eventType);
                if (!string.Equals(eventType, "OnHealthIssue", StringComparison.OrdinalIgnoreCase) && !_cts.IsCancellationRequested)
                {
                    _eventAggregator?.PublishEvent(new HealthIssueEvent(
                        torrent,
                        "CustomScript",
                        $"Custom lifecycle script '{scriptPath}' for event '{eventType}' threw an exception: {ex.Message}",
                        isResolved: false));
                }
            }
            finally
            {
                try
                {
                    _scriptSemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        });
    }

    internal static bool AreSameScript(string script1, string script2)
    {
        if (string.IsNullOrWhiteSpace(script1) || string.IsNullOrWhiteSpace(script2))
        {
            return false;
        }

        var norm1 = NormalizeScriptPath(script1);
        var norm2 = NormalizeScriptPath(script2);

        if (string.IsNullOrWhiteSpace(norm1) || string.IsNullOrWhiteSpace(norm2))
        {
            return false;
        }

        return string.Equals(norm1, norm2, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeScriptPath(string scriptConfig)
    {
        if (string.IsNullOrWhiteSpace(scriptConfig))
        {
            return string.Empty;
        }

        var (parsedPath, _) = CustomScriptService.ParseSettings(scriptConfig);
        var path = !string.IsNullOrWhiteSpace(parsedPath) ? parsedPath : scriptConfig;
        var clean = CustomScriptService.CleanScriptPath(path);

        if (string.IsNullOrWhiteSpace(clean))
        {
            return string.Empty;
        }

        try
        {
            clean = System.IO.Path.GetFullPath(clean);
        }
        catch
        {
            // Ignore invalid path format and keep clean
        }

        return clean.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        _cts.Dispose();
        _scriptSemaphore?.Dispose();
    }
}
