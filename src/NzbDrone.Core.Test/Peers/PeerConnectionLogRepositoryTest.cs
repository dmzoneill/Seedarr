using System;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class PeerConnectionLogRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private PeerConnectionLogRepository _subject;

    [SetUp]
    public void SetUp()
    {
        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""PeerConnectionLogs"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""InfoHash"" TEXT,
                ""TorrentName"" TEXT,
                ""RemoteIp"" TEXT,
                ""RemotePort"" INTEGER NOT NULL DEFAULT 0,
                ""PeerId"" TEXT,
                ""IsEncrypted"" INTEGER NOT NULL DEFAULT 0,
                ""EventType"" TEXT,
                ""Timestamp"" TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new PeerConnectionLogRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection.Close();
        _keepAliveConnection.Dispose();
    }

    private PeerConnectionLog InsertLog(string infoHash, DateTime timestamp, string eventType = "Connected")
    {
        var log = new PeerConnectionLog
        {
            InfoHash = infoHash,
            TorrentName = "test.torrent",
            RemoteIp = "127.0.0.1",
            RemotePort = 6881,
            IsEncrypted = false,
            EventType = eventType,
            Timestamp = timestamp
        };
        return _subject.Insert(log);
    }

    [Test]
    public void GetByTimeRange_should_return_empty_when_no_records()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = _subject.GetByTimeRange(start, end);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetByTimeRange_should_return_records_within_range()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        InsertLog("aaa", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var result = _subject.GetByTimeRange(start, end);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("aaa"));
    }

    [Test]
    public void GetByTimeRange_should_exclude_records_before_start()
    {
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        InsertLog("before", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        InsertLog("within", new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc));

        var result = _subject.GetByTimeRange(start, end);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("within"));
    }

    [Test]
    public void GetByTimeRange_should_exclude_records_after_end()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        InsertLog("within", new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        InsertLog("after", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));

        var result = _subject.GetByTimeRange(start, end);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("within"));
    }

    [Test]
    public void GetByTimeRange_should_include_boundary_timestamps()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        InsertLog("at-start", start);
        InsertLog("at-end", end);

        var result = _subject.GetByTimeRange(start, end);

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetByTimeRange_should_return_multiple_records()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        InsertLog("aaa", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        InsertLog("bbb", new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        InsertLog("ccc", new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc));

        var result = _subject.GetByTimeRange(start, end);

        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void GetByInfoHash_should_return_empty_when_no_records()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var result = _subject.GetByInfoHash("deadbeef", start, end);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetByInfoHash_should_return_matching_records()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var ts = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        InsertLog("deadbeef", ts);
        InsertLog("cafebabe", ts);

        var result = _subject.GetByInfoHash("deadbeef", start, end);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("deadbeef"));
    }

    [Test]
    public void GetByInfoHash_should_exclude_records_outside_time_range()
    {
        var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        InsertLog("deadbeef", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        InsertLog("deadbeef", new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc));

        var result = _subject.GetByInfoHash("deadbeef", start, end);

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void GetByInfoHash_should_return_empty_for_wrong_hash()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        InsertLog("deadbeef", new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));

        var result = _subject.GetByInfoHash("wronghash", start, end);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Purge_should_delete_records_before_cutoff()
    {
        var cutoff = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        InsertLog("old1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        InsertLog("old2", new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        InsertLog("new1", new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc));

        _subject.Purge(cutoff);

        var all = _subject.All();
        Assert.That(all, Has.Exactly(1).Items);
    }

    [Test]
    public void Purge_should_keep_records_at_or_after_cutoff()
    {
        var cutoff = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        InsertLog("at-cutoff", cutoff);
        InsertLog("after-cutoff", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));

        _subject.Purge(cutoff);

        var all = _subject.All();
        Assert.That(all, Has.Exactly(2).Items);
    }

    [Test]
    public void Purge_should_not_fail_when_no_records_exist()
    {
        var cutoff = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        Assert.DoesNotThrow(() => _subject.Purge(cutoff));
    }

    [Test]
    public void Purge_should_delete_all_records_when_cutoff_is_in_future()
    {
        InsertLog("aaa", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        InsertLog("bbb", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        _subject.Purge(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var all = _subject.All();
        Assert.That(all, Is.Empty);
    }
}
