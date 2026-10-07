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

    [Test]
    public void Migration_68_orphan_cleanup_removes_rows_with_null_or_missing_torrent_id()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        conn.Execute("PRAGMA foreign_keys = OFF;");

        conn.Execute(
            "INSERT INTO \"Torrents\" (\"Name\", \"InfoHash\", \"DateAdded\") VALUES ('Valid', 'valid_hash', '2026-01-01');");
        var torrentId = conn.QuerySingle<int>("SELECT \"Id\" FROM \"Torrents\" WHERE \"InfoHash\" = 'valid_hash'");

        conn.Execute(
            "INSERT INTO \"TorrentFiles\" (\"TorrentId\", \"Path\") VALUES (@TorrentId, 'valid.txt');",
            new { TorrentId = torrentId });
        conn.Execute("INSERT INTO \"TorrentFiles\" (\"TorrentId\", \"Path\") VALUES (NULL, 'orphan-null.txt');");
        conn.Execute("INSERT INTO \"TorrentFiles\" (\"TorrentId\", \"Path\") VALUES (999999, 'orphan-missing.txt');");

        conn.Execute(
            "INSERT INTO \"TrackerEntries\" (\"TorrentId\", \"Url\") VALUES (@TorrentId, 'https://valid.example');",
            new { TorrentId = torrentId });
        conn.Execute("INSERT INTO \"TrackerEntries\" (\"TorrentId\", \"Url\") VALUES (NULL, 'https://orphan-null.example');");
        conn.Execute("INSERT INTO \"TrackerEntries\" (\"TorrentId\", \"Url\") VALUES (999999, 'https://orphan-missing.example');");

        conn.Execute(
            """
            INSERT INTO "TorrentEventLogs" ("TorrentId", "TimeStamp", "Level", "Message")
            VALUES (@TorrentId, '2026-01-01', 'Info', 'valid');
            """,
            new { TorrentId = torrentId });
        conn.Execute(
            """
            INSERT INTO "TorrentEventLogs" ("TorrentId", "TimeStamp", "Level", "Message")
            VALUES (NULL, '2026-01-01', 'Info', 'orphan-null');
            """);
        conn.Execute(
            """
            INSERT INTO "TorrentEventLogs" ("TorrentId", "TimeStamp", "Level", "Message")
            VALUES (999999, '2026-01-01', 'Info', 'orphan-missing');
            """);

        conn.Execute(
            "INSERT INTO \"TorrentMediaMetadata\" (\"TorrentId\", \"ArrType\", \"Title\") VALUES (@TorrentId, 'movie', 'valid');",
            new { TorrentId = torrentId });
        conn.Execute(
            "INSERT INTO \"TorrentMediaMetadata\" (\"TorrentId\", \"ArrType\", \"Title\") VALUES (NULL, 'movie', 'orphan-null');");
        conn.Execute(
            "INSERT INTO \"TorrentMediaMetadata\" (\"TorrentId\", \"ArrType\", \"Title\") VALUES (999998, 'movie', 'orphan-missing');");

        Assert.That(conn.QuerySingle<int>("SELECT COUNT(*) FROM \"TorrentFiles\""), Is.EqualTo(3));
        Assert.That(conn.QuerySingle<int>("SELECT COUNT(*) FROM \"TrackerEntries\""), Is.EqualTo(3));
        Assert.That(conn.QuerySingle<int>("SELECT COUNT(*) FROM \"TorrentEventLogs\""), Is.EqualTo(3));
        Assert.That(conn.QuerySingle<int>("SELECT COUNT(*) FROM \"TorrentMediaMetadata\""), Is.EqualTo(3));

        RunMigration68OrphanCleanup(conn);

        Assert.That(conn.QuerySingle<int>("SELECT COUNT(*) FROM \"TorrentFiles\""), Is.EqualTo(1));
        Assert.That(conn.QuerySingle<string>("SELECT \"Path\" FROM \"TorrentFiles\""), Is.EqualTo("valid.txt"));
        Assert.That(conn.QuerySingle<int>("SELECT COUNT(*) FROM \"TrackerEntries\""), Is.EqualTo(1));
        Assert.That(conn.QuerySingle<int>("SELECT COUNT(*) FROM \"TorrentEventLogs\""), Is.EqualTo(1));
        Assert.That(conn.QuerySingle<int>("SELECT COUNT(*) FROM \"TorrentMediaMetadata\""), Is.EqualTo(1));
    }

    private static void RunMigration68OrphanCleanup(System.Data.IDbConnection conn)
    {
        // Same cleanup statements as FixVarcharTruncationAndCascadeDeletes.Up()
        conn.Execute(
            """
            DELETE FROM "TorrentFiles"
            WHERE "TorrentId" IS NULL
               OR NOT EXISTS (SELECT 1 FROM "Torrents" t WHERE t."Id" = "TorrentFiles"."TorrentId");
            """);
        conn.Execute(
            """
            DELETE FROM "TrackerEntries"
            WHERE "TorrentId" IS NULL
               OR NOT EXISTS (SELECT 1 FROM "Torrents" t WHERE t."Id" = "TrackerEntries"."TorrentId");
            """);
        conn.Execute(
            """
            DELETE FROM "TorrentEventLogs"
            WHERE "TorrentId" IS NULL
               OR NOT EXISTS (SELECT 1 FROM "Torrents" t WHERE t."Id" = "TorrentEventLogs"."TorrentId");
            """);
        conn.Execute(
            """
            DELETE FROM "TorrentMediaMetadata"
            WHERE "TorrentId" IS NULL
               OR NOT EXISTS (SELECT 1 FROM "Torrents" t WHERE t."Id" = "TorrentMediaMetadata"."TorrentId");
            """);
    }
}
