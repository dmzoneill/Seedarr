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
public class Migration77Test
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
    public void Migration_77_has_correct_metadata()
    {
        var migrationType = typeof(AddRevokedSessionsExpiresAtIndex);
        Assert.That(typeof(NzbDroneMigrationBase).IsAssignableFrom(migrationType), Is.True);

        var attr = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Version, Is.EqualTo(77));
        Assert.That(NzbDroneMigrationBase.LatestMigration, Is.GreaterThanOrEqualTo(77));
    }

    [Test]
    public void DbFactory_Create_runs_migration_and_adds_revoked_sessions_expires_at_index()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var indexes = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='index'").ToList();

        Assert.That(indexes, Does.Contain("IX_RevokedSessions_ExpiresAtUtc"));
    }

    [Test]
    public void DbFactory_Create_is_idempotent_for_revoked_sessions_expires_at_index()
    {
        var factory = new DbFactory();
        factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");
        factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = new DbFactory().Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}").OpenConnection();
        var indexes = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='index'").ToList();

        Assert.That(indexes, Does.Contain("IX_RevokedSessions_ExpiresAtUtc"));
    }
}
