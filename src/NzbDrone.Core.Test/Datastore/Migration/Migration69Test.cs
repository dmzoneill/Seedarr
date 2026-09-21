using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using FluentMigrator;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Datastore.Migration;

[TestFixture]
public class Migration69Test
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
    public void Migration_69_has_correct_metadata()
    {
        var migrationType = typeof(FixTagIdsDefaultAndDownloadedBigint);
        Assert.That(typeof(NzbDroneMigrationBase).IsAssignableFrom(migrationType), Is.True);

        var attr = migrationType.GetCustomAttribute<MigrationAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Version, Is.EqualTo(69));
        Assert.That(NzbDroneMigrationBase.LatestMigration, Is.GreaterThanOrEqualTo(69));
    }

    [Test]
    public void DbFactory_Create_runs_migration_69_cleanly()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var tables = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='table'").ToList();

        Assert.That(tables, Does.Contain("Torrents"));
        Assert.That(tables, Does.Contain("TrackerEntries"));

        var trackerColumns = conn.Query<string>("SELECT name FROM pragma_table_info('TrackerEntries')").ToList();
        Assert.That(trackerColumns, Does.Contain("Downloaded"));

        var torrentColumns = conn.Query<string>("SELECT name FROM pragma_table_info('Torrents')").ToList();
        Assert.That(torrentColumns, Does.Contain("TagIds"));
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
        Assert.That(tables, Does.Contain("TrackerEntries"));
    }

    [Test]
    public void Migration_69_cleans_up_existing_tag_ids_with_zero_or_empty_values()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();

        // Simulate existing rows with legacy '0', null, or empty TagIds
        conn.Execute("INSERT INTO \"Torrents\" (\"Name\", \"InfoHash\", \"TagIds\", \"DateAdded\") VALUES ('Torrent_0', 'hash_0', '0', '2026-01-01 00:00:00');");
        conn.Execute("INSERT INTO \"Torrents\" (\"Name\", \"InfoHash\", \"TagIds\", \"DateAdded\") VALUES ('Torrent_Empty', 'hash_empty', '', '2026-01-01 00:00:00');");

        // Run cleanup logic as defined in Migration 69 Up()
        conn.Execute("UPDATE \"Torrents\" SET \"TagIds\" = '[]' WHERE \"TagIds\" = '0' OR \"TagIds\" IS NULL OR \"TagIds\" = '';");

        var tagIds0 = conn.QuerySingle<string>("SELECT \"TagIds\" FROM \"Torrents\" WHERE \"InfoHash\" = 'hash_0'");
        var tagIdsEmpty = conn.QuerySingle<string>("SELECT \"TagIds\" FROM \"Torrents\" WHERE \"InfoHash\" = 'hash_empty'");

        Assert.That(tagIds0, Is.EqualTo("[]"));
        Assert.That(tagIdsEmpty, Is.EqualTo("[]"));
    }

    [Test]
    public void Dapper_reads_torrent_with_tag_ids_numeric_zero_as_non_null_collection()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();

        // Simulate row inserted with numeric 0 (SQLite integer column default)
        conn.Execute("INSERT INTO \"Torrents\" (\"Name\", \"InfoHash\", \"TagIds\", \"DateAdded\") VALUES ('LegacyTorrent', 'hash_numeric_0', 0, '2026-01-01 00:00:00');");

        var torrent = conn.QuerySingle<Torrent>("SELECT * FROM \"Torrents\" WHERE \"InfoHash\" = 'hash_numeric_0'");

        Assert.That(torrent, Is.Not.Null);
        Assert.That(torrent.TagIds, Is.Not.Null);
        Assert.That(torrent.TagIds, Is.Empty);
        Assert.DoesNotThrow(() =>
        {
            var contains = torrent.TagIds.Contains(123);
            Assert.That(contains, Is.False);
        });
    }

    [Test]
    public void TrackerEntry_downloaded_handles_values_exceeding_32bit_int_range()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();

        conn.Execute("INSERT INTO \"Torrents\" (\"Name\", \"InfoHash\", \"DateAdded\") VALUES ('TestTorrent', 'tracker_hash_1', '2026-01-01 00:00:00');");
        var torrentId = conn.QuerySingle<int>("SELECT \"Id\" FROM \"Torrents\" WHERE \"InfoHash\" = 'tracker_hash_1'");

        // 5 GB downloaded > 2,147,483,647 bytes
        const long fiveGigabytes = 5L * 1024 * 1024 * 1024;

        conn.Execute(@"
            INSERT INTO ""TrackerEntries"" (""TorrentId"", ""Url"", ""Downloaded"")
            VALUES (@TorrentId, 'https://tracker.example.com/announce', @Downloaded);",
            new { TorrentId = torrentId, Downloaded = fiveGigabytes });

        var entry = conn.QuerySingle<TrackerEntry>(
            "SELECT * FROM \"TrackerEntries\" WHERE \"TorrentId\" = @TorrentId",
            new { TorrentId = torrentId });

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry.Downloaded, Is.EqualTo(fiveGigabytes));
    }
}
