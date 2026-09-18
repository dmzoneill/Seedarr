using System;
using System.Data;
using System.IO;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Datastore;

public interface IMainDatabase : IDatabase
{
    void Checkpoint();

    void Vacuum();
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
        if (DatabaseType == DatabaseType.SQLite)
        {
            try
            {
                using var conn = _database.OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to execute SQLite WAL checkpoint");
            }
        }
    }

    public void Vacuum()
    {
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

        string walRestorePath = null;
        string shmRestorePath = null;

        try
        {
            if (File.Exists(dbPath))
            {
                var backupPath = Path.Combine(appDataFolder, $"{DbFileName}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}");
                FileCopyAction(dbPath, backupPath, true);
                _logger.Info("Created backup of existing database at {0}", backupPath);
            }

            var walPath = dbPath + "-wal";
            if (File.Exists(walPath))
            {
                FileDeleteAction(walPath);
                _logger.Info("Deleted WAL file {0} prior to restore", walPath);
            }

            var shmPath = dbPath + "-shm";
            if (File.Exists(shmPath))
            {
                FileDeleteAction(shmPath);
                _logger.Info("Deleted SHM file {0} prior to restore", shmPath);
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
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to apply pending database restore from {0}; original database retained", dbRestorePath);

            try
            {
                FileDeleteAction(dbRestorePath);
            }
            catch
            {
                // best-effort cleanup
            }

            if (walRestorePath != null)
            {
                try
                {
                    FileDeleteAction(walRestorePath);
                }
                catch
                {
                    // best-effort cleanup
                }
            }

            if (shmRestorePath != null)
            {
                try
                {
                    FileDeleteAction(shmRestorePath);
                }
                catch
                {
                    // best-effort cleanup
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
