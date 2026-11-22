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
public class Migration79Test
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
    public void Migration_79_has_correct_metadata()
    {
        var migrationType = typeof(SeedClientProfileDefinitions);
        Assert.That(typeof(NzbDroneMigrationBase).IsAssignableFrom(migrationType), Is.True);

        var attr = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Version, Is.EqualTo(79));
        Assert.That(NzbDroneMigrationBase.LatestMigration, Is.GreaterThanOrEqualTo(79));
        Assert.DoesNotThrow(() => new SeedClientProfileDefinitions().Down());
    }

    [Test]
    public void DbFactory_Create_seeds_enabled_built_in_client_profiles()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var rows = conn.Query<(string Implementation, string Name, long Enable)>(
            "SELECT Implementation, Name, Enable FROM ClientProfileDefinitions").ToList();

        Assert.That(rows.Select(r => r.Implementation), Is.EquivalentTo(new[]
        {
            "QBittorrentProfile",
            "DelugeProfile",
            "TransmissionProfile",
            "UTorrentProfile",
            "BiglyBTProfile"
        }));
        Assert.That(rows.All(r => r.Enable == 1), Is.True);
        Assert.That(rows.Select(r => r.Name), Does.Contain("Transmission 3.00"));
    }

    [Test]
    public void DbFactory_Create_does_not_duplicate_client_profiles()
    {
        var factory = new DbFactory();
        factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");
        factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = new DbFactory().Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}").OpenConnection();
        var count = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM ClientProfileDefinitions");

        Assert.That(count, Is.EqualTo(5));
    }
}
