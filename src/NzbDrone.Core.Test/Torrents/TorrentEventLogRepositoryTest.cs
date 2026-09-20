using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentEventLogRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private TorrentEventLogRepository _subject;

    [SetUp]
    public void SetUp()
    {
        TableMapping.Register<TorrentEventLog>("TorrentEventLogs");

        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""TorrentEventLogs"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TorrentId"" INTEGER NOT NULL,
                ""TimeStamp"" TEXT NOT NULL,
                ""Level"" TEXT NOT NULL,
                ""Source"" TEXT,
                ""Message"" TEXT NOT NULL
            );";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new TorrentEventLogRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection?.Close();
        _keepAliveConnection?.Dispose();
    }

    [Test]
    public void InsertMany_should_insert_all_logs_in_a_single_call()
    {
        var logs = new List<TorrentEventLog>
        {
            new() { TorrentId = 1, TimeStamp = DateTime.UtcNow, Level = "Info", Source = "Tracker", Message = "Msg 1" },
            new() { TorrentId = 1, TimeStamp = DateTime.UtcNow, Level = "Warn", Source = "Tracker", Message = "Msg 2" },
            new() { TorrentId = 2, TimeStamp = DateTime.UtcNow, Level = "Error", Source = "Disk", Message = "Msg 3" },
        };

        _subject.InsertMany(logs);

        var torrent1Logs = _subject.GetByTorrentId(1, 10);
        var torrent2Logs = _subject.GetByTorrentId(2, 10);

        Assert.That(torrent1Logs, Has.Count.EqualTo(2));
        Assert.That(torrent2Logs, Has.Count.EqualTo(1));
    }

    [Test]
    public void Purge_should_delete_logs_older_than_specified_date()
    {
        var now = DateTime.UtcNow;
        var logs = new List<TorrentEventLog>
        {
            new() { TorrentId = 1, TimeStamp = now.AddDays(-10), Level = "Info", Source = "Sys", Message = "Old" },
            new() { TorrentId = 1, TimeStamp = now.AddDays(-2), Level = "Info", Source = "Sys", Message = "Recent" },
        };

        _subject.InsertMany(logs);

        _subject.Purge(now.AddDays(-5));

        var remaining = _subject.GetByTorrentId(1, 10);
        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(remaining[0].Message, Is.EqualTo("Recent"));
    }

    [Test]
    public void Purge_should_enforce_per_torrent_retention_limit()
    {
        var now = DateTime.UtcNow;
        var logs = new List<TorrentEventLog>();

        // Torrent 1 has 5 logs
        for (var i = 1; i <= 5; i++)
        {
            logs.Add(new TorrentEventLog
            {
                TorrentId = 1,
                TimeStamp = now.AddMinutes(i),
                Level = "Info",
                Source = "Sys",
                Message = $"T1 Msg {i}"
            });
        }

        // Torrent 2 has 2 logs
        for (var i = 1; i <= 2; i++)
        {
            logs.Add(new TorrentEventLog
            {
                TorrentId = 2,
                TimeStamp = now.AddMinutes(i),
                Level = "Info",
                Source = "Sys",
                Message = $"T2 Msg {i}"
            });
        }

        _subject.InsertMany(logs);

        // Retention limit of 3 per torrent
        _subject.Purge(now.AddDays(-1), 3);

        var t1Remaining = _subject.GetByTorrentId(1, 10);
        var t2Remaining = _subject.GetByTorrentId(2, 10);

        Assert.That(t1Remaining, Has.Count.EqualTo(3));
        // Newest 3 should remain (Msg 5, 4, 3)
        Assert.That(t1Remaining.Select(l => l.Message), Does.Contain("T1 Msg 5"));
        Assert.That(t1Remaining.Select(l => l.Message), Does.Contain("T1 Msg 4"));
        Assert.That(t1Remaining.Select(l => l.Message), Does.Contain("T1 Msg 3"));
        Assert.That(t1Remaining.Select(l => l.Message), Does.Not.Contain("T1 Msg 1"));

        // Torrent 2 had 2 logs, so all 2 should remain
        Assert.That(t2Remaining, Has.Count.EqualTo(2));
    }

    [Test]
    public void Purge_should_delete_large_sets_of_records_in_chunks()
    {
        var now = DateTime.UtcNow;
        var oldDate = now.AddDays(-30);
        var logs = new List<TorrentEventLog>();

        for (var i = 0; i < 1250; i++)
        {
            logs.Add(new TorrentEventLog
            {
                TorrentId = 1,
                TimeStamp = oldDate.AddMinutes(i),
                Level = "Info",
                Source = "Sys",
                Message = $"Old Log {i}"
            });
        }

        for (var i = 0; i < 5; i++)
        {
            logs.Add(new TorrentEventLog
            {
                TorrentId = 1,
                TimeStamp = now.AddMinutes(i),
                Level = "Info",
                Source = "Sys",
                Message = $"Recent Log {i}"
            });
        }

        _subject.InsertMany(logs);

        _subject.Purge(now.AddDays(-1), 0);

        var remaining = _subject.GetByTorrentId(1, 100);
        Assert.That(remaining, Has.Count.EqualTo(5));
    }

    [Test]
    public void Purge_with_custom_batch_size_should_delete_records_in_chunks()
    {
        var now = DateTime.UtcNow;
        var oldDate = now.AddDays(-30);
        var logs = new List<TorrentEventLog>();

        for (var i = 0; i < 25; i++)
        {
            logs.Add(new TorrentEventLog
            {
                TorrentId = 1,
                TimeStamp = oldDate.AddMinutes(i),
                Level = "Info",
                Source = "Sys",
                Message = $"Old Log {i}"
            });
        }

        for (var i = 0; i < 3; i++)
        {
            logs.Add(new TorrentEventLog
            {
                TorrentId = 1,
                TimeStamp = now.AddMinutes(i),
                Level = "Info",
                Source = "Sys",
                Message = $"Recent Log {i}"
            });
        }

        _subject.InsertMany(logs);

        // Delete with batchSize of 10
        _subject.Purge(now.AddDays(-1), 0, batchSize: 10);

        var remaining = _subject.GetByTorrentId(1, 100);
        Assert.That(remaining, Has.Count.EqualTo(3));
    }
}
