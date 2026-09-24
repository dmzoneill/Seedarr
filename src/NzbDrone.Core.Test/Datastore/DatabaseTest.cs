using System;
using System.Data;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class DatabaseTest
{
    private string _tempDbPath;

    [SetUp]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"seedarr_db_pragma_test_{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempDbPath))
        {
            try
            {
                File.Delete(_tempDbPath);
            }
            catch
            {
                // Best-effort test cleanup
            }
        }
    }

    [Test]
    public void OpenConnection_should_apply_sqlite_performance_pragmas()
    {
        var database = new Database(() => new SqliteConnection($"Data Source={_tempDbPath}"), DatabaseType.SQLite);

        using var conn = database.OpenConnection();

        var tempStore = conn.ExecuteScalar<long>("PRAGMA temp_store;");
        Assert.That(tempStore, Is.EqualTo(2), "PRAGMA temp_store should be MEMORY (2)");

        var mmapSize = conn.ExecuteScalar<long>("PRAGMA mmap_size;");
        Assert.That(mmapSize, Is.EqualTo(268435456), "PRAGMA mmap_size should be 268435456 (256 MB)");

        var cacheSize = conn.ExecuteScalar<long>("PRAGMA cache_size;");
        Assert.That(cacheSize, Is.EqualTo(-64000), "PRAGMA cache_size should be -64000");

        var synchronous = conn.ExecuteScalar<long>("PRAGMA synchronous;");
        Assert.That(synchronous, Is.EqualTo(1), "PRAGMA synchronous should be NORMAL (1)");

        var foreignKeys = conn.ExecuteScalar<long>("PRAGMA foreign_keys;");
        Assert.That(foreignKeys, Is.EqualTo(1), "PRAGMA foreign_keys should be ON (1)");

        var busyTimeout = conn.ExecuteScalar<long>("PRAGMA busy_timeout;");
        Assert.That(busyTimeout, Is.GreaterThanOrEqualTo(30000), "PRAGMA busy_timeout should be at least 30000");
    }

    [Test]
    public void Optimize_should_execute_pragma_optimize_successfully_on_sqlite()
    {
        var database = new Database(() => new SqliteConnection($"Data Source={_tempDbPath}"), DatabaseType.SQLite);

        Assert.DoesNotThrow(() => database.Optimize());
    }

    [Test]
    public void Optimize_should_be_noop_for_non_sqlite()
    {
        var factoryCalled = false;
        Func<IDbConnection> factory = () =>
        {
            factoryCalled = true;
            return new SqliteConnection($"Data Source={_tempDbPath}");
        };
        var database = new Database(factory, DatabaseType.PostgreSQL);

        database.Optimize();

        Assert.That(factoryCalled, Is.False, "Optimize should not open connection for non-SQLite database");
    }
}
