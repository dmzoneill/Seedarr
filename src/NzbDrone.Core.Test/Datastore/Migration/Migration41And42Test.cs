using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using FluentMigrator;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;

namespace NzbDrone.Core.Test.Datastore.Migration;

[TestFixture]
public class Migration41And42Test
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
    public void Migration_41_has_correct_metadata()
    {
        var migrationType = typeof(CaseInsensitiveInfoHashIndex);
        Assert.That(typeof(NzbDroneMigrationBase).IsAssignableFrom(migrationType), Is.True);

        var attr = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Version, Is.EqualTo(41));
    }

    [Test]
    public void Migration_42_has_correct_metadata()
    {
        var migrationType = typeof(CaseInsensitiveCategoryName);
        Assert.That(typeof(NzbDroneMigrationBase).IsAssignableFrom(migrationType), Is.True);

        var attr = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Version, Is.EqualTo(42));
    }

    [Test]
    public void DbFactory_Create_adds_case_insensitive_unique_indexes()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var indexes = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='index'").ToList();

        Assert.That(indexes, Does.Contain("IX_Torrents_InfoHash"));
        Assert.That(indexes, Does.Contain("IX_Categories_Name"));
    }

    [Test]
    public void CategoryRepository_rejects_duplicate_names_differing_only_by_case()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");
        var repository = new CategoryRepository(db);

        repository.Insert(new Category
        {
            Name = "movies",
            SavePath = "/data/movies",
        });

        Assert.Throws<SqliteException>(() => repository.Insert(new Category
        {
            Name = "Movies",
            SavePath = "/data/movies2",
        }));
    }

    [Test]
    public void Migration_41_dedup_sql_collapses_case_only_info_hash_duplicates()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        conn.Execute("DROP INDEX IF EXISTS \"IX_Torrents_InfoHash\";");
        conn.Execute(
            "INSERT INTO \"Torrents\" (\"Name\", \"InfoHash\", \"DateAdded\") VALUES ('A', 'ABCDEF0123456789ABCDEF0123456789ABCDEF01', '2026-01-01 00:00:00');");
        conn.Execute(
            "INSERT INTO \"Torrents\" (\"Name\", \"InfoHash\", \"DateAdded\") VALUES ('B', 'abcdef0123456789abcdef0123456789abcdef01', '2026-01-01 00:00:00');");

        conn.Execute(
            """
            DELETE FROM "Torrents"
            WHERE "InfoHash" IS NOT NULL
              AND "Id" NOT IN (
                  SELECT MIN("Id")
                  FROM "Torrents"
                  WHERE "InfoHash" IS NOT NULL
                  GROUP BY LOWER(TRIM("InfoHash"))
              );
            """);
        conn.Execute("UPDATE \"Torrents\" SET \"InfoHash\" = LOWER(TRIM(\"InfoHash\")) WHERE \"InfoHash\" IS NOT NULL;");
        conn.Execute(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Torrents_InfoHash\" ON \"Torrents\" (LOWER(\"InfoHash\"));");

        var count = conn.QuerySingle<int>("SELECT COUNT(*) FROM \"Torrents\" WHERE LOWER(\"InfoHash\") = 'abcdef0123456789abcdef0123456789abcdef01'");
        var hash = conn.QuerySingle<string>("SELECT \"InfoHash\" FROM \"Torrents\" LIMIT 1");

        Assert.That(count, Is.EqualTo(1));
        Assert.That(hash, Is.EqualTo("abcdef0123456789abcdef0123456789abcdef01"));
    }

    [Test]
    public void Migration_42_dedup_sql_collapses_case_only_category_name_duplicates()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        conn.Execute("DROP INDEX IF EXISTS \"IX_Categories_Name\";");
        conn.Execute(
            "INSERT INTO \"Categories\" (\"Name\", \"SavePath\") VALUES ('Movies', '/movies');");
        conn.Execute(
            "INSERT INTO \"Categories\" (\"Name\", \"SavePath\") VALUES ('movies', '/movies-alt');");

        conn.Execute(
            """
            DELETE FROM "Categories"
            WHERE "Id" NOT IN (
                SELECT MIN("Id")
                FROM "Categories"
                GROUP BY LOWER(TRIM("Name"))
            );
            """);
        conn.Execute("UPDATE \"Categories\" SET \"Name\" = LOWER(TRIM(\"Name\")) WHERE \"Name\" IS NOT NULL;");
        conn.Execute(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Categories_Name\" ON \"Categories\" (LOWER(\"Name\"));");

        var count = conn.QuerySingle<int>("SELECT COUNT(*) FROM \"Categories\" WHERE LOWER(\"Name\") = 'movies'");
        var name = conn.QuerySingle<string>("SELECT \"Name\" FROM \"Categories\" LIMIT 1");

        Assert.That(count, Is.EqualTo(1));
        Assert.That(name, Is.EqualTo("movies"));
    }
}
