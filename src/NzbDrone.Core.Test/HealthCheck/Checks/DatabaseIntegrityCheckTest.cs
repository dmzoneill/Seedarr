using System;
using System.Data;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class DatabaseIntegrityCheckTest
{
    private IDatabase _database;
    private IDbConnection _connection;
    private IDbCommand _command;
    private DatabaseIntegrityCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _database = Substitute.For<IDatabase>();
        _connection = Substitute.For<IDbConnection>();
        _command = Substitute.For<IDbCommand>();

        _database.OpenConnection().Returns(_connection);
        _connection.CreateCommand().Returns(_command);

        _subject = new DatabaseIntegrityCheck(_database);
    }

    [Test]
    public void Check_should_return_error_when_database_is_null()
    {
        var subject = new DatabaseIntegrityCheck(null);
        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Source, Is.EqualTo(nameof(DatabaseIntegrityCheck)));
        Assert.That(result.Message, Does.Contain("Database is unavailable"));
    }

    [Test]
    public void Check_should_return_error_when_connection_is_null()
    {
        _database.OpenConnection().Returns((IDbConnection)null);
        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("Database connection could not be opened"));
    }

    [Test]
    public void Check_should_return_ok_when_sqlite_quick_check_returns_ok()
    {
        _database.DatabaseType.Returns(DatabaseType.SQLite);
        _command.ExecuteScalar().Returns("ok");

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(_command.CommandText, Is.EqualTo("PRAGMA quick_check;"));
    }

    [Test]
    public void Check_should_return_error_when_sqlite_quick_check_reports_corruption()
    {
        _database.DatabaseType.Returns(DatabaseType.SQLite);
        _command.ExecuteScalar().Returns("*** in database main *** Page 4: b-tree init page failed");

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("b-tree init page failed"));
    }

    [Test]
    public void Check_should_return_error_when_sqlite_quick_check_returns_null_or_empty()
    {
        _database.DatabaseType.Returns(DatabaseType.SQLite);
        _command.ExecuteScalar().Returns(null);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("returned no results"));
    }

    [Test]
    public void Check_should_return_error_when_sqlite_query_throws_exception()
    {
        _database.DatabaseType.Returns(DatabaseType.SQLite);
        _command.ExecuteScalar().Throws(new InvalidOperationException("database disk image is malformed"));

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("database disk image is malformed"));
    }

    [Test]
    public void Check_should_return_ok_when_postgresql_connection_and_query_succeed()
    {
        _database.DatabaseType.Returns(DatabaseType.PostgreSQL);
        _command.ExecuteScalar().Returns(1);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(_command.CommandText, Is.EqualTo("SELECT 1;"));
    }

    [Test]
    public void Check_should_return_error_when_postgresql_query_throws_exception()
    {
        _database.DatabaseType.Returns(DatabaseType.PostgreSQL);
        _command.ExecuteScalar().Throws(new InvalidOperationException("Connection refused"));

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("Connection refused"));
    }

    [Test]
    public void Check_should_accept_IMainDatabase_instance()
    {
        var mainDb = Substitute.For<IMainDatabase>();
        var conn = Substitute.For<IDbConnection>();
        var cmd = Substitute.For<IDbCommand>();

        mainDb.OpenConnection().Returns(conn);
        conn.CreateCommand().Returns(cmd);
        mainDb.DatabaseType.Returns(DatabaseType.SQLite);
        cmd.ExecuteScalar().Returns("ok");

        var subject = new DatabaseIntegrityCheck(mainDb);
        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }
}
