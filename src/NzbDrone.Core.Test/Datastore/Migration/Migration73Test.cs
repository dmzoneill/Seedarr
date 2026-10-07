using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using FluentMigrator;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;

namespace NzbDrone.Core.Test.Datastore.Migration;

[TestFixture]
public class Migration73Test
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
    public void Migration_73_has_correct_metadata()
    {
        var migrationType = typeof(CaseInsensitiveIdentityProviderId);
        Assert.That(typeof(NzbDroneMigrationBase).IsAssignableFrom(migrationType), Is.True);

        var attr = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Version, Is.EqualTo(73));
        Assert.That(NzbDroneMigrationBase.LatestMigration, Is.GreaterThanOrEqualTo(73));
    }

    [Test]
    public void DbFactory_Create_adds_case_insensitive_unique_index_on_provider_id()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var indexes = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='index'").ToList();

        Assert.That(indexes, Does.Contain("IX_IdentityProviders_ProviderId"));
    }

    [Test]
    public void IdentityProviderRepository_rejects_duplicate_provider_ids_differing_only_by_case()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");
        var repository = new IdentityProviderRepository(db);
        var now = DateTime.UtcNow;

        repository.Insert(new IdentityProviderDefinition
        {
            ProviderId = "oidc",
            Name = "OIDC",
            CreatedAt = now,
            UpdatedAt = now,
        });

        Assert.Throws<SqliteException>(() => repository.Insert(new IdentityProviderDefinition
        {
            ProviderId = "OIDC",
            Name = "OIDC Upper",
            CreatedAt = now,
            UpdatedAt = now,
        }));
    }

    [Test]
    public void IdentityProviderRepository_FindByProviderId_is_case_insensitive()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");
        var repository = new IdentityProviderRepository(db);
        var now = DateTime.UtcNow;

        repository.Insert(new IdentityProviderDefinition
        {
            ProviderId = "google",
            Name = "Google",
            CreatedAt = now,
            UpdatedAt = now,
        });

        var found = repository.FindByProviderId("Google");

        Assert.That(found, Is.Not.Null);
        Assert.That(found.ProviderId, Is.EqualTo("google"));
    }
}
