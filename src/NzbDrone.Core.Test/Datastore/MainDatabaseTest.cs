using System;
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
    public void ApplyPendingRestore_should_clean_up_restore_file_when_both_move_and_copy_fail()
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
        Assert.That(File.Exists(dbRestorePath), Is.False, "Staged restore file should be cleaned up on total failure");
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
}
