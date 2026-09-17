using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class DownloadHistoryRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private DownloadHistoryRepository _subject;

    [SetUp]
    public void SetUp()
    {
        TableMapping.Register<DownloadHistory>("DownloadHistory");

        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""DownloadHistory"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TorrentId"" INTEGER,
                ""Title"" TEXT NOT NULL,
                ""InfoHash"" TEXT NOT NULL,
                ""TotalSize"" INTEGER NOT NULL DEFAULT 0,
                ""DateAdded"" TEXT NOT NULL,
                ""DateCompleted"" TEXT,
                ""DateRemoved"" TEXT,
                ""Uploaded"" INTEGER NOT NULL DEFAULT 0,
                ""Downloaded"" INTEGER NOT NULL DEFAULT 0,
                ""Ratio"" REAL NOT NULL DEFAULT 0.0,
                ""SeedingTime"" INTEGER NOT NULL DEFAULT 0,
                ""PrimaryTracker"" TEXT,
                ""IndexerName"" TEXT,
                ""Source"" TEXT,
                ""MagnetUrl"" TEXT,
                ""DownloadUrl"" TEXT,
                ""Status"" TEXT NOT NULL DEFAULT 'Active',
                ""RemovalReason"" TEXT,
                ""DataJson"" TEXT
            );";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new DownloadHistoryRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection?.Close();
        _keepAliveConnection?.Dispose();
    }

    [Test]
    public void DeleteOlderThan_should_delete_large_sets_of_records_in_chunks()
    {
        var now = DateTime.UtcNow;
        var oldDate = now.AddDays(-30);
        var entries = new List<DownloadHistory>();

        for (var i = 0; i < 1250; i++)
        {
            entries.Add(new DownloadHistory
            {
                TorrentId = 1,
                Title = $"Old Torrent {i}",
                InfoHash = $"hash_{i}",
                DateAdded = oldDate.AddMinutes(i),
                DateRemoved = oldDate.AddDays(1),
                Status = "Removed"
            });
        }

        // Active torrents older than cutoff should NOT be deleted
        entries.Add(new DownloadHistory
        {
            TorrentId = 2,
            Title = "Active Old Torrent",
            InfoHash = "active_old_hash",
            DateAdded = oldDate,
            DateRemoved = null,
            Status = "Active"
        });

        // Recent removed torrents should NOT be deleted
        entries.Add(new DownloadHistory
        {
            TorrentId = 3,
            Title = "Recent Removed Torrent",
            InfoHash = "recent_removed_hash",
            DateAdded = now.AddDays(-1),
            DateRemoved = now,
            Status = "Removed"
        });

        _subject.InsertMany(entries);

        var deleted = _subject.DeleteOlderThan(now.AddDays(-7));

        Assert.That(deleted, Is.EqualTo(1250));

        var remaining = _subject.All();
        Assert.That(remaining, Has.Count.EqualTo(2));
        Assert.That(remaining.Any(r => r.InfoHash == "active_old_hash"), Is.True);
        Assert.That(remaining.Any(r => r.InfoHash == "recent_removed_hash"), Is.True);
    }
}
