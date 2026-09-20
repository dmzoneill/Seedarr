using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class MainDatabaseVacuumTest
{
    private string _tempDir;
    private IDbFactory _dbFactory;
    private IConnectionStringFactory _connectionStringFactory;
    private IAppFolderInfo _appFolderInfo;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_vac_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _dbFactory = Substitute.For<IDbFactory>();
        _connectionStringFactory = Substitute.For<IConnectionStringFactory>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();

        _connectionStringFactory.DatabaseType.Returns(DatabaseType.SQLite);
        _connectionStringFactory.MainDbConnectionString.Returns("Data Source=test.db");
        _appFolderInfo.AppDataFolder.Returns(_tempDir);
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
    public void GetFreelistCount_should_return_zero_for_non_sqlite()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.DatabaseType.Returns(DatabaseType.PostgreSQL);
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(mockDb);

        var mainDb = new MainDatabase(_dbFactory, _connectionStringFactory, _appFolderInfo);

        Assert.That(mainDb.GetFreelistCount(), Is.EqualTo(0));
    }

    [Test]
    public void GetFreelistCount_should_query_pragma_freelist_count()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.DatabaseType.Returns(DatabaseType.SQLite);
        var mockConn = Substitute.For<IDbConnection>();
        var mockCmd = Substitute.For<IDbCommand>();
        mockCmd.ExecuteScalar().Returns(42L);
        mockConn.CreateCommand().Returns(mockCmd);
        mockDb.OpenConnection().Returns(mockConn);
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(mockDb);

        var mainDb = new MainDatabase(_dbFactory, _connectionStringFactory, _appFolderInfo);

        Assert.That(mainDb.GetFreelistCount(), Is.EqualTo(42L));
        Assert.That(mockCmd.CommandText, Does.Contain("PRAGMA freelist_count;"));
    }

    [Test]
    public void GetPageSize_should_query_pragma_page_size()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.DatabaseType.Returns(DatabaseType.SQLite);
        var mockConn = Substitute.For<IDbConnection>();
        var mockCmd = Substitute.For<IDbCommand>();
        mockCmd.ExecuteScalar().Returns(8192L);
        mockConn.CreateCommand().Returns(mockCmd);
        mockDb.OpenConnection().Returns(mockConn);
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(mockDb);

        var mainDb = new MainDatabase(_dbFactory, _connectionStringFactory, _appFolderInfo);

        Assert.That(mainDb.GetPageSize(), Is.EqualTo(8192L));
        Assert.That(mockCmd.CommandText, Does.Contain("PRAGMA page_size;"));
    }

    [Test]
    public void IncrementalVacuum_should_execute_incremental_vacuum_when_auto_vacuum_is_incremental()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.DatabaseType.Returns(DatabaseType.SQLite);
        var mockConn = Substitute.For<IDbConnection>();
        var mockCmd = Substitute.For<IDbCommand>();
        mockCmd.ExecuteScalar().Returns(2); // auto_vacuum = INCREMENTAL
        mockConn.CreateCommand().Returns(mockCmd);
        mockDb.OpenConnection().Returns(mockConn);
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(mockDb);

        var mainDb = new MainDatabase(_dbFactory, _connectionStringFactory, _appFolderInfo);
        mainDb.IncrementalVacuum(5000);

        Assert.That(mockCmd.CommandText, Does.Contain("PRAGMA incremental_vacuum(5000);"));
        mockCmd.Received().ExecuteNonQuery();
    }

    [Test]
    public void IncrementalVacuum_should_convert_to_incremental_when_auto_vacuum_is_none()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.DatabaseType.Returns(DatabaseType.SQLite);
        var mockConn = Substitute.For<IDbConnection>();
        var mockCmd = Substitute.For<IDbCommand>();
        mockCmd.ExecuteScalar().Returns(0); // auto_vacuum = NONE
        mockConn.CreateCommand().Returns(mockCmd);
        mockDb.OpenConnection().Returns(mockConn);
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(mockDb);

        var mainDb = new MainDatabase(_dbFactory, _connectionStringFactory, _appFolderInfo);
        mainDb.IncrementalVacuum(5000);

        Assert.That(mockCmd.CommandText, Does.Contain("PRAGMA auto_vacuum = INCREMENTAL; VACUUM;"));
        mockCmd.Received().ExecuteNonQuery();
    }

    [Test]
    public void RealSqlite_should_create_database_with_incremental_auto_vacuum_and_reclaim_freelist()
    {
        var dbPath = Path.Combine(_tempDir, "real_vac.db");
        var connStr = $"Data Source={dbPath};";

        var dbFactory = new DbFactory();
        var database = dbFactory.Create(DatabaseType.SQLite, connStr);

        using (var conn = database.OpenConnection())
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA auto_vacuum;";
                var autoVacMode = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.That(autoVacMode, Is.EqualTo(2), "Database should have auto_vacuum = INCREMENTAL (2)");

                cmd.CommandText = "CREATE TABLE TestDeletes (id INTEGER PRIMARY KEY, payload TEXT);";
                cmd.ExecuteNonQuery();

                // Insert rows
                for (var i = 0; i < 200; i++)
                {
                    cmd.CommandText = $"INSERT INTO TestDeletes VALUES ({i}, '{new string('x', 2000)}');";
                    cmd.ExecuteNonQuery();
                }

                // Delete rows to generate freelist pages
                cmd.CommandText = "DELETE FROM TestDeletes WHERE id > 50;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "PRAGMA freelist_count;";
                var freelistBefore = Convert.ToInt64(cmd.ExecuteScalar());
                Assert.That(freelistBefore, Is.GreaterThan(0), "Freelist should have uncompacted pages after mass deletion");

                cmd.CommandText = "PRAGMA incremental_vacuum(5000);";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "PRAGMA freelist_count;";
                var freelistAfter = Convert.ToInt64(cmd.ExecuteScalar());
                Assert.That(freelistAfter, Is.EqualTo(0), "Freelist should be 0 after incremental vacuum");
            }
        }
    }

    [Test]
    public void Checkpoint_should_execute_wal_checkpoint_and_return_metrics()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.DatabaseType.Returns(DatabaseType.SQLite);
        var mockConn = Substitute.For<IDbConnection>();
        var mockCmd = Substitute.For<IDbCommand>();
        var mockReader = Substitute.For<IDataReader>();

        mockReader.Read().Returns(true);
        mockReader.IsDBNull(0).Returns(false);
        mockReader.GetValue(0).Returns(0); // busy = 0
        mockReader.IsDBNull(1).Returns(false);
        mockReader.GetValue(1).Returns(123); // log = 123
        mockReader.IsDBNull(2).Returns(false);
        mockReader.GetValue(2).Returns(123); // checkpointed = 123

        mockCmd.ExecuteReader().Returns(mockReader);
        mockConn.CreateCommand().Returns(mockCmd);
        mockDb.OpenConnection().Returns(mockConn);
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(mockDb);

        var mainDb = new MainDatabase(_dbFactory, _connectionStringFactory, _appFolderInfo);
        var result = mainDb.Checkpoint(WalCheckpointMode.Passive);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Busy, Is.EqualTo(0));
        Assert.That(result.WalLogPages, Is.EqualTo(123));
        Assert.That(result.WalCheckpointedPages, Is.EqualTo(123));
        Assert.That(result.CheckpointMode, Is.EqualTo(WalCheckpointMode.Passive));
        Assert.That(mockCmd.CommandText, Does.Contain("PRAGMA wal_checkpoint(PASSIVE);"));
    }

    [Test]
    public void Checkpoint_should_return_safe_result_for_non_sqlite()
    {
        var mockDb = Substitute.For<IDatabase>();
        mockDb.DatabaseType.Returns(DatabaseType.PostgreSQL);
        _dbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(mockDb);

        var mainDb = new MainDatabase(_dbFactory, _connectionStringFactory, _appFolderInfo);
        var result = mainDb.Checkpoint(WalCheckpointMode.Restart);

        Assert.That(result.Success, Is.True);
        Assert.That(result.DatabaseType, Is.EqualTo("PostgreSQL"));
        Assert.That(result.Message, Does.Contain("does not use WAL mode"));
        mockDb.DidNotReceive().OpenConnection();
    }

    [Test]
    public void RealSqlite_should_create_wal_file_and_truncate_on_checkpoint()
    {
        var dbPath = Path.Combine(_tempDir, "real_wal.db");
        var connStr = $"Data Source={dbPath};";

        var dbFactory = new DbFactory();
        var database = dbFactory.Create(DatabaseType.SQLite, connStr);

        var mockDbFactory = Substitute.For<IDbFactory>();
        mockDbFactory.Create(Arg.Any<DatabaseType>(), Arg.Any<string>()).Returns(database);

        var mainDb = new MainDatabase(mockDbFactory, _connectionStringFactory, _appFolderInfo);

        using (var conn = database.OpenConnection())
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "CREATE TABLE TestWal (id INTEGER PRIMARY KEY, payload TEXT);";
                cmd.ExecuteNonQuery();

                for (var i = 0; i < 50; i++)
                {
                    cmd.CommandText = $"INSERT INTO TestWal VALUES ({i}, '{new string('z', 1000)}');";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        var walPath = $"{dbPath}-wal";
        var result = mainDb.Checkpoint(WalCheckpointMode.Truncate);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Busy, Is.EqualTo(0));
        Assert.That(result.CheckpointMode, Is.EqualTo(WalCheckpointMode.Truncate));

        if (File.Exists(walPath))
        {
            var walFileInfo = new FileInfo(walPath);
            Assert.That(walFileInfo.Length, Is.EqualTo(0), "WAL file should be truncated to 0 bytes after TRUNCATE checkpoint");
        }
    }
}
