using System;
using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using Polly;

namespace NzbDrone.Core.Test.Backup;

[TestFixture]
public class BackupServiceTest
{
    private IAppFolderInfo _appFolderInfo;
    private IConnectionStringFactory _connectionStringFactory;
    private IEventAggregator _eventAggregator;
    private IConfigService _configService;
    private BackupService _subject;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_backup_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempDir);

        _connectionStringFactory = Substitute.For<IConnectionStringFactory>();
        _connectionStringFactory.DatabaseType.Returns(DatabaseType.SQLite);
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        _connectionStringFactory.MainDbConnectionString.Returns($"Data Source={dbPath}");

        _eventAggregator = Substitute.For<IEventAggregator>();

        _configService = Substitute.For<IConfigService>();
        _configService.BackupRetentionDays.Returns(28);
        _configService.MaxBackups.Returns(10);

        _subject = new BackupService(_appFolderInfo, _connectionStringFactory, _eventAggregator, _configService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private void CreateTestSqliteDatabase()
    {
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test (Id INT);";
        cmd.ExecuteNonQuery();
    }

    private byte[] CreateValidSqliteDatabaseBytes()
    {
        var testDbPath = Path.Combine(_tempDir, "temp_valid.db");
        using (var conn = new SqliteConnection($"Data Source={testDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Test (Id INT);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(testDbPath);
        File.Delete(testDbPath);
        return bytes;
    }

    [Test]
    public void CreateBackup_sqlite_should_return_null_when_db_file_not_found()
    {
        var result = _subject.CreateBackup();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void CreateBackup_sqlite_should_create_zip_when_db_exists()
    {
        CreateTestSqliteDatabase();

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Does.StartWith("seedarr_backup_"));
        Assert.That(result.Name, Does.EndWith(".zip"));
        Assert.That(File.Exists(result.Path), Is.True);

        using var zip = ZipFile.OpenRead(result.Path);
        Assert.That(zip.GetEntry("seedarr.db"), Is.Not.Null);
    }

    [Test]
    public void CreateBackup_sqlite_should_include_config_when_it_exists()
    {
        CreateTestSqliteDatabase();
        File.WriteAllText(Path.Combine(_tempDir, "config.xml"), "<config />");

        var result = _subject.CreateBackup();

        using var zip = ZipFile.OpenRead(result.Path);
        Assert.That(zip.GetEntry("config.xml"), Is.Not.Null);
        Assert.That(zip.GetEntry("seedarr.db"), Is.Not.Null);
    }

    [Test]
    public void CreateBackup_sqlite_should_work_without_config_file()
    {
        CreateTestSqliteDatabase();

        var result = _subject.CreateBackup();

        using var zip = ZipFile.OpenRead(result.Path);
        Assert.That(zip.GetEntry("seedarr.db"), Is.Not.Null);
        Assert.That(zip.GetEntry("config.xml"), Is.Null);
    }

    [Test]
    public void CreateBackup_sqlite_should_succeed_when_staging_file_already_exists()
    {
        CreateTestSqliteDatabase();
        var stagingPath = Path.Combine(_tempDir, "seedarr.db.backup-staging");
        File.WriteAllText(stagingPath, "stale staging file");

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(File.Exists(stagingPath), Is.False);
        using var zip = ZipFile.OpenRead(result.Path);
        Assert.That(zip.GetEntry("seedarr.db"), Is.Not.Null);
    }

    [Test]
    public void CreateBackup_postgres_should_create_zip_with_config_when_config_exists()
    {
        _connectionStringFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);
        _connectionStringFactory.MainDbConnectionString.Returns("Host=localhost;Database=seedarr");
        File.WriteAllText(Path.Combine(_tempDir, "config.xml"), "<config />");

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Does.StartWith("seedarr_backup_"));
        Assert.That(result.Name, Does.EndWith(".zip"));
        Assert.That(File.Exists(result.Path), Is.True);

        using var zip = ZipFile.OpenRead(result.Path);
        Assert.That(zip.GetEntry("config.xml"), Is.Not.Null);
        Assert.That(zip.GetEntry("seedarr.db"), Is.Null);
    }

    [Test]
    public void CreateBackup_postgres_should_create_zip_even_without_config_file()
    {
        _connectionStringFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);
        _connectionStringFactory.MainDbConnectionString.Returns("Host=localhost;Database=seedarr");

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(File.Exists(result.Path), Is.True);

        using var zip = ZipFile.OpenRead(result.Path);
        Assert.That(zip.GetEntry("config.xml"), Is.Null);
        Assert.That(zip.GetEntry("seedarr.db"), Is.Null);
    }

    [Test]
    public void CreateBackup_postgres_should_flag_is_config_only_and_include_manifest_with_warning()
    {
        _connectionStringFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);
        _connectionStringFactory.MainDbConnectionString.Returns("Host=localhost;Database=seedarr");
        File.WriteAllText(Path.Combine(_tempDir, "config.xml"), "<config />");

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsConfigOnly, Is.True);

        using var zip = ZipFile.OpenRead(result.Path);
        var manifestEntry = zip.GetEntry("manifest.json");
        Assert.That(manifestEntry, Is.Not.Null);

        using var stream = manifestEntry.Open();
        using var reader = new StreamReader(stream);
        var manifestJson = reader.ReadToEnd();
        Assert.That(manifestJson, Does.Contain("\"IsConfigOnly\":true"));
        Assert.That(manifestJson, Does.Contain("pg_restore"));

        var backups = _subject.GetBackups();
        Assert.That(backups, Has.Count.EqualTo(1));
        Assert.That(backups[0].IsConfigOnly, Is.True);
    }

    [Test]
    public void CreateBackup_sqlite_should_flag_is_config_only_false()
    {
        CreateTestSqliteDatabase();

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsConfigOnly, Is.False);

        var backups = _subject.GetBackups();
        Assert.That(backups, Has.Count.EqualTo(1));
        Assert.That(backups[0].IsConfigOnly, Is.False);
    }

    [Test]
    public void GetBackups_should_return_empty_list_when_folder_does_not_exist()
    {
        var result = _subject.GetBackups();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetBackups_should_return_backup_files()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "seedarr_backup_1.0_2024-01-01.zip"), "fake zip");

        var result = _subject.GetBackups();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Does.Contain("seedarr_backup_"));
    }

    [Test]
    public void DeleteBackup_should_do_nothing_when_file_not_found()
    {
        Assert.DoesNotThrow(() => _subject.DeleteBackup("nonexistent.zip"));
    }

    [Test]
    public void DeleteBackup_should_delete_the_file()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var filePath = Path.Combine(backupDir, "test.zip");
        File.WriteAllText(filePath, "content");

        _subject.DeleteBackup("test.zip");

        Assert.That(File.Exists(filePath), Is.False);
    }

    [Test]
    public void GetBackupStream_should_return_null_when_file_not_found()
    {
        var result = _subject.GetBackupStream("nonexistent.zip");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetBackupStream_should_return_stream_when_file_exists()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "test.zip"), "content");

        using var result = _subject.GetBackupStream("test.zip");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.CanRead, Is.True);
    }

    [Test]
    public void RestoreBackup_should_throw_when_file_not_found()
    {
        Assert.Throws<FileNotFoundException>(() => _subject.RestoreBackup("nonexistent.zip"));
    }

    [Test]
    public void RestoreBackup_should_extract_db_file()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "restore_test.zip");

        var dbBytes = CreateValidSqliteDatabaseBytes();

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("seedarr.db");
            using var stream = entry.Open();
            stream.Write(dbBytes, 0, dbBytes.Length);
        }

        _subject.RestoreBackup("restore_test.zip");

        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        Assert.That(File.Exists(dbRestorePath), Is.True);
        Assert.That(File.ReadAllBytes(dbRestorePath), Is.EqualTo(dbBytes));
    }

    [Test]
    public void RestoreBackup_should_throw_when_db_entry_is_too_small()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "restore_small.zip");

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("seedarr.db");
            using var stream = entry.Open();
            stream.Write(new byte[50]);
        }

        Assert.Throws<InvalidDataException>(() => _subject.RestoreBackup("restore_small.zip"));

        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        Assert.That(File.Exists(dbRestorePath), Is.False);
    }

    [Test]
    public void RestoreBackup_should_throw_when_db_entry_header_is_corrupt()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "restore_corrupt_header.zip");

        var corruptBytes = new byte[512];
        var invalidHeader = System.Text.Encoding.ASCII.GetBytes("Corrupt format 1\0");
        Array.Copy(invalidHeader, 0, corruptBytes, 0, invalidHeader.Length);

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("seedarr.db");
            using var stream = entry.Open();
            stream.Write(corruptBytes, 0, corruptBytes.Length);
        }

        Assert.Throws<InvalidDataException>(() => _subject.RestoreBackup("restore_corrupt_header.zip"));

        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        Assert.That(File.Exists(dbRestorePath), Is.False);
    }

    [Test]
    public void RestoreBackup_should_stage_config_file_without_db_file()
    {
        _connectionStringFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "restore_postgres_test.zip");

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("config.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<Config><Port>8989</Port></Config>");
        }

        _subject.RestoreBackup("restore_postgres_test.zip");

        var configPath = Path.Combine(_tempDir, "config.xml");
        var configRestorePath = Path.Combine(_tempDir, "config.xml.restore");
        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");

        Assert.That(File.Exists(configRestorePath), Is.True);
        Assert.That(File.ReadAllText(configRestorePath), Is.EqualTo("<Config><Port>8989</Port></Config>"));
        Assert.That(File.Exists(configPath), Is.False);
        Assert.That(File.Exists(dbRestorePath), Is.False);
    }

    [Test]
    public void RestoreBackup_should_stage_config_restore_and_not_overwrite_active_config()
    {
        _connectionStringFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "restore_config_stage_test.zip");

        var configPath = Path.Combine(_tempDir, "config.xml");
        File.WriteAllText(configPath, "<Config><Port>8080</Port></Config>");

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("config.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<Config><Port>9090</Port></Config>");
        }

        _subject.RestoreBackup("restore_config_stage_test.zip");

        var configRestorePath = Path.Combine(_tempDir, "config.xml.restore");

        Assert.That(File.Exists(configRestorePath), Is.True);
        Assert.That(File.ReadAllText(configRestorePath), Is.EqualTo("<Config><Port>9090</Port></Config>"));
        Assert.That(File.Exists(configPath), Is.True);
        Assert.That(File.ReadAllText(configPath), Is.EqualTo("<Config><Port>8080</Port></Config>"), "Active config.xml must not be overwritten prematurely");
    }

    [Test]
    public void RestoreBackup_should_stage_both_db_and_config_restore_files()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "restore_both_test.zip");

        var dbBytes = CreateValidSqliteDatabaseBytes();

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var dbEntry = zip.CreateEntry("seedarr.db");
            using (var stream = dbEntry.Open())
            {
                stream.Write(dbBytes, 0, dbBytes.Length);
            }

            var configEntry = zip.CreateEntry("config.xml");
            using (var writer = new StreamWriter(configEntry.Open()))
            {
                writer.Write("<Config><ApiKey>restored-key</ApiKey></Config>");
            }
        }

        var configPath = Path.Combine(_tempDir, "config.xml");
        File.WriteAllText(configPath, "<Config><ApiKey>original-key</ApiKey></Config>");

        _subject.RestoreBackup("restore_both_test.zip");

        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        var configRestorePath = Path.Combine(_tempDir, "config.xml.restore");

        Assert.That(File.Exists(dbRestorePath), Is.True);
        Assert.That(File.Exists(configRestorePath), Is.True);
        Assert.That(File.ReadAllText(configRestorePath), Is.EqualTo("<Config><ApiKey>restored-key</ApiKey></Config>"));
        Assert.That(File.ReadAllText(configPath), Is.EqualTo("<Config><ApiKey>original-key</ApiKey></Config>"));
    }

    [Test]
    public void DeleteBackup_should_strip_directory_traversal()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "safe.zip"), "content");

        Assert.DoesNotThrow(() => _subject.DeleteBackup("../../../etc/passwd"));
    }

    [Test]
    public void CreateBackup_should_publish_BackupCreatedEvent_on_success()
    {
        CreateTestSqliteDatabase();

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<BackupCreatedEvent>(e =>
            e.Type == BackupType.Manual &&
            e.FileName == result.Name &&
            e.Path == result.Path &&
            e.Size == result.Size));
    }

    [Test]
    public void CreateBackup_scheduled_should_publish_BackupCreatedEvent_with_Scheduled_type()
    {
        CreateTestSqliteDatabase();

        var result = _subject.CreateBackup(BackupType.Scheduled);

        Assert.That(result, Is.Not.Null);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<BackupCreatedEvent>(e =>
            e.Type == BackupType.Scheduled &&
            e.FileName == result.Name));
    }

    [Test]
    public void CreateBackup_should_publish_BackupFailedEvent_when_db_file_not_found()
    {
        var result = _subject.CreateBackup(BackupType.Scheduled);

        Assert.That(result, Is.Null);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<BackupFailedEvent>(e =>
            e.Type == BackupType.Scheduled &&
            e.ErrorMessage.Contains("Database file not found")));
    }

    [Test]
    public void CreateBackup_should_publish_BackupFailedEvent_and_rethrow_on_exception()
    {
        CreateTestSqliteDatabase();
        _connectionStringFactory.MainDbConnectionString.Returns("Invalid connection string ;; %%");

        Assert.Throws<ArgumentException>(() => _subject.CreateBackup(BackupType.Manual));

        _eventAggregator.Received(1).PublishEvent(Arg.Is<BackupFailedEvent>(e =>
            e.Type == BackupType.Manual &&
            e.Exception != null));
    }

    [Test]
    public void CreateBackup_should_succeed_without_event_aggregator()
    {
        CreateTestSqliteDatabase();
        var legacyService = new BackupService(_appFolderInfo, _connectionStringFactory);

        var result = legacyService.CreateBackup();

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void CreateBackup_should_throw_and_publish_BackupFailedEvent_when_disk_space_is_insufficient()
    {
        CreateTestSqliteDatabase();
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        var root = Path.GetPathRoot(Path.GetFullPath(_tempDir)) ?? "/";
        var freeSpace = new DriveInfo(root).AvailableFreeSpace;
        using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Write))
        {
            fs.SetLength(freeSpace + (100L * 1024 * 1024));
        }

        var ex = Assert.Throws<InvalidOperationException>(() => _subject.CreateBackup(BackupType.Manual));
        Assert.That(ex.Message, Does.Contain("Insufficient disk space to create backup"));

        _eventAggregator.Received(1).PublishEvent(Arg.Is<BackupFailedEvent>(e =>
            e.Type == BackupType.Manual &&
            e.ErrorMessage.Contains("Insufficient disk space") &&
            e.Exception is InvalidOperationException));
    }

    [Test]
    public void CreateBackup_sqlite_should_configure_busy_timeout_and_wal_checkpoint_pragma()
    {
        CreateTestSqliteDatabase();
        int? configuredTimeout = null;
        string executedPragmas = null;
        int? busyTimeoutPragmaValue = null;

        _subject.OnSqliteConnectionConfigured = (conn, pragmas) =>
        {
            configuredTimeout = conn.DefaultTimeout;
            executedPragmas = pragmas;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA busy_timeout;";
            busyTimeoutPragmaValue = Convert.ToInt32(cmd.ExecuteScalar());
        };

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(configuredTimeout, Is.EqualTo(30));
        Assert.That(executedPragmas, Does.Contain("PRAGMA busy_timeout = 30000;"));
        Assert.That(executedPragmas, Does.Contain("PRAGMA wal_checkpoint(PASSIVE);"));
        Assert.That(busyTimeoutPragmaValue, Is.EqualTo(30000));
    }

    [Test]
    public void CreateBackup_sqlite_should_succeed_when_database_is_in_wal_mode_with_uncheckpointed_data()
    {
        var dbPath = Path.Combine(_tempDir, "seedarr.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE TestWal (Id INT); INSERT INTO TestWal VALUES (100);";
            cmd.ExecuteNonQuery();
        }

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(File.Exists(result.Path), Is.True);

        using var zip = ZipFile.OpenRead(result.Path);
        Assert.That(zip.GetEntry("seedarr.db"), Is.Not.Null);
    }

    [Test]
    public void CreateBackup_sqlite_should_retry_and_succeed_when_database_initially_locked()
    {
        CreateTestSqliteDatabase();
        var attempts = 0;

        _subject.VacuumRetryPolicy = Policy
            .Handle<SqliteException>(ex => ex.SqliteErrorCode is 5 or 6)
            .WaitAndRetry(3, _ => TimeSpan.FromMilliseconds(1));

        _subject.ExecuteVacuumCommand = cmd =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new SqliteException("database is locked", 5);
            }

            return cmd.ExecuteNonQuery();
        };

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(attempts, Is.EqualTo(3));
    }

    [Test]
    public void CreateBackup_sqlite_should_fail_and_publish_BackupFailedEvent_when_lock_retries_exhausted()
    {
        CreateTestSqliteDatabase();
        var attempts = 0;

        _subject.VacuumRetryPolicy = Policy
            .Handle<SqliteException>(ex => ex.SqliteErrorCode is 5 or 6)
            .WaitAndRetry(2, _ => TimeSpan.FromMilliseconds(1));

        _subject.ExecuteVacuumCommand = cmd =>
        {
            attempts++;
            throw new SqliteException("database is locked", 5);
        };

        var ex = Assert.Throws<SqliteException>(() => _subject.CreateBackup(BackupType.Manual));
        Assert.That(ex.SqliteErrorCode, Is.EqualTo(5));
        Assert.That(attempts, Is.EqualTo(3));

        _eventAggregator.Received(1).PublishEvent(Arg.Is<BackupFailedEvent>(e =>
            e.Type == BackupType.Manual &&
            e.Exception is SqliteException));

        var stagingPath = Path.Combine(_tempDir, "seedarr.db.backup-staging");
        Assert.That(File.Exists(stagingPath), Is.False);
    }

    [Test]
    public void CreateBackup_should_prune_backups_when_count_exceeds_max_backups()
    {
        CreateTestSqliteDatabase();
        _configService.MaxBackups.Returns(2);
        _configService.BackupRetentionDays.Returns(0);

        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);

        var oldestPath = Path.Combine(backupDir, "seedarr_backup_1.0_2026-01-01_00-00-00-000.zip");
        var newerPath = Path.Combine(backupDir, "seedarr_backup_1.0_2026-01-02_00-00-00-000.zip");

        File.WriteAllText(oldestPath, "dummy-oldest");
        File.SetCreationTimeUtc(oldestPath, DateTime.UtcNow.AddDays(-5));
        File.SetLastWriteTimeUtc(oldestPath, DateTime.UtcNow.AddDays(-5));

        File.WriteAllText(newerPath, "dummy-newer");
        File.SetCreationTimeUtc(newerPath, DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(newerPath, DateTime.UtcNow.AddDays(-3));

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        var backups = _subject.GetBackups();
        Assert.That(backups, Has.Count.EqualTo(2));
        Assert.That(File.Exists(oldestPath), Is.False, "Oldest backup exceeding MaxBackups should be pruned");
        Assert.That(File.Exists(newerPath), Is.True);
        Assert.That(File.Exists(result.Path), Is.True);
    }

    [Test]
    public void CreateBackup_should_prune_backups_when_age_exceeds_retention_days()
    {
        CreateTestSqliteDatabase();
        _configService.MaxBackups.Returns(10);
        _configService.BackupRetentionDays.Returns(7);

        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);

        var expiredPath = Path.Combine(backupDir, "seedarr_backup_1.0_2026-01-01_00-00-00-000.zip");
        var validPath = Path.Combine(backupDir, "seedarr_backup_1.0_2026-01-08_00-00-00-000.zip");

        File.WriteAllText(expiredPath, "dummy-expired");
        File.SetCreationTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-14));
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-14));

        File.WriteAllText(validPath, "dummy-valid");
        File.SetCreationTimeUtc(validPath, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(validPath, DateTime.UtcNow.AddDays(-2));

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(File.Exists(expiredPath), Is.False, "Backup older than BackupRetentionDays should be pruned");
        Assert.That(File.Exists(validPath), Is.True);
        Assert.That(File.Exists(result.Path), Is.True);
    }

    [Test]
    public void RestoreBackup_sqlite_should_throw_when_database_file_missing_in_archive()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "restore_missing_db.zip");

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("config.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<Config />");
        }

        var ex = Assert.Throws<InvalidOperationException>(() => _subject.RestoreBackup("restore_missing_db.zip"));
        Assert.That(ex.Message, Is.EqualTo("Database file not found in backup archive"));

        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        Assert.That(File.Exists(dbRestorePath), Is.False);
    }

    [Test]
    public void RestoreBackup_should_throw_when_sqlite_header_is_invalid()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "restore_bad_header.zip");

        var badHeaderBytes = new byte[512];
        var badHeader = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\xFF");
        Array.Copy(badHeader, 0, badHeaderBytes, 0, badHeader.Length);

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("seedarr.db");
            using var stream = entry.Open();
            stream.Write(badHeaderBytes, 0, badHeaderBytes.Length);
        }

        var ex = Assert.Throws<InvalidDataException>(() => _subject.RestoreBackup("restore_bad_header.zip"));
        Assert.That(ex.Message, Does.Contain("does not contain a valid SQLite database header"));

        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        Assert.That(File.Exists(dbRestorePath), Is.False);
    }

    [Test]
    public void RestoreBackup_should_throw_when_sqlite_integrity_check_fails()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "restore_corrupted_integrity.zip");

        var validDbBytes = CreateValidSqliteDatabaseBytes();
        for (var i = 100; i < 200 && i < validDbBytes.Length; i++)
        {
            validDbBytes[i] = 0xAA;
        }

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("seedarr.db");
            using var stream = entry.Open();
            stream.Write(validDbBytes, 0, validDbBytes.Length);
        }

        var ex = Assert.Throws<InvalidDataException>(() => _subject.RestoreBackup("restore_corrupted_integrity.zip"));
        Assert.That(ex.Message, Does.Contain("SQLite integrity check failed"));

        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        Assert.That(File.Exists(dbRestorePath), Is.False);
    }
}
