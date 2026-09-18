using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Update;

public class PostUpdateVerificationService : IPostUpdateVerificationService, IHandle<ApplicationStartedEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IHealthCheckService _healthCheckService;
    private readonly IEventAggregator _eventAggregator;
    private readonly string _targetDirectory;
    private readonly string _stateFilePath;
    private readonly Logger _logger;
    private readonly object _lock = new();

    public PostUpdateVerificationService(
        IAppFolderInfo appFolderInfo = null,
        IHealthCheckService healthCheckService = null,
        IEventAggregator eventAggregator = null,
        string targetDirectory = null,
        string stateFilePath = null)
    {
        _appFolderInfo = appFolderInfo;
        _healthCheckService = healthCheckService;
        _eventAggregator = eventAggregator;
        _targetDirectory = targetDirectory ?? _appFolderInfo?.StartUpFolder ?? AppDomain.CurrentDomain.BaseDirectory;
        _stateFilePath = stateFilePath ?? Path.Combine(_appFolderInfo?.AppDataFolder ?? AppDomain.CurrentDomain.BaseDirectory, "update_state.json");
        _logger = LogManager.GetCurrentClassLogger();
    }

    public UpdateState GetUpdateState()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    var json = File.ReadAllText(_stateFilePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var state = JsonSerializer.Deserialize<UpdateState>(json, JsonOptions);
                        if (state != null)
                        {
                            return state;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to read update state from '{0}'. Falling back to default state.", _stateFilePath);
            }

            return new UpdateState
            {
                PreviousVersion = BuildInfo.Version?.ToString() ?? "1.0.0",
                State = UpdateLifecycleState.Idle,
            };
        }
    }

    public UpdateState StageUpdate(string targetVersion, string backupPath)
    {
        var state = new UpdateState
        {
            PreviousVersion = BuildInfo.Version?.ToString() ?? "1.0.0",
            TargetVersion = targetVersion ?? string.Empty,
            State = UpdateLifecycleState.Staging,
            InitiatedAt = DateTime.UtcNow,
            VerificationTimeoutSeconds = 60,
            BackupDirectory = backupPath ?? string.Empty,
            ErrorMessage = null,
        };

        SaveUpdateState(state);
        _logger.Info("Update staged for version {0} with backup path '{1}'.", targetVersion, backupPath);
        return state;
    }

    public async Task<bool> VerifyUpdateAsync()
    {
        var state = GetUpdateState();

        if (state.State == UpdateLifecycleState.Completed || state.State == UpdateLifecycleState.RolledBack)
        {
            return state.State == UpdateLifecycleState.Completed;
        }

        if (state.InitiatedAt.HasValue && state.VerificationTimeoutSeconds > 0)
        {
            var elapsed = (DateTime.UtcNow - state.InitiatedAt.Value).TotalSeconds;
            if (elapsed > state.VerificationTimeoutSeconds)
            {
                var timeoutReason = $"Verification timed out after {state.VerificationTimeoutSeconds} seconds.";
                _logger.Warn(timeoutReason);
                await RollbackAsync(timeoutReason).ConfigureAwait(false);
                return false;
            }
        }

        try
        {
            if (_healthCheckService != null)
            {
                var checkResults = _healthCheckService.PerformChecks();
                var errors = checkResults?.Where(r => r.Type == HealthCheckResultType.Error).ToList();
                if (errors != null && errors.Count > 0)
                {
                    var failureReason = string.Join("; ", errors.Select(e => $"{e.Source}: {e.Message}"));
                    _logger.Warn("Subsystem health verification failed: {0}", failureReason);
                    await RollbackAsync(failureReason).ConfigureAwait(false);
                    return false;
                }
            }

            state = state with
            {
                State = UpdateLifecycleState.Completed,
                ErrorMessage = null,
            };
            SaveUpdateState(state);

            _logger.Info("Update verification succeeded. Upgraded to version {0}.", state.TargetVersion);

            CleanupBackup(state.BackupDirectory);

            _eventAggregator?.PublishEvent(new ApplicationUpdatedEvent(state.PreviousVersion, state.TargetVersion));

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception encountered during post-update verification.");
            await RollbackAsync($"Verification exception: {ex.Message}").ConfigureAwait(false);
            return false;
        }
    }

    public Task RollbackAsync(string reason)
    {
        var state = GetUpdateState();
        _logger.Error("Rollback triggered for update to {0}. Reason: {1}", state.TargetVersion, reason);

        if (!string.IsNullOrWhiteSpace(state.BackupDirectory) && Directory.Exists(state.BackupDirectory))
        {
            try
            {
                RestoreDirectory(state.BackupDirectory, _targetDirectory);
                _logger.Info("Files successfully restored from backup directory '{0}' to '{1}'.", state.BackupDirectory, _targetDirectory);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to restore files from backup directory '{0}' to '{1}'.", state.BackupDirectory, _targetDirectory);
            }
        }
        else
        {
            _logger.Warn("Backup directory '{0}' does not exist or is not specified. Unable to restore files.", state.BackupDirectory);
        }

        state = state with
        {
            State = UpdateLifecycleState.RolledBack,
            ErrorMessage = reason,
        };
        SaveUpdateState(state);

        _eventAggregator?.PublishEvent(new HealthIssueEvent(
            (Torrent)null,
            "UpdateRollback",
            $"Application update rolled back: {reason}",
            isResolved: false));

        return Task.CompletedTask;
    }

    public void SaveUpdateState(UpdateState state)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_stateFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(state, JsonOptions);
                File.WriteAllText(_stateFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to persist update state to '{0}'.", _stateFilePath);
            }
        }
    }

    public void Handle(ApplicationStartedEvent message)
    {
        var state = GetUpdateState();
        if (state.State is UpdateLifecycleState.PendingVerification or UpdateLifecycleState.Staging or UpdateLifecycleState.Restarting)
        {
            _logger.Info("Pending update verification detected on application startup ({0} -> {1}).", state.PreviousVersion, state.TargetVersion);
            _ = Task.Run(async () =>
            {
                try
                {
                    await VerifyUpdateAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing post-update verification on application startup.");
                }
            });
        }
    }

    private static void RestoreDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, dirPath);
            Directory.CreateDirectory(Path.Combine(targetDir, relativePath));
        }

        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, filePath);
            var destFile = Path.Combine(targetDir, relativePath);
            var destDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(filePath, destFile, overwrite: true);
        }
    }

    private void CleanupBackup(string backupDir)
    {
        if (string.IsNullOrWhiteSpace(backupDir))
        {
            return;
        }

        try
        {
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, recursive: true);
                _logger.Info("Cleaned up backup directory '{0}'.", backupDir);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to cleanup backup directory '{0}'.", backupDir);
        }
    }
}
