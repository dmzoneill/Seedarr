using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TrackerEntryRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private TrackerEntryRepository _subject;

    [SetUp]
    public void SetUp()
    {
        // Register the table name before use (type.Name + "s" would give "TrackerEntrys", not "TrackerEntries")
        TableMapping.Register<TrackerEntry>("TrackerEntries");

        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""TrackerEntries"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TorrentId"" INTEGER NOT NULL,
                ""Url"" TEXT NOT NULL,
                ""Tier"" INTEGER NOT NULL DEFAULT 0,
                ""Status"" INTEGER NOT NULL DEFAULT 0,
                ""Enabled"" INTEGER NOT NULL DEFAULT 1,
                ""Seeders"" INTEGER NOT NULL DEFAULT 0,
                ""Leechers"" INTEGER NOT NULL DEFAULT 0,
                ""Downloaded"" INTEGER NOT NULL DEFAULT 0,
                ""TotalAnnounces"" INTEGER NOT NULL DEFAULT 0,
                ""SuccessfulAnnounces"" INTEGER NOT NULL DEFAULT 0,
                ""ConsecutiveFailures"" INTEGER NOT NULL DEFAULT 0,
                ""LastResponseTime"" REAL NOT NULL DEFAULT 0.0,
                ""AverageResponseTime"" REAL NOT NULL DEFAULT 0.0,
                ""AnnounceInterval"" INTEGER NOT NULL DEFAULT 1800,
                ""MinAnnounceInterval"" INTEGER NOT NULL DEFAULT 60,
                ""LastAnnounce"" TEXT,
                ""LastScrape"" TEXT,
                ""NextAnnounce"" TEXT,
                ""ErrorMessage"" TEXT,
                ""LastErrorTime"" TEXT,
                ""WarningMessage"" TEXT
            )";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new TrackerEntryRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection.Close();
        _keepAliveConnection.Dispose();
    }

    private static TrackerEntry MakeEntry(int torrentId, string url, int tier = 0) =>
        new() { TorrentId = torrentId, Url = url, Tier = tier, Enabled = true };

    [Test]
    public void GetByTorrentId_returns_empty_when_no_entries_exist()
    {
        var result = _subject.GetByTorrentId(1);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetByTorrentId_returns_only_entries_for_requested_torrent()
    {
        _subject.Insert(MakeEntry(1, "http://tracker1.example/announce"));
        _subject.Insert(MakeEntry(2, "http://tracker2.example/announce"));

        var result = _subject.GetByTorrentId(1);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].TorrentId, Is.EqualTo(1));
        Assert.That(result[0].Url, Is.EqualTo("http://tracker1.example/announce"));
    }

    [Test]
    public void GetByTorrentId_returns_all_entries_for_torrent()
    {
        _subject.Insert(MakeEntry(1, "http://tracker1.example/announce", tier: 0));
        _subject.Insert(MakeEntry(1, "http://tracker2.example/announce", tier: 1));
        _subject.Insert(MakeEntry(2, "http://other.example/announce"));

        var result = _subject.GetByTorrentId(1);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(
            result.Select(e => e.Url),
            Is.EquivalentTo(new[] { "http://tracker1.example/announce", "http://tracker2.example/announce" }));
    }

    [Test]
    public void GetByTorrentId_returns_entries_ordered_by_tier_then_url()
    {
        _subject.Insert(MakeEntry(1, "http://b.example/announce", tier: 1));
        _subject.Insert(MakeEntry(1, "http://z.example/announce", tier: 0));
        _subject.Insert(MakeEntry(1, "http://a.example/announce", tier: 0));

        var result = _subject.GetByTorrentId(1);

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Url, Is.EqualTo("http://a.example/announce"));
        Assert.That(result[1].Url, Is.EqualTo("http://z.example/announce"));
        Assert.That(result[2].Url, Is.EqualTo("http://b.example/announce"));
    }

    [Test]
    public void DeleteByTorrentId_removes_all_entries_for_torrent()
    {
        _subject.Insert(MakeEntry(1, "http://tracker1.example/announce"));
        _subject.Insert(MakeEntry(1, "http://tracker2.example/announce"));

        _subject.DeleteByTorrentId(1);

        Assert.That(_subject.GetByTorrentId(1), Is.Empty);
    }

    [Test]
    public void DeleteByTorrentId_does_not_remove_entries_for_other_torrents()
    {
        _subject.Insert(MakeEntry(1, "http://tracker1.example/announce"));
        _subject.Insert(MakeEntry(2, "http://tracker2.example/announce"));

        _subject.DeleteByTorrentId(1);
        var remaining = _subject.GetByTorrentId(2);

        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(remaining[0].TorrentId, Is.EqualTo(2));
    }

    [Test]
    public void DeleteByTorrentId_on_nonexistent_torrent_does_not_throw()
    {
        Assert.DoesNotThrow(() => _subject.DeleteByTorrentId(9999));
    }

    [Test]
    public void GetByTorrentId_returns_empty_after_all_entries_deleted()
    {
        _subject.Insert(MakeEntry(1, "http://tracker.example/announce"));
        _subject.DeleteByTorrentId(1);

        Assert.That(_subject.GetByTorrentId(1), Is.Empty);
    }
}
