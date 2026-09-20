using System;
using System.Data;
using System.IO;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Datastore;

public interface IMainDatabase : IDatabase
{
    void Checkpoint();

    DatabaseMaintenanceResult Checkpoint(WalCheckpointMode mode);

    void Vacuum();

    long GetFreelistCount();

    long GetPageSize();

    int GetAutoVacuumStatus();

    void IncrementalVacuum(int maxPages = 5000);
}

public class MainDatabase : IMainDatabase
{
    private const string DbFileName = "seedarr.db";
    private const string ConfigFileName = "config.xml";

    private readonly IDatabase _database;
    private readonly Logger _logger;

    internal Action<string, string, bool> FileMoveAction { get; set; } = (source, destination, overwrite) => File.Move(source, destination, overwrite);
    internal Action<string, string, bool> FileCopyAction { get; set; } = (source, destination, overwrite) => File.Copy(source, destination, overwrite);
    internal Action<string> FileDeleteAction { get; set; } = path => File.Delete(path);

    public MainDatabase(IDbFactory dbFactory, IConnectionStringFactory connectionStringFactory, IAppFolderInfo appFolderInfo)
        : this(dbFactory, connectionStringFactory, appFolderInfo, null, null, null)
    {
    }

    internal MainDatabase(
        IDbFactory dbFactory,
        IConnectionStringFactory connectionStringFactory,
        IAppFolderInfo appFolderInfo,
        Action<string, string, bool> fileMove,
        Action<string, string, bool> fileCopy = null,
        Action<string> fileDelete = null)
    {
        _logger = LogManager.GetCurrentClassLogger();

        if (fileMove != null)
        {
            FileMoveAction = fileMove;
        }

        if (fileCopy != null)
        {
            FileCopyAction = fileCopy;
        }

        if (fileDelete != null)
        {
            FileDeleteAction = fileDelete;
        }

        if (!string.IsNullOrWhiteSpace(appFolderInfo?.AppDataFolder))
        {
            ApplyPendingRestore(appFolderInfo.AppDataFolder);
        }

        _database = dbFactory.Create(
            connectionStringFactory.DatabaseType,
            connectionStringFactory.MainDbConnectionString);
    }

    public IDbConnection OpenConnection() => _database.OpenConnection();
    public DatabaseType DatabaseType => _database.DatabaseType;
    public Version Version => _database.Version;

    public void Checkpoint()
    {
        Checkpoint(WalCheckpointMode.Truncate);
    }

    public DatabaseMaintenanceResult Checkpoint(WalCheckpointMode mode)
    {
        var dbTypeStr = DatabaseType.ToString();
        if (DatabaseType != DatabaseType.SQLite)
        {
            return new DatabaseMaintenanceResult
            {
                Success = true,
                DatabaseType = dbTypeStr,
                CheckpointMode = mode,
                Message = $"{dbTypeStr} database does not use WAL mode."
            };
        }

        try
        {
            using var conn = _database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA wal_checkpoint({mode.ToString().ToUpperInvariant()});";
            using var reader = cmd.ExecuteReader();
            var busy = 0;
            var log = 0;
            var checkpointed = 0;
            if (reader.Read())
            {
                busy = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                log = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                checkpointed = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
            }

            var success = busy == 0;
            var message = success
                ? $"WAL checkpoint ({mode}) succeeded. Log pages: {log}, Checkpointed: {checkpointed}."
                : $"WAL checkpoint ({mode}) busy. Log pages: {log}, Checkpointed: {checkpointed}.";

            if (success)
            {
                _logger.Debug(message);
            }
            else
            {
                _logger.Warn(message);
            }

            return new DatabaseMaintenanceResult
            {
                Success = success,
                DatabaseType = dbTypeStr,
                Busy = busy,
                WalLogPages = log,
                WalCheckpointedPages = checkpointed,
                CheckpointMode = mode,
                Message = message
            };
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to execute SQLite WAL checkpoint ({0})", mode);
            return new DatabaseMaintenanceResult
            {
                Success = false,
                DatabaseType = dbTypeStr,
                CheckpointMode = mode,
                Message = ex.Message
            };
        }
    }

    public void Optimize()
    {
        if (DatabaseType == DatabaseType.SQLite)
        {
            try
            {
                _database.Optimize();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to execute SQLite PRAGMA optimize");
            }
        }
    }

    public void Vacuum()
    {
        Optimize();
        Checkpoint();
        if (DatabaseType == DatabaseType.SQLite)
        {
            try
            {
                using var conn = _database.OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM;";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to execute SQLite VACUUM");
            }
        }
    }

    public long GetFreelistCount()
    {
        if (DatabaseType != DatabaseType.SQLite)
        {
            return 0;
        }

        try
        {
            using var conn = _database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA freelist_count;";
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to query SQLite freelist_count");
            return 0;
        }
    }

    public long GetPageSize()
    {
        if (DatabaseType != DatabaseType.SQLite)
        {
            return 4096;
        }

        try
        {
            using var conn = _database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA page_size;";
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? 4096 : Convert.ToInt64(result);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to query SQLite page_size");
            return 4096;
        }
    }

    public int GetAutoVacuumStatus()
    {
        if (DatabaseType != DatabaseType.SQLite)
        {
            return 0;
        }

        try
        {
            using var conn = _database.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA auto_vacuum;";
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to query SQLite auto_vacuum status");
            return 0;
        }
    }

    public void IncrementalVacuum(int maxPages = 5000)
    {
        if (DatabaseType != DatabaseType.SQLite)
        {
            return;
        }

        try
        {
            using var conn = _database.OpenConnection();
            using var cmd = conn.CreateCommand();

            var autoVacuum = GetAutoVacuumStatus();
            if (autoVacuum != 2)
            {
                _logger.Info("SQLite auto_vacuum is not INCREMENTAL ({0}); converting database...", autoVacuum);
                cmd.CommandText = "PRAGMA auto_vacuum = INCREMENTAL; VACUUM;";
                cmd.ExecuteNonQuery();
                return;
            }

            if (maxPages > 0)
            {
                cmd.CommandText = $"PRAGMA incremental_vacuum({maxPages});";
            }
            else
            {
                cmd.CommandText = "PRAGMA incremental_vacuum;";
            }

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to execute SQLite incremental_vacuum");
            throw;
        }
    }

    internal void ApplyPendingRestore(string appDataFolder)
    {
        var dbPath = Path.Combine(appDataFolder, DbFileName);
        var dbRestorePath = dbPath + ".restore";
        var configPath = Path.Combine(appDataFolder, ConfigFileName);
        var configRestorePath = configPath + ".restore";

        if (File.Exists(dbRestorePath))
        {
            ApplyPendingDatabaseRestore(appDataFolder, dbPath, dbRestorePath);
        }

        if (File.Exists(configRestorePath))
        {
            ApplyPendingConfigRestore(appDataFolder, configPath, configRestorePath);
        }
    }

    private void ApplyPendingDatabaseRestore(string appDataFolder, string dbPath, string dbRestorePath)
    {
        _logger.Warn("Pending database restore found at {0}; applying before opening connections", dbRestorePath);

        string backupPath = null;
        string walBakPath = null;
        string shmBakPath = null;
        var walPath = dbPath + "-wal";
        var shmPath = dbPath + "-shm";
        string walRestorePath = null;
        string shmRestorePath = null;
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        try
        {
            if (File.Exists(dbPath))
            {
                backupPath = Path.Combine(appDataFolder, $"{DbFileName}.bak-{timestamp}");
                FileCopyAction(dbPath, backupPath, true);
                _logger.Info("Created backup of existing database at {0}", backupPath);
            }

            if (File.Exists(walPath))
            {
                walBakPath = $"{walPath}.bak-{timestamp}";
                MoveWithFallback(walPath, walBakPath);
                _logger.Info("Staged existing WAL file to {0} prior to restore", walBakPath);
            }

            if (File.Exists(shmPath))
            {
                shmBakPath = $"{shmPath}.bak-{timestamp}";
                MoveWithFallback(shmPath, shmBakPath);
                _logger.Info("Staged existing SHM file to {0} prior to restore", shmBakPath);
            }

            if (File.Exists(walPath + ".restore"))
            {
                walRestorePath = walPath + ".restore";
            }
            else if (File.Exists(dbRestorePath + "-wal"))
            {
                walRestorePath = dbRestorePath + "-wal";
            }

            if (File.Exists(shmPath + ".restore"))
            {
                shmRestorePath = shmPath + ".restore";
            }
            else if (File.Exists(dbRestorePath + "-shm"))
            {
                shmRestorePath = dbRestorePath + "-shm";
            }

            MoveWithFallback(dbRestorePath, dbPath);
            _logger.Info("Database restore applied successfully from {0}", dbRestorePath);

            if (walRestorePath != null)
            {
                MoveWithFallback(walRestorePath, walPath);
                _logger.Info("Database WAL restore applied successfully from {0}", walRestorePath);
            }

            if (shmRestorePath != null)
            {
                MoveWithFallback(shmRestorePath, shmPath);
                _logger.Info("Database SHM restore applied successfully from {0}", shmRestorePath);
            }

            if (walBakPath != null && File.Exists(walBakPath))
            {
                try
                {
                    FileDeleteAction(walBakPath);
                    _logger.Info("Cleaned up WAL staging backup {0}", walBakPath);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to clean up WAL staging backup {0}", walBakPath);
                }
            }

            if (shmBakPath != null && File.Exists(shmBakPath))
            {
                try
                {
                    FileDeleteAction(shmBakPath);
                    _logger.Info("Cleaned up SHM staging backup {0}", shmBakPath);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to clean up SHM staging backup {0}", shmBakPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to apply pending database restore from {0}; original database retained", dbRestorePath);

            if (backupPath != null && File.Exists(backupPath))
            {
                try
                {
                    FileCopyAction(backupPath, dbPath, true);
                    _logger.Info("Rolled back original database from {0} to {1}", backupPath, dbPath);
                }
                catch (Exception rollbackEx)
                {
                    _logger.Error(rollbackEx, "Failed to roll back original database from {0}", backupPath);
                }
            }

            if (walBakPath != null && File.Exists(walBakPath))
            {
                try
                {
                    MoveWithFallback(walBakPath, walPath);
                    _logger.Info("Restored original WAL file from {0} to {1}", walBakPath, walPath);
                }
                catch (Exception walEx)
                {
                    _logger.Error(walEx, "Failed to restore WAL file from {0}", walBakPath);
                }
            }

            if (shmBakPath != null && File.Exists(shmBakPath))
            {
                try
                {
                    MoveWithFallback(shmBakPath, shmPath);
                    _logger.Info("Restored original SHM file from {0} to {1}", shmBakPath, shmPath);
                }
                catch (Exception shmEx)
                {
                    _logger.Error(shmEx, "Failed to restore SHM file from {0}", shmBakPath);
                }
            }

            if (File.Exists(dbRestorePath))
            {
                try
                {
                    var failedPath = dbRestorePath + ".failed";
                    FileMoveAction(dbRestorePath, failedPath, true);
                    _logger.Warn("Preserved failed database restore file at {0}", failedPath);
                }
                catch (Exception preserveEx)
                {
                    _logger.Warn(preserveEx, "Failed to rename {0} to .failed; retaining as is", dbRestorePath);
                }
            }

            if (walRestorePath != null && File.Exists(walRestorePath))
            {
                try
                {
                    var failedWalPath = walRestorePath + ".failed";
                    FileMoveAction(walRestorePath, failedWalPath, true);
                }
                catch
                {
                    // retain as is
                }
            }

            if (shmRestorePath != null && File.Exists(shmRestorePath))
            {
                try
                {
                    var failedShmPath = shmRestorePath + ".failed";
                    FileMoveAction(shmRestorePath, failedShmPath, true);
                }
                catch
                {
                    // retain as is
                }
            }
        }
    }

    private void ApplyPendingConfigRestore(string appDataFolder, string configPath, string configRestorePath)
    {
        _logger.Warn("Pending config restore found at {0}; applying before opening connections", configRestorePath);

        try
        {
            if (File.Exists(configPath))
            {
                var backupPath = Path.Combine(appDataFolder, $"{ConfigFileName}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}");
                FileCopyAction(configPath, backupPath, true);
                _logger.Info("Created backup of existing config at {0}", backupPath);
            }

            MoveWithFallback(configRestorePath, configPath);
            _logger.Info("Config restore applied successfully from {0}", configRestorePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to apply pending config restore from {0}; original config retained", configRestorePath);

            try
            {
                FileDeleteAction(configRestorePath);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private void MoveWithFallback(string sourcePath, string destinationPath)
    {
        try
        {
            FileMoveAction(sourcePath, destinationPath, true);
        }
        catch (IOException ex)
        {
            _logger.Warn(ex, "Atomic move failed from {0} to {1}; falling back to copy and delete", sourcePath, destinationPath);
            FileCopyAction(sourcePath, destinationPath, true);
            FileDeleteAction(sourcePath);
        }
    }
}
