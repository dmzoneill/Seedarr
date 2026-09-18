using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Backup;

public interface IBackupService
{
    BackupInfo CreateBackup(BackupType type = BackupType.Manual);
    List<BackupInfo> GetBackups();
    void DeleteBackup(string fileName);
    Stream GetBackupStream(string fileName);
    void RestoreBackup(string fileName);
}

public class BackupService : IBackupService
{
    private const string BackupFolderName = "Backups";
    private const string DbFileName = "seedarr.db";
    private const string ConfigFileName = "config.xml";

    private static readonly RetryPolicy DefaultVacuumRetryPolicy = Policy
        .Handle<SqliteException>(ex => ex.SqliteErrorCode is 5 or 6)
        .WaitAndRetry(
            new[]
            {
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(1000),
                TimeSpan.FromMilliseconds(2000)
            },
            (exception, timeSpan, retryCount, _) =>
            {
                LogManager.GetCurrentClassLogger().Warn(exception, "Database locked during VACUUM INTO, retrying backup attempt {0} after {1}ms", retryCount, timeSpan.TotalMilliseconds);
            });

    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IConnectionStringFactory _connectionStringFactory;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    public BackupService(IAppFolderInfo appFolderInfo, IConnectionStringFactory connectionStringFactory)
        : this(appFolderInfo, connectionStringFactory, null)
    {
    }

    public BackupService(
        IAppFolderInfo appFolderInfo,
        IConnectionStringFactory connectionStringFactory,
        IEventAggregator eventAggregator)
    {
        _appFolderInfo = appFolderInfo;
        _connectionStringFactory = connectionStringFactory;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    internal Action<SqliteConnection, string> OnSqliteConnectionConfigured { get; set; }

    internal Func<SqliteCommand, int> ExecuteVacuumCommand { get; set; } = cmd => cmd.ExecuteNonQuery();

    internal RetryPolicy VacuumRetryPolicy { get; set; } = DefaultVacuumRetryPolicy;

    public BackupInfo CreateBackup(BackupType type = BackupType.Manual)
    {
        try
        {
            var backupFolder = GetBackupFolder();
            Directory.CreateDirectory(backupFolder);

            var version = BuildInfo.Version.ToString();
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            var backupFileName = $"seedarr_backup_{version}_{timestamp}.zip";
            var backupPath = Path.Combine(backupFolder, backupFileName);
            var configPath = Path.Combine(_appFolderInfo.AppDataFolder, ConfigFileName);

            if (_connectionStringFactory.DatabaseType == DatabaseType.SQLite)
            {
                var dbPath = Path.Combine(_appFolderInfo.AppDataFolder, DbFileName);

                if (!File.Exists(dbPath))
                {
                    var msg = $"Database file not found at {dbPath}, skipping backup";
                    _logger.Warn(msg);
                    _eventAggregator?.PublishEvent(new BackupFailedEvent(type, msg, new FileNotFoundException(msg, dbPath)));
                    return null;
                }

                var dbFileInfo = new FileInfo(dbPath);
                var requiredSpace = (dbFileInfo.Length * 3) + (100 * 1024 * 1024);

                try
                {
                    var root = Path.GetPathRoot(Path.GetFullPath(_appFolderInfo.AppDataFolder)) ?? "/";
                    var drive = new DriveInfo(root);
                    if (drive.IsReady && drive.AvailableFreeSpace < requiredSpace)
                    {
                        var msg = $"Insufficient disk space to create backup. Required: {requiredSpace / (1024 * 1024)} MB, Available: {drive.AvailableFreeSpace / (1024 * 1024)} MB.";
                        _logger.Warn(msg);
                        var ex = new InvalidOperationException(msg);
                        _eventAggregator?.PublishEvent(new BackupFailedEvent(type, msg, ex));
                        throw ex;
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to determine free disk space for backup pre-flight check");
                }

                var dbStagingPath = dbPath + ".backup-staging";

                try
                {
                    if (File.Exists(dbStagingPath))
                    {
                        File.Delete(dbStagingPath);
                    }

                    var connStr = DbFactory.CleanSqliteConnectionString(_connectionStringFactory.MainDbConnectionString);
                    using var conn = new SqliteConnection(connStr);
                    conn.DefaultTimeout = 30;
                    conn.Open();

                    using (var pragmaCmd = conn.CreateCommand())
                    {
                        pragmaCmd.CommandText = "PRAGMA busy_timeout = 30000; PRAGMA wal_checkpoint(PASSIVE);";
                        pragmaCmd.ExecuteNonQuery();
                        OnSqliteConnectionConfigured?.Invoke(conn, pragmaCmd.CommandText);
                    }

                    (VacuumRetryPolicy ?? DefaultVacuumRetryPolicy).Execute(() =>
                    {
                        if (conn.State != ConnectionState.Open)
                        {
                            conn.Open();
                        }

                        if (File.Exists(dbStagingPath))
                        {
                            File.Delete(dbStagingPath);
                        }

                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $"VACUUM INTO '{dbStagingPath.Replace("'", "''")}';";
                        (ExecuteVacuumCommand ?? (c => c.ExecuteNonQuery()))(cmd);
                    });

                    using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
                    {
                        zip.CreateEntryFromFile(dbStagingPath, DbFileName);

                        if (File.Exists(configPath))
                        {
                            zip.CreateEntryFromFile(configPath, ConfigFileName);
                        }
                    }
                }
                finally
                {
                    if (File.Exists(dbStagingPath))
                    {
                        File.Delete(dbStagingPath);
                    }
                }
            }
            else
            {
                _logger.Info("Creating PostgreSQL backup: config exported (external database dump required)");

                using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
                {
                    if (File.Exists(configPath))
                    {
                        zip.CreateEntryFromFile(configPath, ConfigFileName);
                    }
                }
            }

            _logger.Info("Backup created: {0}", backupPath);

            var fileInfo = new FileInfo(backupPath);

            var backupInfo = new BackupInfo
            {
                Name = fileInfo.Name,
                Path = fileInfo.FullName,
                Size = fileInfo.Length,
                Time = fileInfo.CreationTimeUtc
            };

            _eventAggregator?.PublishEvent(new BackupCreatedEvent(backupInfo.Path, backupInfo.Name, type, backupInfo.Size));

            return backupInfo;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create backup");
            _eventAggregator?.PublishEvent(new BackupFailedEvent(type, ex.Message, ex));
            throw;
        }
    }

    public List<BackupInfo> GetBackups()
    {
        var backupFolder = GetBackupFolder();

        if (!Directory.Exists(backupFolder))
        {
            return new List<BackupInfo>();
        }

        return Directory.GetFiles(backupFolder, "seedarr_backup_*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupInfo
            {
                Name = f.Name,
                Path = f.FullName,
                Size = f.Length,
                Time = f.CreationTimeUtc
            })
            .ToList();
    }

    public void DeleteBackup(string fileName)
    {
        var filePath = GetSafeBackupPath(fileName);

        if (!File.Exists(filePath))
        {
            _logger.Warn("Backup file not found: {0}", filePath);
            return;
        }

        File.Delete(filePath);
        _logger.Info("Backup deleted: {0}", filePath);
    }

    public Stream GetBackupStream(string fileName)
    {
        var filePath = GetSafeBackupPath(fileName);

        if (!File.Exists(filePath))
        {
            return null;
        }

        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void RestoreBackup(string fileName)
    {
        var filePath = GetSafeBackupPath(fileName);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Backup file not found", fileName);
        }

        _logger.Warn("Staging restore from {0} - application restart required to complete", filePath);

        var dbPath = Path.Combine(_appFolderInfo.AppDataFolder, DbFileName);
        var dbRestorePath = dbPath + ".restore";
        var configPath = Path.Combine(_appFolderInfo.AppDataFolder, ConfigFileName);
        var configRestorePath = configPath + ".restore";

        using var zip = ZipFile.OpenRead(filePath);

        var dbEntry = zip.GetEntry(DbFileName);
        if (dbEntry != null)
        {
            if (dbEntry.Length <= 100)
            {
                throw new InvalidDataException($"Database entry in backup archive '{fileName}' is corrupt or too small ({dbEntry.Length} bytes).");
            }

            var tempRestorePath = dbRestorePath + ".tmp";
            try
            {
                dbEntry.ExtractToFile(tempRestorePath, overwrite: true);

                var fileInfo = new FileInfo(tempRestorePath);
                if (fileInfo.Length <= 100)
                {
                    throw new InvalidDataException($"Extracted database file from backup archive '{fileName}' is corrupt or too small ({fileInfo.Length} bytes).");
                }

                using (var fs = new FileStream(tempRestorePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var headerBytes = new byte[16];
                    var read = fs.Read(headerBytes, 0, headerBytes.Length);
                    var headerStr = Encoding.ASCII.GetString(headerBytes, 0, read);

                    if (!headerStr.StartsWith("SQLite format 3", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"Database entry in backup archive '{fileName}' does not contain a valid SQLite database header.");
                    }
                }

                File.Move(tempRestorePath, dbRestorePath, overwrite: true);
                _logger.Info("Database restore staged at {0}; swap will occur on next startup", dbRestorePath);
            }
            finally
            {
                if (File.Exists(tempRestorePath))
                {
                    try
                    {
                        File.Delete(tempRestorePath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        var configEntry = zip.GetEntry(ConfigFileName);
        if (configEntry != null)
        {
            configEntry.ExtractToFile(configRestorePath, overwrite: true);
            _logger.Info("Config restore staged at {0}; swap will occur on next startup", configRestorePath);
        }
    }

    private string GetBackupFolder()
    {
        return Path.Combine(_appFolderInfo.AppDataFolder, BackupFolderName);
    }

    private string GetSafeBackupPath(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        return Path.Combine(GetBackupFolder(), safeName);
    }
}

public class BackupInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    public long Size { get; set; }
    public DateTime Time { get; set; }
}
