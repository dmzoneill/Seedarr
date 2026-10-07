using System;
using System.IO;
using System.Reflection;
using FluentMigrator;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;

namespace NzbDrone.Core.Test.Datastore.Migration;

[TestFixture]
public class Migration75Test
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
    public void Migration_75_has_correct_metadata()
    {
        var migrationType = typeof(AddIdentityProviderTrustedProxies);
        Assert.That(typeof(NzbDroneMigrationBase).IsAssignableFrom(migrationType), Is.True);

        var attr = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Version, Is.EqualTo(75));
        Assert.That(NzbDroneMigrationBase.LatestMigration, Is.GreaterThanOrEqualTo(75));
    }

    [Test]
    public void IdentityProviderRepository_persists_trusted_proxies()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");
        var repository = new IdentityProviderRepository(db);
        var now = DateTime.UtcNow;

        var inserted = repository.Insert(new IdentityProviderDefinition
        {
            ProviderId = "forward-auth",
            Name = "Forward Auth",
            ProviderType = IdentityProviderType.ForwardAuth,
            TrustedProxies = "10.0.0.0/8,172.16.0.0/12",
            CreatedAt = now,
            UpdatedAt = now,
        });

        var loaded = repository.Get(inserted.Id);
        Assert.That(loaded.TrustedProxies, Is.EqualTo("10.0.0.0/8,172.16.0.0/12"));

        loaded.TrustedProxies = "192.168.1.1";
        loaded.UpdatedAt = now;
        repository.Update(loaded);

        var updated = repository.Get(inserted.Id);
        Assert.That(updated.TrustedProxies, Is.EqualTo("192.168.1.1"));
    }
}
