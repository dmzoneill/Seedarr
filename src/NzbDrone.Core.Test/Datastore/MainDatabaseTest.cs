using System;
using System.Data;
using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class MainDatabaseTest
{
    private string _tempDir;
    private IDbFactory _dbFactory;
    private IConnectionStringFactory _connectionStringFactory;
    private IAppFolderInfo _appFolderInfo;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_db_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _dbFactory = Substitute.For<IDbFactory>();
        _connectionStringFactory = Substitute.For<IConnectionStringFactory>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();

        _connectionStringFactory.DatabaseType.Returns(DatabaseType.SQLite);
        _connectionStringFactory.MainDbConnectionString.Returns("Data Source=test.db");
        _appFolderInfo.AppDataFolder.Returns(_tempDir);
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(Substitute.For<IDatabase>());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [Test]
    public void ApplyPendingRestore_should_fallback_to_copy_and_delete_when_move_throws_ioexception()
    {
        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        const string expectedContent = "sqlite-test-data-payload";
        File.WriteAllText(dbRestorePath, expectedContent);

        var moveAttempted = false;
        var copyAttempted = false;

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo,
            fileMove: (src, dst, overwrite) =>
            {
                moveAttempted = true;
                throw new IOException("Invalid cross-device link (EXDEV)");
            },
            fileCopy: (src, dst, overwrite) =>
            {
                copyAttempted = true;
                File.Copy(src, dst, overwrite);
            });

        Assert.That(moveAttempted, Is.True, "File.Move should have been attempted");
        Assert.That(copyAttempted, Is.True, "File.Copy should have been called as fallback");
        Assert.That(File.Exists(dbPath), Is.True, "Target database file should exist");
        Assert.That(File.ReadAllText(dbPath), Is.EqualTo(expectedContent));
        Assert.That(File.Exists(dbRestorePath), Is.False, "Staged restore file should be deleted");
    }

    [Test]
    public void ApplyPendingRestore_should_fallback_to_copy_and_delete_for_wal_and_shm_restore_files()
    {
        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        var walRestorePath = Path.Combine(_tempDir, "seedarr.db-wal.restore");
        var shmRestorePath = Path.Combine(_tempDir, "seedarr.db-shm.restore");

        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        var walPath = Path.Combine(_tempDir, "seedarr.db-wal");
        var shmPath = Path.Combine(_tempDir, "seedarr.db-shm");

        File.WriteAllText(dbRestorePath, "db-content");
        File.WriteAllText(walRestorePath, "wal-content");
        File.WriteAllText(shmRestorePath, "shm-content");

        var moveCalls = 0;
        var copyCalls = 0;

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo,
            fileMove: (src, dst, overwrite) =>
            {
                moveCalls++;
                throw new IOException("Invalid cross-device link (EXDEV)");
            },
            fileCopy: (src, dst, overwrite) =>
            {
                copyCalls++;
                File.Copy(src, dst, overwrite);
            });

        Assert.That(moveCalls, Is.EqualTo(3), "Move should have been attempted for db, wal, and shm");
        Assert.That(copyCalls, Is.EqualTo(3), "Copy should have been used as fallback for db, wal, and shm");

        Assert.That(File.Exists(dbPath), Is.True);
        Assert.That(File.ReadAllText(dbPath), Is.EqualTo("db-content"));
        Assert.That(File.Exists(walPath), Is.True);
        Assert.That(File.ReadAllText(walPath), Is.EqualTo("wal-content"));
        Assert.That(File.Exists(shmPath), Is.True);
        Assert.That(File.ReadAllText(shmPath), Is.EqualTo("shm-content"));

        Assert.That(File.Exists(dbRestorePath), Is.False);
        Assert.That(File.Exists(walRestorePath), Is.False);
        Assert.That(File.Exists(shmRestorePath), Is.False);
    }

    [Test]
    public void ApplyPendingRestore_should_succeed_via_move_when_move_does_not_throw()
    {
        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        File.WriteAllText(dbRestorePath, "unmoved-content");

        var moveAttempted = false;
        var copyAttempted = false;

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo,
            fileMove: (src, dst, overwrite) =>
            {
                moveAttempted = true;
                File.Move(src, dst, overwrite);
            },
            fileCopy: (src, dst, overwrite) =>
            {
                copyAttempted = true;
                File.Copy(src, dst, overwrite);
            });

        Assert.That(moveAttempted, Is.True);
        Assert.That(copyAttempted, Is.False, "File.Copy should NOT be called when Move succeeds");
        Assert.That(File.Exists(dbPath), Is.True);
        Assert.That(File.ReadAllText(dbPath), Is.EqualTo("unmoved-content"));
        Assert.That(File.Exists(dbRestorePath), Is.False);
    }

    [Test]
    public void ApplyPendingRestore_should_preserve_restore_file_when_both_move_and_copy_fail()
    {
        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        File.WriteAllText(dbRestorePath, "restore-content");

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo,
            fileMove: (src, dst, overwrite) => throw new IOException("Cross-device link"),
            fileCopy: (src, dst, overwrite) => throw new IOException("Disk full"));

        Assert.That(File.Exists(dbPath), Is.False, "Database should not be restored");
        Assert.That(File.Exists(dbRestorePath) || File.Exists(dbRestorePath + ".failed"), Is.True, "Staged restore file should be preserved on total failure");
    }

    [Test]
    public void ApplyPendingRestore_should_preserve_wal_and_shm_and_rollback_db_on_move_failure()
    {
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        var walPath = Path.Combine(_tempDir, "seedarr.db-wal");
        var shmPath = Path.Combine(_tempDir, "seedarr.db-shm");
        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");

        File.WriteAllText(dbPath, "original-db-content");
        File.WriteAllText(walPath, "original-wal-content");
        File.WriteAllText(shmPath, "original-shm-content");
        File.WriteAllText(dbRestorePath, "new-restored-data");

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo,
            fileMove: (src, dst, overwrite) =>
            {
                if (src == dbRestorePath && dst == dbPath)
                {
                    throw new IOException("Simulated disk error during db swap");
                }

                File.Move(src, dst, overwrite);
            },
            fileCopy: (src, dst, overwrite) =>
            {
                if (src == dbRestorePath && dst == dbPath)
                {
                    throw new IOException("Simulated copy failure during fallback");
                }

                File.Copy(src, dst, overwrite);
            });

        Assert.That(File.Exists(dbPath), Is.True, "Original database file should be restored");
        Assert.That(File.ReadAllText(dbPath), Is.EqualTo("original-db-content"), "Original database content should be rolled back");
        Assert.That(File.Exists(walPath), Is.True, "Original WAL file should be preserved and restored");
        Assert.That(File.ReadAllText(walPath), Is.EqualTo("original-wal-content"));
        Assert.That(File.Exists(shmPath), Is.True, "Original SHM file should be preserved and restored");
        Assert.That(File.ReadAllText(shmPath), Is.EqualTo("original-shm-content"));

        var failedRestore = File.Exists(dbRestorePath + ".failed") || File.Exists(dbRestorePath);
        Assert.That(failedRestore, Is.True, "Restore artifact should be preserved rather than deleted");
    }

    [Test]
    public void ApplyPendingRestore_should_clean_up_staging_files_on_successful_restore()
    {
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        var walPath = Path.Combine(_tempDir, "seedarr.db-wal");
        var shmPath = Path.Combine(_tempDir, "seedarr.db-shm");
        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");

        File.WriteAllText(dbPath, "old-db-content");
        File.WriteAllText(walPath, "old-wal-content");
        File.WriteAllText(shmPath, "old-shm-content");
        File.WriteAllText(dbRestorePath, "new-db-content");

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo);

        Assert.That(File.Exists(dbPath), Is.True);
        Assert.That(File.ReadAllText(dbPath), Is.EqualTo("new-db-content"));
        Assert.That(File.Exists(walPath), Is.False, "Old WAL should not be attached to restored DB");
        Assert.That(File.Exists(shmPath), Is.False, "Old SHM should not be attached to restored DB");
        Assert.That(File.Exists(dbRestorePath), Is.False, "Restore staging file should be applied and removed");

        var walStagingFiles = Directory.GetFiles(_tempDir, "seedarr.db-wal.bak-*");
        var shmStagingFiles = Directory.GetFiles(_tempDir, "seedarr.db-shm.bak-*");
        Assert.That(walStagingFiles.Length, Is.EqualTo(0), "Temporary WAL staging backup should be cleaned up");
        Assert.That(shmStagingFiles.Length, Is.EqualTo(0), "Temporary SHM staging backup should be cleaned up");

        var dbBackups = Directory.GetFiles(_tempDir, "seedarr.db.bak-*");
        Assert.That(dbBackups.Length, Is.EqualTo(1), "Original DB backup should be retained");
        Assert.That(File.ReadAllText(dbBackups[0]), Is.EqualTo("old-db-content"));
    }

    [Test]
    public void ApplyPendingRestore_should_backup_existing_db_and_delete_stale_wal_and_shm()
    {
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        var walPath = Path.Combine(_tempDir, "seedarr.db-wal");
        var shmPath = Path.Combine(_tempDir, "seedarr.db-shm");
        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");

        File.WriteAllText(dbPath, "existing-db-data");
        File.WriteAllText(walPath, "stale-wal");
        File.WriteAllText(shmPath, "stale-shm");
        File.WriteAllText(dbRestorePath, "new-restored-data");

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo,
            fileMove: (src, dst, overwrite) => throw new IOException("Cross-device link"));

        Assert.That(File.Exists(dbPath), Is.True);
        Assert.That(File.ReadAllText(dbPath), Is.EqualTo("new-restored-data"));
        Assert.That(File.Exists(walPath), Is.False, "Stale WAL file should be removed");
        Assert.That(File.Exists(shmPath), Is.False, "Stale SHM file should be removed");

        var backups = Directory.GetFiles(_tempDir, "seedarr.db.bak-*");
        Assert.That(backups.Length, Is.EqualTo(1), "A backup of existing DB should be created");
        Assert.That(File.ReadAllText(backups[0]), Is.EqualTo("existing-db-data"));
    }

    [Test]
    public void ApplyPendingRestore_should_swap_staged_config_and_backup_original_config()
    {
        var configPath = Path.Combine(_tempDir, "config.xml");
        var configRestorePath = Path.Combine(_tempDir, "config.xml.restore");

        File.WriteAllText(configPath, "original-config-content");
        File.WriteAllText(configRestorePath, "new-restored-config-content");

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo);

        Assert.That(File.Exists(configPath), Is.True);
        Assert.That(File.ReadAllText(configPath), Is.EqualTo("new-restored-config-content"));
        Assert.That(File.Exists(configRestorePath), Is.False, "Staged config restore file should be removed");

        var backups = Directory.GetFiles(_tempDir, "config.xml.bak-*");
        Assert.That(backups.Length, Is.EqualTo(1), "A backup of existing config should be created");
        Assert.That(File.ReadAllText(backups[0]), Is.EqualTo("original-config-content"));
    }

    [Test]
    public void ApplyPendingRestore_should_swap_staged_config_when_no_previous_config_exists()
    {
        var configPath = Path.Combine(_tempDir, "config.xml");
        var configRestorePath = Path.Combine(_tempDir, "config.xml.restore");

        File.WriteAllText(configRestorePath, "fresh-restored-config-content");

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo);

        Assert.That(File.Exists(configPath), Is.True);
        Assert.That(File.ReadAllText(configPath), Is.EqualTo("fresh-restored-config-content"));
        Assert.That(File.Exists(configRestorePath), Is.False);

        var backups = Directory.GetFiles(_tempDir, "config.xml.bak-*");
        Assert.That(backups.Length, Is.EqualTo(0), "No backup should be created when no original config exists");
    }

    [Test]
    public void ApplyPendingRestore_should_fallback_to_copy_and_delete_when_config_move_throws_ioexception()
    {
        var configPath = Path.Combine(_tempDir, "config.xml");
        var configRestorePath = Path.Combine(_tempDir, "config.xml.restore");

        File.WriteAllText(configRestorePath, "config-fallback-data");

        var moveAttempted = false;
        var copyAttempted = false;

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo,
            fileMove: (src, dst, overwrite) =>
            {
                moveAttempted = true;
                throw new IOException("Invalid cross-device link (EXDEV)");
            },
            fileCopy: (src, dst, overwrite) =>
            {
                copyAttempted = true;
                File.Copy(src, dst, overwrite);
            });

        Assert.That(moveAttempted, Is.True, "File.Move should have been attempted");
        Assert.That(copyAttempted, Is.True, "File.Copy should have been called as fallback");
        Assert.That(File.Exists(configPath), Is.True);
        Assert.That(File.ReadAllText(configPath), Is.EqualTo("config-fallback-data"));
        Assert.That(File.Exists(configRestorePath), Is.False);
    }

    [Test]
    public void ApplyPendingRestore_should_clean_up_config_restore_file_when_both_move_and_copy_fail()
    {
        var configPath = Path.Combine(_tempDir, "config.xml");
        var configRestorePath = Path.Combine(_tempDir, "config.xml.restore");

        File.WriteAllText(configPath, "original-config-untouched");
        File.WriteAllText(configRestorePath, "corrupted-restore-data");

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo,
            fileMove: (src, dst, overwrite) => throw new IOException("Cross-device link"),
            fileCopy: (src, dst, overwrite) => throw new IOException("Disk full"));

        Assert.That(File.ReadAllText(configPath), Is.EqualTo("original-config-untouched"), "Original config should be retained");
        Assert.That(File.Exists(configRestorePath), Is.False, "Staged config restore file should be cleaned up on total failure");
    }

    [Test]
    public void ApplyPendingRestore_should_restore_both_db_and_config_simultaneously()
    {
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        var configPath = Path.Combine(_tempDir, "config.xml");
        var configRestorePath = Path.Combine(_tempDir, "config.xml.restore");

        File.WriteAllText(dbPath, "old-db");
        File.WriteAllText(dbRestorePath, "new-db");
        File.WriteAllText(configPath, "old-config");
        File.WriteAllText(configRestorePath, "new-config");

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo);

        Assert.That(File.ReadAllText(dbPath), Is.EqualTo("new-db"));
        Assert.That(File.ReadAllText(configPath), Is.EqualTo("new-config"));
        Assert.That(File.Exists(dbRestorePath), Is.False);
        Assert.That(File.Exists(configRestorePath), Is.False);

        var dbBackups = Directory.GetFiles(_tempDir, "seedarr.db.bak-*");
        var configBackups = Directory.GetFiles(_tempDir, "config.xml.bak-*");
        Assert.That(dbBackups.Length, Is.EqualTo(1));
        Assert.That(File.ReadAllText(dbBackups[0]), Is.EqualTo("old-db"));
        Assert.That(configBackups.Length, Is.EqualTo(1));
        Assert.That(File.ReadAllText(configBackups[0]), Is.EqualTo("old-config"));
    }

    [Test]
    public void ApplyPendingRestore_should_run_for_postgres_when_config_restore_exists()
    {
        _connectionStringFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);
        _connectionStringFactory.MainDbConnectionString.Returns("Host=localhost;Database=seedarr");

        var configPath = Path.Combine(_tempDir, "config.xml");
        var configRestorePath = Path.Combine(_tempDir, "config.xml.restore");

        File.WriteAllText(configPath, "postgres-old-config");
        File.WriteAllText(configRestorePath, "postgres-new-config");

        var database = new MainDatabase(
            _dbFactory,
            _connectionStringFactory,
            _appFolderInfo);

        Assert.That(File.ReadAllText(configPath), Is.EqualTo("postgres-new-config"));
        Assert.That(File.Exists(configRestorePath), Is.False);

        var configBackups = Directory.GetFiles(_tempDir, "config.xml.bak-*");
        Assert.That(configBackups.Length, Is.EqualTo(1));
        Assert.That(File.ReadAllText(configBackups[0]), Is.EqualTo("postgres-old-config"));
    }

    [Test]
    public void Optimize_should_delegate_to_underlying_database()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.DatabaseType.Returns(DatabaseType.SQLite);
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(mockDb);

        var mainDatabase = new MainDatabase(_dbFactory, _connectionStringFactory, _appFolderInfo);
        mainDatabase.Optimize();

        mockDb.Received(1).Optimize();
    }

    [Test]
    public void Optimize_should_swallow_exception_when_underlying_database_fails()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.DatabaseType.Returns(DatabaseType.SQLite);
        mockDb.When(x => x.Optimize()).Do(_ => throw new InvalidOperationException("disk error"));
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(mockDb);

        var mainDatabase = new MainDatabase(_dbFactory, _connectionStringFactory, _appFolderInfo);

        Assert.DoesNotThrow(() => mainDatabase.Optimize());
    }

    [Test]
    public void Vacuum_should_execute_optimize_before_checkpoint()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.DatabaseType.Returns(DatabaseType.SQLite);
        var mockConn = Substitute.For<IDbConnection>();
        var mockCmd = Substitute.For<IDbCommand>();
        mockConn.CreateCommand().Returns(mockCmd);
        mockDb.OpenConnection().Returns(mockConn);
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(mockDb);

        var mainDatabase = new MainDatabase(_dbFactory, _connectionStringFactory, _appFolderInfo);
        mainDatabase.Vacuum();

        mockDb.Received(1).Optimize();
    }
}
