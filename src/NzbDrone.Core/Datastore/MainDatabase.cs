using System;
using System.Data;
using System.IO;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Datastore;

public interface IMainDatabase : IDatabase
{
}

public class MainDatabase : IMainDatabase
{
    private const string DbFileName = "seedarr.db";

    private readonly IDatabase _database;
    private readonly Logger _logger;

    public MainDatabase(IDbFactory dbFactory, IConnectionStringFactory connectionStringFactory, IAppFolderInfo appFolderInfo)
    {
        _logger = LogManager.GetCurrentClassLogger();

        if (connectionStringFactory.DatabaseType == DatabaseType.SQLite)
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

    private void ApplyPendingRestore(string appDataFolder)
    {
        var dbPath = Path.Combine(appDataFolder, DbFileName);
        var dbRestorePath = dbPath + ".restore";

        if (!File.Exists(dbRestorePath))
        {
            return;
        }

        _logger.Warn("Pending database restore found at {0}; applying before opening connections", dbRestorePath);

        try
        {
            if (File.Exists(dbPath))
            {
                var backupPath = Path.Combine(appDataFolder, $"{DbFileName}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}");
                File.Copy(dbPath, backupPath, overwrite: true);
                _logger.Info("Created backup of existing database at {0}", backupPath);
            }

            var walPath = dbPath + "-wal";
            if (File.Exists(walPath))
            {
                File.Delete(walPath);
                _logger.Info("Deleted WAL file {0} prior to restore", walPath);
            }

            var shmPath = dbPath + "-shm";
            if (File.Exists(shmPath))
            {
                File.Delete(shmPath);
                _logger.Info("Deleted SHM file {0} prior to restore", shmPath);
            }

            File.Move(dbRestorePath, dbPath, overwrite: true);
            _logger.Info("Database restore applied successfully from {0}", dbRestorePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to apply pending database restore from {0}; original database retained", dbRestorePath);

            try
            {
                File.Delete(dbRestorePath);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
