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

    [Test]
    public void DeleteOlderThan_should_preserve_active_and_seeding_torrents_and_delete_removed_torrents()
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-30);
        var oldDate = now.AddDays(-60);

        var entries = new List<DownloadHistory>
        {
            // Active torrent older than cutoff - must NOT be deleted
            new()
            {
                TorrentId = 1,
                Title = "Active Old Torrent",
                InfoHash = "active_hash",
                DateAdded = oldDate,
                DateRemoved = null,
                Status = "Active"
            },
            // Seeding torrent older than cutoff - must NOT be deleted
            new()
            {
                TorrentId = 2,
                Title = "Seeding Old Torrent",
                InfoHash = "seeding_hash",
                DateAdded = oldDate,
                DateRemoved = null,
                Status = "Seeding"
            },
            // Completed torrent still active in library older than cutoff - must NOT be deleted
            new()
            {
                TorrentId = 3,
                Title = "Completed In Library Old Torrent",
                InfoHash = "completed_lib_hash",
                DateAdded = oldDate,
                DateRemoved = null,
                Status = "Completed"
            },
            // Removed torrent older than cutoff with DateRemoved < cutoff - MUST be deleted
            new()
            {
                TorrentId = null,
                Title = "Removed Old Torrent",
                InfoHash = "removed_hash",
                DateAdded = oldDate,
                DateRemoved = oldDate.AddDays(5),
                Status = "Removed"
            },
            // Inactive torrent older than cutoff with null DateRemoved and null TorrentId - MUST be deleted
            new()
            {
                TorrentId = null,
                Title = "Inactive Old Torrent",
                InfoHash = "inactive_hash",
                DateAdded = oldDate,
                DateRemoved = null,
                Status = "Inactive"
            },
            // Recently removed torrent older than cutoff DateAdded - must NOT be deleted
            new()
            {
                TorrentId = null,
                Title = "Recently Removed Old Torrent",
                InfoHash = "recent_removed_hash",
                DateAdded = oldDate,
                DateRemoved = now.AddDays(-2),
                Status = "Removed"
            }
        };

        _subject.InsertMany(entries);

        var deleted = _subject.DeleteOlderThan(cutoff);

        Assert.That(deleted, Is.EqualTo(2));

        var remaining = _subject.All().ToList();
        Assert.That(remaining, Has.Count.EqualTo(4));
        Assert.That(remaining.Any(r => r.InfoHash == "active_hash"), Is.True);
        Assert.That(remaining.Any(r => r.InfoHash == "seeding_hash"), Is.True);
        Assert.That(remaining.Any(r => r.InfoHash == "completed_lib_hash"), Is.True);
        Assert.That(remaining.Any(r => r.InfoHash == "recent_removed_hash"), Is.True);
        Assert.That(remaining.Any(r => r.InfoHash == "removed_hash"), Is.False);
        Assert.That(remaining.Any(r => r.InfoHash == "inactive_hash"), Is.False);
    }
}
