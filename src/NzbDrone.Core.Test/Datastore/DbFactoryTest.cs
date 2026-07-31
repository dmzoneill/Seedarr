using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
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
