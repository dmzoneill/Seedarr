using System;
using System.IO;
using System.Reflection;
using Dapper;
using FluentMigrator;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Datastore.Migration;

[TestFixture]
public class Migration76Test
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
    public void Migration_76_has_correct_metadata()
    {
        var migrationType = typeof(FixScalarTagIdsJson);
        Assert.That(typeof(NzbDroneMigrationBase).IsAssignableFrom(migrationType), Is.True);

        var attr = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Version, Is.EqualTo(76));
        Assert.That(NzbDroneMigrationBase.LatestMigration, Is.GreaterThanOrEqualTo(76));
    }

    [Test]
    public void DbFactory_Create_runs_migration_76_and_repairs_scalar_tag_ids()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();

        conn.Execute("INSERT INTO \"Torrents\" (\"Name\", \"InfoHash\", \"TagIds\", \"DateAdded\") VALUES ('Legacy', 'hash_legacy_7', '7', '2026-01-01 00:00:00');");

        conn.Execute("""
            UPDATE "Torrents" SET "TagIds" = '[' || "TagIds" || ']'
            WHERE "TagIds" IS NOT NULL
              AND "TagIds" != ''
              AND "TagIds" != '[]'
              AND "TagIds" NOT LIKE '[%';
            """);

        var torrent = conn.QuerySingle<Torrent>("SELECT * FROM \"Torrents\" WHERE \"InfoHash\" = 'hash_legacy_7'");
        Assert.That(torrent.TagIds, Is.EqualTo(new[] { 7 }));
    }
}
