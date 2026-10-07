using System.IO;
using Npgsql;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class ConnectionStringFactoryTest
{
    private IAppFolderInfo _appFolderInfo;
    private IConfigFileProvider _configFileProvider;

    [SetUp]
    public void Setup()
    {
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();

        _appFolderInfo.AppDataFolder.Returns("/tmp/seedarr");
        _configFileProvider.PostgresHost.Returns(string.Empty);
    }

    private ConnectionStringFactory BuildSubject()
    {
        return new ConnectionStringFactory(_appFolderInfo, _configFileProvider);
    }

    [Test]
    public void DatabaseType_should_be_sqlite_when_postgres_host_is_empty()
    {
        _configFileProvider.PostgresHost.Returns(string.Empty);

        var subject = BuildSubject();

        Assert.That(subject.DatabaseType, Is.EqualTo(DatabaseType.SQLite));
    }

    [Test]
    public void DatabaseType_should_be_sqlite_when_postgres_host_is_null()
    {
        _configFileProvider.PostgresHost.Returns((string)null);

        var subject = BuildSubject();

        Assert.That(subject.DatabaseType, Is.EqualTo(DatabaseType.SQLite));
    }

    [Test]
    public void DatabaseType_should_be_sqlite_when_postgres_host_is_whitespace_only()
    {
        _configFileProvider.PostgresHost.Returns("   ");

        var subject = BuildSubject();

        Assert.That(subject.DatabaseType, Is.EqualTo(DatabaseType.SQLite));
    }

    [Test]
    public void MainDbConnectionString_should_use_sqlite_when_postgres_host_is_whitespace_only()
    {
        _configFileProvider.PostgresHost.Returns("\t");
        _appFolderInfo.AppDataFolder.Returns("/data/seedarr");

        var subject = BuildSubject();

        var expectedPath = Path.Combine("/data/seedarr", "seedarr.db");
        Assert.That(subject.MainDbConnectionString, Does.Contain(expectedPath));
    }

    [Test]
    public void DatabaseType_should_be_postgresql_when_postgres_host_is_set()
    {
        _configFileProvider.PostgresHost.Returns("db.example.com");

        var subject = BuildSubject();

        Assert.That(subject.DatabaseType, Is.EqualTo(DatabaseType.PostgreSQL));
    }

    [Test]
    public void MainDbConnectionString_should_contain_db_path_when_sqlite()
    {
        _configFileProvider.PostgresHost.Returns(string.Empty);
        _appFolderInfo.AppDataFolder.Returns("/data/seedarr");

        var subject = BuildSubject();

        var expectedPath = Path.Combine("/data/seedarr", "seedarr.db");
        Assert.That(subject.MainDbConnectionString, Does.Contain(expectedPath));
    }

    [Test]
    public void MainDbConnectionString_should_not_include_cache_shared_for_sqlite()
    {
        _configFileProvider.PostgresHost.Returns(string.Empty);

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.Not.Contain("Cache=Shared"));
    }

    [Test]
    public void MainDbConnectionString_should_start_with_data_source_for_sqlite()
    {
        _configFileProvider.PostgresHost.Returns(string.Empty);

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.StartWith("Data Source="));
    }

    [Test]
    public void MainDbConnectionString_should_include_busy_timeout_for_sqlite()
    {
        _configFileProvider.PostgresHost.Returns(string.Empty);

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.Contain("Busy Timeout=30"));
    }

    [Test]
    public void MainDbConnectionString_should_include_default_timeout_for_sqlite()
    {
        _configFileProvider.PostgresHost.Returns(string.Empty);

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.Contain("Default Timeout=30"));
    }

    [Test]
    public void MainDbConnectionString_should_include_foreign_keys_for_sqlite()
    {
        _configFileProvider.PostgresHost.Returns(string.Empty);

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.Contain("Foreign Keys=True"));
    }

    [Test]
    public void MainDbConnectionString_cleaned_for_migrations_should_include_30_second_timeout_and_foreign_keys()
    {
        _configFileProvider.PostgresHost.Returns(string.Empty);

        var subject = BuildSubject();
        var migrationConnStr = DbFactory.CleanSqliteConnectionString(subject.MainDbConnectionString);

        Assert.That(migrationConnStr, Does.Contain("Default Timeout=30"));
        Assert.That(migrationConnStr, Does.Contain("Foreign Keys=True"));
        Assert.That(migrationConnStr, Does.Not.Contain("Cache=Shared"));
    }

    [Test]
    public void MainDbConnectionString_should_trim_postgres_host()
    {
        _configFileProvider.PostgresHost.Returns("  pg.example.com  ");
        _configFileProvider.PostgresPort.Returns(5432);
        _configFileProvider.PostgresMainDb.Returns("seedarr");
        _configFileProvider.PostgresUser.Returns("user");
        _configFileProvider.PostgresPassword.Returns("secret");

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.Contain("Host=pg.example.com"));
    }

    [Test]
    public void MainDbConnectionString_should_contain_host_when_postgres()
    {
        _configFileProvider.PostgresHost.Returns("pg.example.com");
        _configFileProvider.PostgresPort.Returns(5432);
        _configFileProvider.PostgresMainDb.Returns("seedarr");
        _configFileProvider.PostgresUser.Returns("user");
        _configFileProvider.PostgresPassword.Returns("secret");

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.Contain("Host=pg.example.com"));
    }

    [Test]
    public void MainDbConnectionString_should_contain_port_when_postgres()
    {
        _configFileProvider.PostgresHost.Returns("pg.example.com");
        _configFileProvider.PostgresPort.Returns(5433);
        _configFileProvider.PostgresMainDb.Returns("seedarr");
        _configFileProvider.PostgresUser.Returns("user");
        _configFileProvider.PostgresPassword.Returns("secret");

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.Contain("Port=5433"));
    }

    [Test]
    public void MainDbConnectionString_should_contain_database_when_postgres()
    {
        _configFileProvider.PostgresHost.Returns("pg.example.com");
        _configFileProvider.PostgresPort.Returns(5432);
        _configFileProvider.PostgresMainDb.Returns("mydb");
        _configFileProvider.PostgresUser.Returns("user");
        _configFileProvider.PostgresPassword.Returns("secret");

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.Contain("Database=mydb"));
    }

    [Test]
    public void MainDbConnectionString_should_contain_username_when_postgres()
    {
        _configFileProvider.PostgresHost.Returns("pg.example.com");
        _configFileProvider.PostgresPort.Returns(5432);
        _configFileProvider.PostgresMainDb.Returns("seedarr");
        _configFileProvider.PostgresUser.Returns("admin");
        _configFileProvider.PostgresPassword.Returns("secret");

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.Contain("Username=admin"));
    }

    [Test]
    public void MainDbConnectionString_should_contain_password_when_postgres()
    {
        _configFileProvider.PostgresHost.Returns("pg.example.com");
        _configFileProvider.PostgresPort.Returns(5432);
        _configFileProvider.PostgresMainDb.Returns("seedarr");
        _configFileProvider.PostgresUser.Returns("user");
        _configFileProvider.PostgresPassword.Returns("p@ssw0rd");

        var subject = BuildSubject();

        Assert.That(subject.MainDbConnectionString, Does.Contain("Password=p@ssw0rd"));
    }

    [Test]
    public void MainDbConnectionString_should_round_trip_postgres_credentials_with_delimiter_chars()
    {
        _configFileProvider.PostgresHost.Returns("pg.example.com");
        _configFileProvider.PostgresPort.Returns(5432);
        _configFileProvider.PostgresMainDb.Returns("seedarr");
        _configFileProvider.PostgresUser.Returns("user=name");
        _configFileProvider.PostgresPassword.Returns("pass;word\\");

        var subject = BuildSubject();

        var parsed = new NpgsqlConnectionStringBuilder(subject.MainDbConnectionString);
        Assert.That(parsed.Host, Is.EqualTo("pg.example.com"));
        Assert.That(parsed.Port, Is.EqualTo(5432));
        Assert.That(parsed.Database, Is.EqualTo("seedarr"));
        Assert.That(parsed.Username, Is.EqualTo("user=name"));
        Assert.That(parsed.Password, Is.EqualTo("pass;word\\"));
    }

    [Test]
    public void MainDbConnectionString_is_stable_across_multiple_reads()
    {
        _configFileProvider.PostgresHost.Returns(string.Empty);

        var subject = BuildSubject();

        var first = subject.MainDbConnectionString;
        var second = subject.MainDbConnectionString;

        Assert.That(second, Is.EqualTo(first));
    }
}
