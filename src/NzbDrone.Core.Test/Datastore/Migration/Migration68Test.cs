using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using FluentMigrator;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;

namespace NzbDrone.Core.Test.Datastore.Migration;

[TestFixture]
public class Migration68Test
{
    private string _tempDbPath;

    [SetUp]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"seedarr_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_68_has_correct_metadata()
    {
        var migrationType = typeof(FixVarcharTruncationAndCascadeDeletes);
        Assert.That(typeof(NzbDroneMigrationBase).IsAssignableFrom(migrationType), Is.True);

        var attr = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Version, Is.EqualTo(68));
        Assert.That(NzbDroneMigrationBase.LatestMigration, Is.GreaterThanOrEqualTo(68));
    }

    [Test]
    public void DbFactory_Create_runs_migration_68_cleanly()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var tables = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='table'").ToList();

        Assert.That(tables, Does.Contain("Torrents"));
        Assert.That(tables, Does.Contain("TorrentFiles"));
        Assert.That(tables, Does.Contain("TrackerEntries"));
        Assert.That(tables, Does.Contain("TorrentEventLogs"));
        Assert.That(tables, Does.Contain("TorrentMediaMetadata"));
        Assert.That(tables, Does.Contain("DownloadHistory"));
        Assert.That(tables, Does.Contain("NotificationDefinitions"));
    }

    [Test]
    public void DbFactory_Create_is_idempotent_and_does_not_fail_on_subsequent_runs()
    {
        var factory = new DbFactory();
        var db1 = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");
        var db2 = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db2.OpenConnection();
        var tables = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='table'").ToList();

        Assert.That(tables, Does.Contain("Torrents"));
        Assert.That(tables, Does.Contain("TorrentFiles"));
        Assert.That(tables, Does.Contain("TrackerEntries"));
        Assert.That(tables, Does.Contain("TorrentEventLogs"));
        Assert.That(tables, Does.Contain("TorrentMediaMetadata"));
    }
}
