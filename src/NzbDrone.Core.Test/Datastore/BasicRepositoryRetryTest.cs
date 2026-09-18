using System;
using System.Data;
using Microsoft.Data.Sqlite;
using Npgsql;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class BasicRepositoryRetryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;

    private class TestableRepository : BasicRepository<Tag>
    {
        public TestableRepository(IDatabase database)
            : base(database)
        {
        }

        public TResult ExecuteQuery<TResult>(Func<IDbConnection, TResult> query) => QueryWithRetry(query);

        public void ExecuteAction(Action<IDbConnection> action) => ExecuteWithRetry(action);
    }

    private class TransientNpgsqlException : NpgsqlException
    {
        public TransientNpgsqlException(string message)
            : base(message)
        {
        }

        public override bool IsTransient => true;
    }

    [SetUp]
    public void SetUp()
    {
        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""Tags"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""Label"" TEXT NOT NULL
            );
            CREATE TABLE ""Torrents"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""InfoHash"" TEXT NOT NULL
            );
            CREATE TABLE ""DownloadHistory"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""InfoHash"" TEXT NOT NULL,
                ""TorrentId"" INTEGER,
                ""DateAdded"" TEXT
            );
            CREATE TABLE ""TorrentFiles"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TorrentId"" INTEGER NOT NULL,
                ""Path"" TEXT NOT NULL
            );";
        cmd.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection.Close();
        _keepAliveConnection.Dispose();
    }

    [Test]
    public void QueryWithRetry_retries_and_succeeds_on_sqlite_busy_code_5()
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        database.OpenConnection().Returns(connection);

        var subject = new TestableRepository(database);
        var attempts = 0;

        var result = subject.ExecuteQuery(conn =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new SqliteException("database is busy", 5);
            }

            return "success";
        });

        Assert.That(result, Is.EqualTo("success"));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void QueryWithRetry_retries_and_succeeds_on_sqlite_locked_code_6()
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        database.OpenConnection().Returns(connection);

        var subject = new TestableRepository(database);
        var attempts = 0;

        var result = subject.ExecuteQuery(conn =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new SqliteException("database table is locked", 6);
            }

            return 42;
        });

        Assert.That(result, Is.EqualTo(42));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void QueryWithRetry_retries_and_succeeds_on_postgres_serialization_failure_40001()
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        database.OpenConnection().Returns(connection);

        var subject = new TestableRepository(database);
        var attempts = 0;

        var result = subject.ExecuteQuery(conn =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new PostgresException("could not serialize access due to concurrent update", "ERROR", "ERROR", "40001");
            }

            return "recovered";
        });

        Assert.That(result, Is.EqualTo("recovered"));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void QueryWithRetry_retries_and_succeeds_on_postgres_deadlock_detected_40P01()
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        database.OpenConnection().Returns(connection);

        var subject = new TestableRepository(database);
        var attempts = 0;

        var result = subject.ExecuteQuery(conn =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01");
            }

            return "resolved";
        });

        Assert.That(result, Is.EqualTo("resolved"));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    [TestCase("08000")]
    [TestCase("08006")]
    public void QueryWithRetry_retries_and_succeeds_on_postgres_connection_errors(string sqlState)
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        database.OpenConnection().Returns(connection);

        var subject = new TestableRepository(database);
        var attempts = 0;

        var result = subject.ExecuteQuery(conn =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new PostgresException("connection dropped", "ERROR", "ERROR", sqlState);
            }

            return "connected";
        });

        Assert.That(result, Is.EqualTo("connected"));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void QueryWithRetry_retries_and_succeeds_on_transient_npgsql_exception()
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        database.OpenConnection().Returns(connection);

        var subject = new TestableRepository(database);
        var attempts = 0;

        var result = subject.ExecuteQuery(conn =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new TransientNpgsqlException("transient socket timeout");
            }

            return "recovered";
        });

        Assert.That(result, Is.EqualTo("recovered"));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void QueryWithRetry_retries_when_OpenConnection_throws_transient_error()
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        var openAttempts = 0;

        database.OpenConnection().Returns(_ =>
        {
            openAttempts++;
            if (openAttempts < 2)
            {
                throw new PostgresException("connection failure", "ERROR", "ERROR", "08006");
            }

            return connection;
        });

        var subject = new TestableRepository(database);

        var result = subject.ExecuteQuery(conn => "reconnected");

        Assert.That(result, Is.EqualTo("reconnected"));
        Assert.That(openAttempts, Is.EqualTo(2));
    }

    [Test]
    public void ExecuteWithRetry_retries_and_succeeds_after_transient_error()
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        database.OpenConnection().Returns(connection);

        var subject = new TestableRepository(database);
        var attempts = 0;

        subject.ExecuteAction(conn =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01");
            }
        });

        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void QueryWithRetry_does_not_retry_non_transient_sqlite_error()
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        database.OpenConnection().Returns(connection);

        var subject = new TestableRepository(database);
        var attempts = 0;

        var ex = Assert.Throws<SqliteException>(() => subject.ExecuteQuery<string>(conn =>
        {
            attempts++;
            throw new SqliteException("syntax error", 1);
        }));

        Assert.That(ex.SqliteErrorCode, Is.EqualTo(1));
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public void QueryWithRetry_does_not_retry_non_transient_postgres_error()
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        database.OpenConnection().Returns(connection);

        var subject = new TestableRepository(database);
        var attempts = 0;

        var ex = Assert.Throws<PostgresException>(() => subject.ExecuteQuery<string>(conn =>
        {
            attempts++;
            throw new PostgresException("unique violation", "ERROR", "ERROR", "23505");
        }));

        Assert.That(ex.SqlState, Is.EqualTo("23505"));
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public void QueryWithRetry_does_not_retry_non_transient_npgsql_exception()
    {
        var database = Substitute.For<IDatabase>();
        var connection = Substitute.For<IDbConnection>();
        database.OpenConnection().Returns(connection);

        var subject = new TestableRepository(database);
        var attempts = 0;

        Assert.Throws<NpgsqlException>(() => subject.ExecuteQuery<string>(conn =>
        {
            attempts++;
            throw new NpgsqlException("regular non-transient npgsql error");
        }));

        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public void TorrentRepository_GetByInfoHash_retries_and_succeeds_after_transient_failure()
    {
        var realDatabase = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        var attempts = 0;
        var database = Substitute.For<IDatabase>();
        database.OpenConnection().Returns(_ =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new SqliteException("database is locked", 5);
            }

            return realDatabase.OpenConnection();
        });

        var repo = new TorrentRepository(database);
        var result = repo.GetByInfoHash("abcdef1234567890abcdef1234567890abcdef12");

        Assert.That(result, Is.Null);
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void DownloadHistoryRepository_FindByInfoHash_retries_and_succeeds_after_postgres_deadlock()
    {
        var realDatabase = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        var attempts = 0;
        var database = Substitute.For<IDatabase>();
        database.OpenConnection().Returns(_ =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01");
            }

            return realDatabase.OpenConnection();
        });

        var repo = new DownloadHistoryRepository(database);
        var result = repo.FindByInfoHash("abcdef1234567890abcdef1234567890abcdef12");

        Assert.That(result, Is.Null);
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void TorrentFileRepository_DeleteByTorrentId_retries_and_succeeds_after_transient_error()
    {
        var realDatabase = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        var attempts = 0;
        var database = Substitute.For<IDatabase>();
        database.OpenConnection().Returns(_ =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new SqliteException("database is busy", 5);
            }

            return realDatabase.OpenConnection();
        });

        var repo = new TorrentFileRepository(database);
        Assert.DoesNotThrow(() => repo.DeleteByTorrentId(123));
        Assert.That(attempts, Is.EqualTo(2));
    }
}
