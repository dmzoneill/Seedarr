using System;
using System.Data;
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
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            foreach (var bak in Directory.GetFiles(dir, $"{fileName}.pre-migration-*.bak"))
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
