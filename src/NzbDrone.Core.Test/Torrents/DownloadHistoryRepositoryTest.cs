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

    [Test]
    public void DeleteOlderThan_with_custom_batch_size_should_delete_records_in_chunks()
    {
        var now = DateTime.UtcNow;
        var oldDate = now.AddDays(-30);
        var entries = new List<DownloadHistory>();

        for (var i = 0; i < 25; i++)
        {
            entries.Add(new DownloadHistory
            {
                TorrentId = null,
                Title = $"Old Deleted Torrent {i}",
                InfoHash = $"old_hash_{i}",
                DateAdded = oldDate.AddMinutes(i),
                DateRemoved = oldDate.AddDays(1),
                Status = "Removed"
            });
        }

        for (var i = 0; i < 2; i++)
        {
            entries.Add(new DownloadHistory
            {
                TorrentId = i + 1,
                Title = $"Active Torrent {i}",
                InfoHash = $"active_hash_{i}",
                DateAdded = now.AddDays(-1),
                Status = "Active"
            });
        }

        _subject.InsertMany(entries);

        // Delete with batchSize of 10
        var deleted = _subject.DeleteOlderThan(now.AddDays(-7), batchSize: 10);

        Assert.That(deleted, Is.EqualTo(25));
        var remaining = _subject.All().ToList();
        Assert.That(remaining, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetCount_without_filters_returns_total_count()
    {
        var entries = new List<DownloadHistory>
        {
            new() { Title = "Ubuntu 22.04", InfoHash = "hash1", Status = "Active", PrimaryTracker = "tracker1" },
            new() { Title = "Debian 12", InfoHash = "hash2", Status = "Completed", PrimaryTracker = "tracker2" },
            new() { Title = "Arch Linux", InfoHash = "hash3", Status = "Removed", PrimaryTracker = "tracker3" }
        };

        _subject.InsertMany(entries);

        var count = _subject.GetCount();
        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public void GetCount_with_status_filter_returns_filtered_count()
    {
        var entries = new List<DownloadHistory>
        {
            new() { Title = "Ubuntu 22.04", InfoHash = "hash1", Status = "Active", PrimaryTracker = "tracker1" },
            new() { Title = "Debian 12", InfoHash = "hash2", Status = "Completed", PrimaryTracker = "tracker2" },
            new() { Title = "Fedora 40", InfoHash = "hash3", Status = "Active", PrimaryTracker = "tracker1" }
        };

        _subject.InsertMany(entries);

        var activeCount = _subject.GetCount(status: "Active");
        var completedCount = _subject.GetCount(status: "Completed");
        var removedCount = _subject.GetCount(status: "Removed");

        Assert.That(activeCount, Is.EqualTo(2));
        Assert.That(completedCount, Is.EqualTo(1));
        Assert.That(removedCount, Is.EqualTo(0));
    }

    [Test]
    public void GetCount_with_query_filter_returns_matching_count()
    {
        var entries = new List<DownloadHistory>
        {
            new() { Title = "Ubuntu 22.04 Server", InfoHash = "hash1", Status = "Active", PrimaryTracker = "tracker1" },
            new() { Title = "Debian 12 Bookworm", InfoHash = "hash2", Status = "Completed", PrimaryTracker = "specialtracker" },
            new() { Title = "Arch Linux", InfoHash = "ubuntuhash", Status = "Removed", PrimaryTracker = "tracker3" }
        };

        _subject.InsertMany(entries);

        var ubuntuCount = _subject.GetCount(query: "Ubuntu");
        var trackerCount = _subject.GetCount(query: "specialtracker");
        var nonExistentCount = _subject.GetCount(query: "FreeBSD");

        Assert.That(ubuntuCount, Is.EqualTo(2));
        Assert.That(trackerCount, Is.EqualTo(1));
        Assert.That(nonExistentCount, Is.EqualTo(0));
    }

    [Test]
    public void GetCount_with_query_and_status_filter_returns_conjunction_count()
    {
        var entries = new List<DownloadHistory>
        {
            new() { Title = "Ubuntu 22.04 Desktop", InfoHash = "hash1", Status = "Active" },
            new() { Title = "Ubuntu 24.04 Server", InfoHash = "hash2", Status = "Completed" },
            new() { Title = "Debian 12", InfoHash = "hash3", Status = "Active" }
        };

        _subject.InsertMany(entries);

        var activeUbuntu = _subject.GetCount(query: "Ubuntu", status: "Active");
        var completedUbuntu = _subject.GetCount(query: "Ubuntu", status: "Completed");
        var removedUbuntu = _subject.GetCount(query: "Ubuntu", status: "Removed");

        Assert.That(activeUbuntu, Is.EqualTo(1));
        Assert.That(completedUbuntu, Is.EqualTo(1));
        Assert.That(removedUbuntu, Is.EqualTo(0));
    }
}
