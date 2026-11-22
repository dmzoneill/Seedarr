using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using FluentMigrator;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Notifications;

namespace NzbDrone.Core.Test.Datastore.Migration;

[TestFixture]
public class Migration78Test
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
    public void Migration_78_has_correct_metadata()
    {
        var migrationType = typeof(AddFallbackNotificationId);
        Assert.That(typeof(NzbDroneMigrationBase).IsAssignableFrom(migrationType), Is.True);

        var attr = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Version, Is.EqualTo(78));
        Assert.That(NzbDroneMigrationBase.LatestMigration, Is.GreaterThanOrEqualTo(78));
        Assert.DoesNotThrow(() => new SeedClientProfileDefinitions().Down());
    }

    [Test]
    public void DbFactory_Create_adds_nullable_fallback_notification_id_column()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var column = conn.QueryFirstOrDefault<ColumnInfo>(
            "SELECT name AS Name, type AS Type, \"notnull\" AS NotNullFlag FROM pragma_table_info('NotificationDefinitions') WHERE name = 'FallbackNotificationId'");

        Assert.That(column, Is.Not.Null);
        Assert.That(column.Name, Is.EqualTo("FallbackNotificationId"));
        Assert.That(column.Type, Is.EqualTo("INTEGER").IgnoreCase);
        Assert.That(column.NotNullFlag, Is.EqualTo(0));
    }

    [Test]
    public void NotificationRepository_insert_and_update_persist_fallback_notification_id()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");
        var repository = new NotificationRepository(db);

        var backup = repository.Insert(new NotificationDefinition
        {
            Name = "Backup Webhook",
            Implementation = "Webhook",
            Settings = "{\"url\":\"https://example.com/backup\"}",
            Enable = true,
            OnGrab = true,
        });

        var primary = repository.Insert(new NotificationDefinition
        {
            Name = "Primary Discord",
            Implementation = "Discord",
            Settings = "{\"webhookUrl\":\"https://example.com/discord\"}",
            Enable = true,
            OnGrab = true,
            FallbackNotificationId = backup.Id,
        });

        var created = repository.Get(primary.Id);
        Assert.That(created, Is.Not.Null);
        Assert.That(created.FallbackNotificationId, Is.EqualTo(backup.Id));

        created.Name = "Primary Discord Updated";
        created.FallbackNotificationId = null;
        repository.Update(created);

        var cleared = repository.Get(primary.Id);
        Assert.That(cleared.Name, Is.EqualTo("Primary Discord Updated"));
        Assert.That(cleared.FallbackNotificationId, Is.Null);

        cleared.FallbackNotificationId = backup.Id;
        repository.Update(cleared);

        var restored = repository.Get(primary.Id);
        Assert.That(restored.FallbackNotificationId, Is.EqualTo(backup.Id));
        Assert.That(repository.All().Single(n => n.Id == backup.Id).FallbackNotificationId, Is.Null);
    }

    [Test]
    public void DbFactory_Create_is_idempotent_for_fallback_notification_id()
    {
        var factory = new DbFactory();
        factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");
        factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = new DbFactory().Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}").OpenConnection();
        var columns = conn.Query<string>("SELECT name FROM pragma_table_info('NotificationDefinitions')").ToList();

        Assert.That(columns.Count(name => name == "FallbackNotificationId"), Is.EqualTo(1));
    }

    [Test]
    public void DbFactory_Create_adds_nullable_fallback_notification_id_on_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("SEEDARR_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("Set SEEDARR_TEST_POSTGRES to verify the PostgreSQL migration.");
        }

        var databaseName = "seedarr_m78_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            admin.Open();
            using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            create.ExecuteNonQuery();
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName
        };

        try
        {
            // FluentMigrator records every applied version. Mark 1-77 applied and create the
            // notification table without FallbackNotificationId so only migration 078 runs.
            using (var seed = new NpgsqlConnection(builder.ConnectionString))
            {
                seed.Open();
                using var cmd = seed.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE "VersionInfo" (
                        "Version" bigint NOT NULL,
                        "AppliedOn" timestamp NULL,
                        "Description" text NULL
                    );
                    INSERT INTO "VersionInfo" ("Version", "AppliedOn", "Description")
                    SELECT g, NOW(), 'baseline'
                    FROM generate_series(1, 77) AS g;

                    CREATE TABLE "NotificationDefinitions" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "Name" text NOT NULL,
                        "Implementation" text NOT NULL,
                        "ConfigContract" text NULL,
                        "Settings" text NULL,
                        "Enable" boolean NOT NULL,
                        "Priority" integer NOT NULL,
                        "OnGrab" boolean NOT NULL,
                        "OnDownloadComplete" boolean NOT NULL,
                        "OnMediaInspected" boolean NOT NULL,
                        "OnExtractComplete" boolean NOT NULL,
                        "OnSeedGoalReached" boolean NOT NULL,
                        "OnTorrentDeleted" boolean NOT NULL,
                        "OnHealthIssue" boolean NOT NULL,
                        "OnHealthRestored" boolean NOT NULL,
                        "OnManualInteractionRequired" boolean NOT NULL,
                        "OnApplicationUpdate" boolean NOT NULL,
                        "OnBackupComplete" boolean NOT NULL,
                        "OnBackupFailed" boolean NOT NULL,
                        "Tags" text NOT NULL,
                        "Categories" text NOT NULL
                    );
                    """;
                cmd.ExecuteNonQuery();
            }

            var factory = new DbFactory();
            var db = factory.Create(DatabaseType.PostgreSQL, builder.ConnectionString);

            using var conn = db.OpenConnection();
            var column = conn.QueryFirstOrDefault<PostgresColumnInfo>(
                """
                SELECT column_name AS Name, data_type AS DataType, is_nullable AS IsNullable
                FROM information_schema.columns
                WHERE table_name = 'NotificationDefinitions'
                    AND column_name = 'FallbackNotificationId'
                """);

            Assert.That(column, Is.Not.Null);
            Assert.That(column.DataType, Is.EqualTo("integer"));
            Assert.That(column.IsNullable, Is.EqualTo("YES"));

            var repository = new NotificationRepository(db);
            var backup = repository.Insert(new NotificationDefinition
            {
                Name = "Postgres Backup",
                Implementation = "Webhook",
                Enable = true,
                OnGrab = true,
            });
            var primary = repository.Insert(new NotificationDefinition
            {
                Name = "Postgres Primary",
                Implementation = "Webhook",
                Enable = true,
                OnGrab = true,
                FallbackNotificationId = backup.Id,
            });

            Assert.That(repository.Get(primary.Id).FallbackNotificationId, Is.EqualTo(backup.Id));

            primary.FallbackNotificationId = null;
            repository.Update(primary);
            Assert.That(repository.Get(primary.Id).FallbackNotificationId, Is.Null);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
            admin.Open();
            using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
            drop.ExecuteNonQuery();
        }
    }

    private sealed class ColumnInfo
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public int NotNullFlag { get; set; }
    }

    private sealed class PostgresColumnInfo
    {
        public string Name { get; set; }

        public string DataType { get; set; }

        public string IsNullable { get; set; }
    }
}
