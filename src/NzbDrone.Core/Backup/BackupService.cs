using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Data.Sqlite;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
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
    private static readonly byte[] SqliteMagicHeader = { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x20, 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x20, 0x33, 0x00 };

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
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public BackupService(IAppFolderInfo appFolderInfo, IConnectionStringFactory connectionStringFactory)
        : this(appFolderInfo, connectionStringFactory, null, null)
    {
    }

    public BackupService(
        IAppFolderInfo appFolderInfo,
        IConnectionStringFactory connectionStringFactory,
        IEventAggregator eventAggregator)
        : this(appFolderInfo, connectionStringFactory, eventAggregator, null)
    {
    }

    public BackupService(
        IAppFolderInfo appFolderInfo,
        IConnectionStringFactory connectionStringFactory,
        IConfigService configService)
        : this(appFolderInfo, connectionStringFactory, null, configService)
    {
    }

    public BackupService(
        IAppFolderInfo appFolderInfo,
        IConnectionStringFactory connectionStringFactory,
        IEventAggregator eventAggregator,
        IConfigService configService)
    {
        _appFolderInfo = appFolderInfo;
        _connectionStringFactory = connectionStringFactory;
        _eventAggregator = eventAggregator;
        _configService = configService;
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

            PruneBackups();

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

        var isSqlite = _connectionStringFactory == null || _connectionStringFactory.DatabaseType == DatabaseType.SQLite;
        var dbEntry = zip.GetEntry(DbFileName);

        if (isSqlite && dbEntry == null)
        {
            throw new InvalidOperationException("Database file not found in backup archive");
        }

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

                    if (read < 16 || !headerBytes.SequenceEqual(SqliteMagicHeader))
                    {
                        throw new InvalidDataException($"Database entry in backup archive '{fileName}' does not contain a valid SQLite database header.");
                    }
                }

                VerifySqliteIntegrity(tempRestorePath, fileName);

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

    private void PruneBackups()
    {
        try
        {
            var maxBackups = _configService?.MaxBackups ?? 10;
            var retentionDays = _configService?.BackupRetentionDays ?? 28;

            var allBackups = GetBackups();
            var toDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (maxBackups > 0 && allBackups.Count > maxBackups)
            {
                foreach (var backup in allBackups.Skip(maxBackups))
                {
                    toDelete.Add(backup.Name);
                }
            }

            if (retentionDays > 0)
            {
                var cutoffTime = DateTime.UtcNow.AddDays(-retentionDays);
                foreach (var backup in allBackups)
                {
                    if (backup.Time < cutoffTime)
                    {
                        toDelete.Add(backup.Name);
                    }
                }
            }

            foreach (var fileName in toDelete)
            {
                _logger.Info("Pruning old backup: {0}", fileName);
                DeleteBackup(fileName);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to prune old backups");
        }
    }

    private static void VerifySqliteIntegrity(string dbPath, string fileName)
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        try
        {
            using var conn = new SqliteConnection(connStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check;";
            using var reader = cmd.ExecuteReader();

            var checkResults = new List<string>();
            while (reader.Read())
            {
                checkResults.Add(reader.GetString(0));
            }

            if (checkResults.Count == 0 || !checkResults.All(r => string.Equals(r, "ok", StringComparison.OrdinalIgnoreCase)))
            {
                var error = checkResults.Count == 0 ? "No result returned" : string.Join("; ", checkResults);
                throw new InvalidDataException($"SQLite integrity check failed for backup archive '{fileName}': {error}");
            }
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException($"SQLite integrity check failed for backup archive '{fileName}': {ex.Message}", ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }
}

public class BackupInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    public long Size { get; set; }
    public DateTime Time { get; set; }
}
