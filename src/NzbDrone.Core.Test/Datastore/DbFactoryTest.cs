using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class DbFactoryTest
{
    private string _tempDbPath;
    private static readonly FieldInfo TypeHandlerFlagField =
        typeof(DbFactory).GetField("_typeHandlersRegistered", BindingFlags.Static | BindingFlags.NonPublic);

    [SetUp]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"seedarr_test_{Guid.NewGuid():N}.db");

        // Reset the static flag so each test starts from a known state,
        // ensuring the registration branch in Create() is exercised.
        TypeHandlerFlagField?.SetValue(null, false);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempDbPath))
        {
            File.Delete(_tempDbPath);
        }

        var dir = Path.GetDirectoryName(_tempDbPath);
        var fileName = Path.GetFileName(_tempDbPath);
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            var sidecar = _tempDbPath + suffix;
            if (File.Exists(sidecar))
            {
                try
                {
                    File.Delete(sidecar);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }

        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            foreach (var bak in Directory.GetFiles(dir, $"{fileName}.pre-migration-*.bak*"))
            {
                try
                {
                    File.Delete(bak);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }

        LogManager.Configuration = null;
    }

    [Test]
    public void Create_returns_database_with_sqlite_type()
    {
        var factory = new DbFactory();

        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        Assert.That(db.DatabaseType, Is.EqualTo(DatabaseType.SQLite));
    }

    [Test]
    public void Create_returns_database_that_can_open_connections()
    {
        var factory = new DbFactory();

        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        Assert.That(conn.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void Create_enables_wal_mode_and_busy_timeout_for_sqlite()
    {
        var factory = new DbFactory();

        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var journalMode = conn.ExecuteScalar<string>("PRAGMA journal_mode;");
        Assert.That(journalMode, Is.EqualTo("wal").IgnoreCase);

        var busyTimeout = conn.ExecuteScalar<int>("PRAGMA busy_timeout;");
        Assert.That(busyTimeout, Is.GreaterThanOrEqualTo(30000));

        var tempStore = conn.ExecuteScalar<long>("PRAGMA temp_store;");
        Assert.That(tempStore, Is.EqualTo(2));

        var mmapSize = conn.ExecuteScalar<long>("PRAGMA mmap_size;");
        Assert.That(mmapSize, Is.EqualTo(268435456));
    }

    [Test]
    public void Create_runs_migrations_and_creates_tags_table()
    {
        var factory = new DbFactory();

        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var count = conn.ExecuteScalar<int>(
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Tags'");
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void Create_runs_migrations_and_creates_multiple_expected_tables()
    {
        var factory = new DbFactory();

        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var tables = conn.Query<string>(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").ToList();

        Assert.That(tables, Does.Contain("Tags"));
        Assert.That(tables, Does.Contain("Config"));
        Assert.That(tables, Does.Contain("Commands"));
        Assert.That(tables, Does.Contain("Torrents"));
    }

    [Test]
    public void Create_registers_type_handlers_on_first_call()
    {
        TypeHandlerFlagField?.SetValue(null, false);
        var factory = new DbFactory();

        // Should not throw; the handler should be registered during this call.
        Assert.DoesNotThrow(() => factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}"));

        var flagAfter = (bool)(TypeHandlerFlagField?.GetValue(null) ?? false);
        Assert.That(flagAfter, Is.True);
    }

    [Test]
    public void Create_skips_type_handler_registration_when_already_registered()
    {
        var factory = new DbFactory();
        var connectionString = $"Data Source={_tempDbPath}";

        // First call registers handlers and sets the flag.
        factory.Create(DatabaseType.SQLite, connectionString);

        // Second call with a fresh temp DB — flag is already true, registration is skipped.
        var secondTempDb = Path.Combine(Path.GetTempPath(), $"seedarr_test2_{Guid.NewGuid():N}.db");
        try
        {
            Assert.DoesNotThrow(() => factory.Create(DatabaseType.SQLite, $"Data Source={secondTempDb}"));
        }
        finally
        {
            if (File.Exists(secondTempDb))
            {
                File.Delete(secondTempDb);
            }
        }
    }

    [Test]
    public void GetMigrationConnectionString_for_sqlite_includes_30_second_timeout()
    {
        var connStr = DbFactory.GetMigrationConnectionString(DatabaseType.SQLite, "Data Source=seedarr.db;");

        Assert.That(connStr, Does.Contain("Default Timeout=30"));
        Assert.That(connStr, Does.Contain("Foreign Keys=True"));
    }

    [Test]
    public void CleanSqliteConnectionString_preserves_default_timeout()
    {
        var input = "Data Source=seedarr.db;Default Timeout=45;Foreign Keys=True;";
        var result = DbFactory.CleanSqliteConnectionString(input);

        Assert.That(result, Does.Contain("Default Timeout=45"));
        Assert.That(result, Does.Contain("Foreign Keys=True"));
    }

    [Test]
    public void CleanSqliteConnectionString_preserves_busy_timeout_as_default_timeout()
    {
        var input = "Data Source=seedarr.db;Busy Timeout=30;";
        var result = DbFactory.CleanSqliteConnectionString(input);

        Assert.That(result, Does.Contain("Default Timeout=30"));
        Assert.That(result, Does.Not.Contain("Busy Timeout"));
    }

    [Test]
    public void CleanSqliteConnectionString_converts_millisecond_busy_timeout_to_seconds()
    {
        var input = "Data Source=seedarr.db;Busy Timeout=30000;";
        var result = DbFactory.CleanSqliteConnectionString(input);

        Assert.That(result, Does.Contain("Default Timeout=30"));
        Assert.That(result, Does.Not.Contain("Busy Timeout"));
    }

    [Test]
    public void CleanSqliteConnectionString_preserves_existing_foreign_keys_and_default_timeout_when_busy_timeout_also_present()
    {
        var input = "Data Source=seedarr.db;Cache=Shared;Busy Timeout=30;Default Timeout=30;Foreign Keys=True;";
        var result = DbFactory.CleanSqliteConnectionString(input);

        Assert.That(result, Does.Contain("Default Timeout=30"));
        Assert.That(result, Does.Contain("Foreign Keys=True"));
        Assert.That(result, Does.Not.Contain("Cache=Shared"));
        Assert.That(result, Does.Not.Contain("Busy Timeout"));
    }

    [Test]
    public void CleanSqliteConnectionString_strips_cache_shared_when_present()
    {
        var input = "Data Source=seedarr.db;Cache=Shared;Foreign Keys=True;";
        var result = DbFactory.CleanSqliteConnectionString(input);

        Assert.That(result, Does.Not.Contain("Cache=Shared"));
        Assert.That(result, Does.Contain("Data Source=seedarr.db"));
        Assert.That(result, Does.Contain("Foreign Keys=True"));
        Assert.That(result, Does.Contain("Default Timeout=30"));
    }

    [Test]
    public void CleanSqliteConnectionString_strips_cache_shared_without_trailing_semicolon()
    {
        var input = "Data Source=seedarr.db;Cache=Shared";
        var result = DbFactory.CleanSqliteConnectionString(input);

        Assert.That(result, Does.Not.Contain("Cache=Shared"));
        Assert.That(result, Does.Contain("Data Source=seedarr.db"));
        Assert.That(result, Does.Contain("Foreign Keys=True"));
        Assert.That(result, Does.Contain("Default Timeout=30"));
    }

    [Test]
    public void CleanSqliteConnectionString_ensures_foreign_keys_and_timeout_when_neither_provided()
    {
        var input = "Data Source=seedarr.db;";
        var result = DbFactory.CleanSqliteConnectionString(input);

        Assert.That(result, Does.Contain("Default Timeout=30"));
        Assert.That(result, Does.Contain("Foreign Keys=True"));
    }

    [Test]
    public void SqliteDoubleTypeHandler_SetValue_stores_double_value()
    {
        var handler = new SqliteDoubleTypeHandler();
        var param = new FakeDbParameter();

        handler.SetValue(param, 3.14159);

        Assert.That(param.Value, Is.EqualTo(3.14159));
    }

    [Test]
    public void SqliteDoubleTypeHandler_SetValue_stores_zero()
    {
        var handler = new SqliteDoubleTypeHandler();
        var param = new FakeDbParameter();

        handler.SetValue(param, 0.0);

        Assert.That(param.Value, Is.EqualTo(0.0));
    }

    [Test]
    public void SqliteDoubleTypeHandler_SetValue_stores_negative_value()
    {
        var handler = new SqliteDoubleTypeHandler();
        var param = new FakeDbParameter();

        handler.SetValue(param, -1.5);

        Assert.That(param.Value, Is.EqualTo(-1.5));
    }

    [Test]
    public void SqliteDoubleTypeHandler_Parse_converts_long_to_double()
    {
        var handler = new SqliteDoubleTypeHandler();

        var result = handler.Parse(42L);

        Assert.That(result, Is.EqualTo(42.0));
    }

    [Test]
    public void SqliteDoubleTypeHandler_Parse_converts_string_to_double()
    {
        var handler = new SqliteDoubleTypeHandler();

        var result = handler.Parse("1.5");

        Assert.That(result, Is.EqualTo(1.5));
    }

    [Test]
    public void SqliteDoubleTypeHandler_Parse_converts_double_to_double()
    {
        var handler = new SqliteDoubleTypeHandler();

        var result = handler.Parse(2.71828);

        Assert.That(result, Is.EqualTo(2.71828).Within(0.00001));
    }

    [Test]
    public void SqliteDoubleTypeHandler_Parse_converts_integer_to_double()
    {
        var handler = new SqliteDoubleTypeHandler();

        var result = handler.Parse(100);

        Assert.That(result, Is.EqualTo(100.0));
    }

    [Test]
    public void SqliteDoubleTypeHandler_Parse_returns_zero_for_dbnull()
    {
        var handler = new SqliteDoubleTypeHandler();

        var result = handler.Parse(DBNull.Value);

        Assert.That(result, Is.EqualTo(0.0));
    }

    [Test]
    public void SqliteDoubleTypeHandler_Parse_returns_zero_for_null()
    {
        var handler = new SqliteDoubleTypeHandler();

        var result = handler.Parse(null);

        Assert.That(result, Is.EqualTo(0.0));
    }

    [Test]
    public void CreatePreMigrationSnapshot_includes_only_committed_rows_while_a_write_transaction_is_open()
    {
        var connectionString = $"Data Source={_tempDbPath}";
        using (var setup = new SqliteConnection(connectionString))
        {
            setup.Open();
            using var cmd = setup.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE WalBusyTest (Id INTEGER PRIMARY KEY, Val TEXT); INSERT INTO WalBusyTest VALUES (1, 'committed');";
            cmd.ExecuteNonQuery();
        }

        using var blockingConn = new SqliteConnection(connectionString);
        blockingConn.Open();
        using var tx = blockingConn.BeginTransaction();
        using (var insert = blockingConn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO WalBusyTest VALUES (2, 'uncommitted');";
            insert.ExecuteNonQuery();
        }

        var factory = new DbFactory();
        var snapshotPath = factory.CreatePreMigrationSnapshot(connectionString);

        Assert.That(snapshotPath, Is.Not.Null, "Online backup must proceed while another connection holds an open write transaction.");
        Assert.That(File.Exists(snapshotPath), Is.True);

        using var backupConn = new SqliteConnection($"Data Source={snapshotPath};Pooling=False");
        backupConn.Open();
        using var read = backupConn.CreateCommand();
        read.CommandText = "SELECT Val FROM WalBusyTest ORDER BY Id;";
        using var reader = read.ExecuteReader();
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("committed"));
        Assert.That(reader.Read(), Is.False, "Uncommitted rows must not be included in the snapshot.");
    }

    [Test]
    public void CreatePreMigrationSnapshot_does_not_drop_locks_held_by_open_connections()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Ignore("Reproduces POSIX fcntl lock loss, which unlinks a live SQLite WAL.");
        }

        var connectionString = $"Data Source={_tempDbPath}";
        using var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE LockProbe (Id INTEGER PRIMARY KEY, Val TEXT); INSERT INTO LockProbe VALUES (1, 'committed');";
            cmd.ExecuteNonQuery();
        }

        var factory = new DbFactory();
        var snapshotPath = factory.CreatePreMigrationSnapshot(connectionString);

        Assert.That(snapshotPath, Is.Not.Null);
        Assert.That(File.Exists(snapshotPath), Is.True);
        Assert.That(File.Exists(snapshotPath + "-wal"), Is.False, "The pre-migration snapshot must be a single self-contained database file.");

        using (var snap = new SqliteConnection($"Data Source={snapshotPath};Pooling=False"))
        {
            snap.Open();
            using var cmd = snap.CreateCommand();
            cmd.CommandText = "SELECT Val FROM LockProbe WHERE Id = 1;";
            Assert.That(cmd.ExecuteScalar()?.ToString(), Is.EqualTo("committed"));
        }

        // A real read (not "SELECT 1") makes the other process open the WAL. If this
        // process has lost its advisory locks, that process unlinks the WAL on close.
        OpenAndCloseSqliteInChildProcess(_tempDbPath);

        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO LockProbe VALUES (2, 'after-snapshot');";
            cmd.ExecuteNonQuery();
        }

        SqliteException ioError = null;
        string observed = null;
        try
        {
            using var fresh = new SqliteConnection($"Data Source={_tempDbPath};Pooling=False");
            fresh.Open();
            using var cmd = fresh.CreateCommand();
            cmd.CommandText = "SELECT Val FROM LockProbe WHERE Id = 2;";
            observed = cmd.ExecuteScalar()?.ToString();
        }
        catch (SqliteException ex)
        {
            ioError = ex;
        }

        Assert.That(
            ioError,
            Is.Null,
            "A new connection must not hit disk I/O error after another process opens and closes the database. " +
            "That SQLITE_IOERR means the snapshot dropped POSIX locks and the other process unlinked the WAL. " +
            (ioError == null ? string.Empty : ioError.Message));
        Assert.That(observed, Is.EqualTo("after-snapshot"));
        Assert.That(File.Exists(_tempDbPath + "-wal"), Is.True, "WAL must stay linked while this process has the database open.");
        Assert.That(File.Exists(_tempDbPath + "-shm"), Is.True, "SHM must stay linked while this process has the database open.");
    }

    [Test]
    public void CreatePreMigrationSnapshot_removes_partial_artifacts_when_backup_fails()
    {
        var connectionString = $"Data Source={_tempDbPath}";
        using (var setup = new SqliteConnection(connectionString))
        {
            setup.Open();
            using var cmd = setup.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE Keep (Id INTEGER PRIMARY KEY); INSERT INTO Keep VALUES (1);";
            cmd.ExecuteNonQuery();
        }

        // The snapshot name is chosen inside the method from the current second.
        // Occupy that name with a directory so opening the destination fails after
        // the path is selected. Retry when the clock ticks between the two reads.
        string snapshotPath = "retry";
        string matchedBlocker = null;
        var blockers = new List<string>();
        try
        {
            for (var attempt = 0; attempt < 5 && snapshotPath != null; attempt++)
            {
                var blocker = $"{_tempDbPath}.pre-migration-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                blockers.Add(blocker);
                if (File.Exists(blocker))
                {
                    File.Delete(blocker);
                }

                if (!Directory.Exists(blocker))
                {
                    Directory.CreateDirectory(blocker);
                }

                File.WriteAllText(blocker + "-wal", "partial-wal");
                File.WriteAllText(blocker + "-shm", "partial-shm");

                snapshotPath = new DbFactory().CreatePreMigrationSnapshot(connectionString);
                if (snapshotPath == null)
                {
                    matchedBlocker = blocker;
                }
                else if (File.Exists(snapshotPath))
                {
                    File.Delete(snapshotPath);
                }
            }

            Assert.That(snapshotPath, Is.Null, "Backup must fail when the snapshot path is a directory, and the failure must be swallowed.");
            Assert.That(matchedBlocker, Is.Not.Null);
            Assert.That(File.Exists(matchedBlocker + "-wal"), Is.False, "A partial WAL sidecar must be deleted after a failed snapshot.");
            Assert.That(File.Exists(matchedBlocker + "-shm"), Is.False, "A partial SHM sidecar must be deleted after a failed snapshot.");
            Assert.That(Directory.Exists(matchedBlocker), Is.True, "File.Delete cannot remove the directory that blocked the snapshot; that failure is logged and ignored.");

            using var stillOpen = new SqliteConnection(connectionString);
            stillOpen.Open();
            using var read = stillOpen.CreateCommand();
            read.CommandText = "SELECT COUNT(*) FROM Keep;";
            Assert.That(Convert.ToInt32(read.ExecuteScalar()), Is.EqualTo(1));
        }
        finally
        {
            foreach (var blocker in blockers)
            {
                if (Directory.Exists(blocker))
                {
                    Directory.Delete(blocker, true);
                }

                foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
                {
                    var path = blocker + suffix;
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // File.Delete of a real sidecar succeeds above. A sidecar in a directory
        // this process cannot write hits the cleanup catch without failing startup.
        var lockedDir = Path.Combine(Path.GetTempPath(), "seedarr_snaplock_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lockedDir);
        var lockedDb = Path.Combine(lockedDir, "locked.db");
        var memoryTarget = new MemoryTarget { Layout = "${message}" };
        var logConfig = new LoggingConfiguration();
        logConfig.AddTarget("memory", memoryTarget);
        logConfig.AddRule(LogLevel.Trace, LogLevel.Fatal, memoryTarget);
        LogManager.Configuration = logConfig;
        try
        {
            using (var setup = new SqliteConnection($"Data Source={lockedDb}"))
            {
                setup.Open();
                using var cmd = setup.CreateCommand();
                cmd.CommandText = "CREATE TABLE Keep (Id INTEGER PRIMARY KEY); INSERT INTO Keep VALUES (1);";
                cmd.ExecuteNonQuery();
            }

            string failedSnapshot = "retry";
            for (var attempt = 0; attempt < 5 && !memoryTarget.Logs.Any(log => log.Contains("Failed to delete partial")); attempt++)
            {
                var partialWal = $"{lockedDb}.pre-migration-{DateTime.UtcNow:yyyyMMddHHmmss}.bak-wal";
                File.WriteAllText(partialWal, "locked");
                File.SetUnixFileMode(
                    lockedDir,
                    UnixFileMode.UserRead | UnixFileMode.UserExecute);
                try
                {
                    failedSnapshot = new DbFactory().CreatePreMigrationSnapshot($"Data Source={lockedDb}");
                }
                finally
                {
                    File.SetUnixFileMode(
                        lockedDir,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }

            Assert.That(failedSnapshot, Is.Null);
            Assert.That(
                memoryTarget.Logs.Any(log => log.Contains("Failed to delete partial pre-migration snapshot file")),
                Is.True,
                "Cleanup must log and continue when a partial snapshot file cannot be deleted.");
        }
        finally
        {
            File.SetUnixFileMode(
                lockedDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (Directory.Exists(lockedDir))
            {
                Directory.Delete(lockedDir, true);
            }
        }
    }

    [Test]
    public void Create_creates_pre_migration_snapshot_when_database_file_exists()
    {
        using (var conn = new SqliteConnection($"Data Source={_tempDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE ExistingTable (Id INTEGER PRIMARY KEY, Val TEXT); INSERT INTO ExistingTable VALUES (1, 'initial');";
            cmd.ExecuteNonQuery();
        }

        var factory = new DbFactory();
        factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        var dir = Path.GetDirectoryName(_tempDbPath);
        var fileName = Path.GetFileName(_tempDbPath);
        var snapshots = Directory.GetFiles(dir, $"{fileName}.pre-migration-*.bak");

        Assert.That(snapshots, Has.Length.EqualTo(1));

        using (var backupConn = new SqliteConnection($"Data Source={snapshots[0]}"))
        {
            backupConn.Open();
            using var cmd = backupConn.CreateCommand();
            cmd.CommandText = "SELECT Val FROM ExistingTable WHERE Id = 1;";
            var val = cmd.ExecuteScalar()?.ToString();
            Assert.That(val, Is.EqualTo("initial"));
        }
    }

    [Test]
    public void PrunePreMigrationSnapshots_retains_at_most_3_pre_migration_bak_snapshots()
    {
        var b1 = $"{_tempDbPath}.pre-migration-20260101000001.bak";
        var b2 = $"{_tempDbPath}.pre-migration-20260101000002.bak";
        var b3 = $"{_tempDbPath}.pre-migration-20260101000003.bak";
        var b4 = $"{_tempDbPath}.pre-migration-20260101000004.bak";
        var b5 = $"{_tempDbPath}.pre-migration-20260101000005.bak";

        File.WriteAllText(b1, "1");
        File.WriteAllText(b2, "2");
        File.WriteAllText(b3, "3");
        File.WriteAllText(b4, "4");
        File.WriteAllText(b5, "5");

        DbFactory.PrunePreMigrationSnapshots(_tempDbPath, 3);

        var dir = Path.GetDirectoryName(_tempDbPath);
        var fileName = Path.GetFileName(_tempDbPath);
        var remaining = Directory.GetFiles(dir, $"{fileName}.pre-migration-*.bak");

        Assert.That(remaining, Has.Length.EqualTo(3));
        Assert.That(File.Exists(b1), Is.False);
        Assert.That(File.Exists(b2), Is.False);
        Assert.That(File.Exists(b3), Is.True);
        Assert.That(File.Exists(b4), Is.True);
        Assert.That(File.Exists(b5), Is.True);
    }

    [Test]
    public void PrunePreMigrationSnapshots_retains_newest_snapshots_by_write_time_not_path_order()
    {
        var ts = "20260101120000";
        var b0 = $"{_tempDbPath}.pre-migration-{ts}.bak";
        var b9 = $"{_tempDbPath}.pre-migration-{ts}_9.bak";
        var b10 = $"{_tempDbPath}.pre-migration-{ts}_10.bak";
        var b11 = $"{_tempDbPath}.pre-migration-{ts}_11.bak";

        File.WriteAllText(b0, "0");
        File.WriteAllText(b9, "9");
        File.WriteAllText(b10, "10");
        File.WriteAllText(b11, "11");

        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(b0, baseTime);
        File.SetLastWriteTimeUtc(b9, baseTime.AddSeconds(1));
        File.SetLastWriteTimeUtc(b10, baseTime.AddSeconds(2));
        File.SetLastWriteTimeUtc(b11, baseTime.AddSeconds(3));

        DbFactory.PrunePreMigrationSnapshots(_tempDbPath, 2);

        Assert.That(File.Exists(b0), Is.False);
        Assert.That(File.Exists(b9), Is.False);
        Assert.That(File.Exists(b10), Is.True);
        Assert.That(File.Exists(b11), Is.True);
    }

    [Test]
    public void Migration_failure_logs_backup_file_path_and_does_not_delete_snapshot()
    {
        using (var conn = new SqliteConnection($"Data Source={_tempDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Config (Id INTEGER PRIMARY KEY, Conflict TEXT);";
            cmd.ExecuteNonQuery();
        }

        var memoryTarget = new MemoryTarget { Layout = "${message} ${exception}" };
        var config = new LoggingConfiguration();
        config.AddTarget("memory", memoryTarget);
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, memoryTarget);
        LogManager.Configuration = config;

        var factory = new DbFactory();

        Assert.Throws<Exception>(() => factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}"));

        var dir = Path.GetDirectoryName(_tempDbPath);
        var fileName = Path.GetFileName(_tempDbPath);
        var snapshots = Directory.GetFiles(dir, $"{fileName}.pre-migration-*.bak");

        Assert.That(snapshots, Has.Length.EqualTo(1));
        Assert.That(File.Exists(snapshots[0]), Is.True, "Pre-migration snapshot must not be deleted on migration failure");

        var logs = memoryTarget.Logs;
        Assert.That(logs.Any(log => log.Contains(snapshots[0])), Is.True, "Log output must reference the pre-migration snapshot path");
    }

    private static void OpenAndCloseSqliteInChildProcess(string dbPath)
    {
        var python = FindExecutable("python3") ?? FindExecutable("python");
        ProcessStartInfo startInfo;
        if (python != null)
        {
            startInfo = new ProcessStartInfo
            {
                FileName = python,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute('select count(*) from sqlite_master'); c.close()");
            startInfo.ArgumentList.Add(dbPath);
        }
        else
        {
            var sqlite = FindExecutable("sqlite3");
            if (sqlite == null)
            {
                Assert.Ignore("python3 or sqlite3 is required to open the database from another process.");
            }

            startInfo = new ProcessStartInfo
            {
                FileName = sqlite,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(dbPath);
            startInfo.ArgumentList.Add("SELECT count(*) FROM sqlite_master;");
        }

        using var process = Process.Start(startInfo);
        Assert.That(process, Is.Not.Null, "Failed to start the external SQLite process.");
        var exited = process.WaitForExit(15000);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // The assertion below reports the timeout.
            }

            Assert.Fail("External SQLite process did not exit.");
        }

        var stderr = process.StandardError.ReadToEnd();
        Assert.That(process.ExitCode, Is.EqualTo(0), "External SQLite process failed: " + stderr);
    }

    private static string FindExecutable(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private class FakeDbParameter : IDbDataParameter
    {
        public DbType DbType { get; set; }
        public ParameterDirection Direction { get; set; }
        public bool IsNullable => false;
        public string ParameterName { get; set; }
        public string SourceColumn { get; set; }
        public DataRowVersion SourceVersion { get; set; }
        public object Value { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public int Size { get; set; }
    }
}
