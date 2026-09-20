using System;
using System.Collections.Generic;
using System.Linq;
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

    [Test]
    public void GetConnectionCounts_should_return_encrypted_and_plaintext_counts()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        _subject.Insert(new PeerConnectionLog
        {
            InfoHash = "aaa",
            RemoteIp = "127.0.0.1",
            IsEncrypted = true,
            EventType = "Connected",
            Timestamp = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        });

        _subject.Insert(new PeerConnectionLog
        {
            InfoHash = "bbb",
            RemoteIp = "127.0.0.1",
            IsEncrypted = true,
            EventType = "Connected",
            Timestamp = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)
        });

        _subject.Insert(new PeerConnectionLog
        {
            InfoHash = "ccc",
            RemoteIp = "127.0.0.1",
            IsEncrypted = false,
            EventType = "Connected",
            Timestamp = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
        });

        _subject.Insert(new PeerConnectionLog
        {
            InfoHash = "ddd",
            RemoteIp = "127.0.0.1",
            IsEncrypted = true,
            EventType = "Disconnected",
            Timestamp = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)
        });

        var (encrypted, plaintext) = _subject.GetConnectionCounts(start, end);

        Assert.That(encrypted, Is.EqualTo(2));
        Assert.That(plaintext, Is.EqualTo(1));
    }

    [Test]
    public void Purge_should_enforce_max_rows_retention_limit()
    {
        var now = DateTime.UtcNow;

        for (var i = 1; i <= 10; i++)
        {
            InsertLog($"hash-{i}", now.AddMinutes(i));
        }

        // Keep at most 4 newest rows
        _subject.Purge(now.AddDays(-1), 4);

        var remaining = _subject.All().ToList();
        Assert.That(remaining, Has.Count.EqualTo(4));
        Assert.That(remaining.Select(l => l.InfoHash), Does.Contain("hash-10"));
        Assert.That(remaining.Select(l => l.InfoHash), Does.Contain("hash-9"));
        Assert.That(remaining.Select(l => l.InfoHash), Does.Contain("hash-8"));
        Assert.That(remaining.Select(l => l.InfoHash), Does.Contain("hash-7"));
        Assert.That(remaining.Select(l => l.InfoHash), Does.Not.Contain("hash-1"));
    }

    [Test]
    public void Purge_should_delete_large_sets_of_records_in_chunks()
    {
        var now = DateTime.UtcNow;
        var oldDate = now.AddDays(-30);
        var oldLogs = new List<PeerConnectionLog>(1100);

        for (var i = 0; i < 1100; i++)
        {
            oldLogs.Add(new PeerConnectionLog
            {
                InfoHash = $"old-{i}",
                TorrentName = "test.torrent",
                RemoteIp = "127.0.0.1",
                RemotePort = 6881,
                IsEncrypted = false,
                EventType = "Connected",
                Timestamp = oldDate.AddMinutes(i)
            });
        }

        _subject.InsertMany(oldLogs);

        for (var i = 0; i < 5; i++)
        {
            InsertLog($"recent-{i}", now.AddMinutes(i));
        }

        _subject.Purge(now.AddDays(-1), 50000);

        var remaining = _subject.All().ToList();
        Assert.That(remaining, Has.Count.EqualTo(5));
    }

    [Test]
    public void GetByTimeRange_should_paginate_with_limit_and_offset()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= 10; i++)
        {
            InsertLog($"hash-{i:D2}", start.AddDays(i));
        }

        var page1 = _subject.GetByTimeRange(start, end, limit: 3, offset: 0);
        var page2 = _subject.GetByTimeRange(start, end, limit: 3, offset: 3);

        Assert.That(page1, Has.Count.EqualTo(3));
        Assert.That(page2, Has.Count.EqualTo(3));
        Assert.That(page1[0].InfoHash, Is.EqualTo("hash-10"));
        Assert.That(page1[1].InfoHash, Is.EqualTo("hash-09"));
        Assert.That(page1[2].InfoHash, Is.EqualTo("hash-08"));
        Assert.That(page2[0].InfoHash, Is.EqualTo("hash-07"));
        Assert.That(page2[1].InfoHash, Is.EqualTo("hash-06"));
        Assert.That(page2[2].InfoHash, Is.EqualTo("hash-05"));
    }

    [Test]
    public void GetByInfoHash_should_paginate_with_limit_and_offset()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        const string targetHash = "paginatedhash";

        for (var i = 1; i <= 5; i++)
        {
            InsertLog(targetHash, start.AddDays(i));
        }

        var page1 = _subject.GetByInfoHash(targetHash, start, end, limit: 2, offset: 0);
        var page2 = _subject.GetByInfoHash(targetHash, start, end, limit: 2, offset: 2);

        Assert.That(page1, Has.Count.EqualTo(2));
        Assert.That(page2, Has.Count.EqualTo(2));
        Assert.That(page1[0].Timestamp, Is.GreaterThan(page1[1].Timestamp));
        Assert.That(page2[0].Timestamp, Is.GreaterThan(page2[1].Timestamp));
        Assert.That(page1[1].Timestamp, Is.GreaterThan(page2[0].Timestamp));
    }

    [Test]
    public void GetLogs_and_GetLogsByInfoHash_aliases_should_work_with_pagination()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        const string targetHash = "aliashash";

        for (var i = 1; i <= 4; i++)
        {
            InsertLog(targetHash, start.AddDays(i));
        }

        var timeRangeLogs = _subject.GetLogs(start, end, limit: 2, offset: 1);
        var infoHashLogs = _subject.GetLogsByInfoHash(targetHash, start, end, limit: 2, offset: 1);

        Assert.That(timeRangeLogs, Has.Count.EqualTo(2));
        Assert.That(infoHashLogs, Has.Count.EqualTo(2));
    }

    [Test]
    public void Purge_with_custom_batch_size_should_delete_in_chunks()
    {
        var now = DateTime.UtcNow;
        var oldDate = now.AddDays(-30);

        for (var i = 0; i < 25; i++)
        {
            InsertLog($"old-batch-{i}", oldDate.AddMinutes(i));
        }

        for (var i = 0; i < 3; i++)
        {
            InsertLog($"recent-{i}", now.AddMinutes(i));
        }

        // Batch size 10, should take 3 iterations
        _subject.Purge(now.AddDays(-1), 50000, batchSize: 10);

        var remaining = _subject.All().ToList();
        Assert.That(remaining, Has.Count.EqualTo(3));
    }
}
