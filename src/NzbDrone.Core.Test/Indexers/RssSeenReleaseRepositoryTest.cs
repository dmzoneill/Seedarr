using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class RssSeenReleaseRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private RssSeenReleaseRepository _subject;

    [SetUp]
    public void SetUp()
    {
        TableMapping.Register<RssSeenRelease>("RssSeenReleases");

        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""RssSeenReleases"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""IndexerId"" INTEGER NOT NULL,
                ""Guid"" TEXT NOT NULL DEFAULT '',
                ""InfoHash"" TEXT,
                ""PublishDate"" TEXT,
                ""FirstSeenUtc"" TEXT NOT NULL,
                ""Status"" INTEGER NOT NULL DEFAULT 1,
                ""MatchedRuleId"" INTEGER
            );
            CREATE INDEX ""IX_RssSeenReleases_IndexerId_Guid"" ON ""RssSeenReleases"" (""IndexerId"", ""Guid"");
            CREATE INDEX ""IX_RssSeenReleases_IndexerId_InfoHash"" ON ""RssSeenReleases"" (""IndexerId"", ""InfoHash"");
            CREATE INDEX ""IX_RssSeenReleases_FirstSeenUtc"" ON ""RssSeenReleases"" (""FirstSeenUtc"");";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new RssSeenReleaseRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection?.Close();
        _keepAliveConnection?.Dispose();
    }

    [Test]
    public void IsSeen_should_return_false_when_not_seen()
    {
        Assert.That(_subject.IsSeen(1, "guid-1", "hash1"), Is.False);
        Assert.That(_subject.IsSeen(1, "guid-1"), Is.False);
        Assert.That(_subject.IsSeen(1, null, "hash1"), Is.False);
        Assert.That(_subject.IsSeen(1, null, null), Is.False);
    }

    [Test]
    public void IsSeen_should_return_true_when_guid_is_seen()
    {
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 1,
            Guid = "unique-guid-123",
            FirstSeenUtc = DateTime.UtcNow,
            Status = RssSeenStatus.Grabbed
        });

        Assert.That(_subject.IsSeen(1, "unique-guid-123"), Is.True);
        Assert.That(_subject.IsSeen(1, "unique-guid-123", "non-existent-hash"), Is.True);
        Assert.That(_subject.IsSeen(2, "unique-guid-123"), Is.False);
    }

    [Test]
    public void IsSeen_should_return_true_when_infohash_is_seen_case_insensitively()
    {
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 1,
            Guid = "guid-1",
            InfoHash = "4a5b6c7d8e9f0123456789abcdef0123456789ab",
            FirstSeenUtc = DateTime.UtcNow,
            Status = RssSeenStatus.Ignored
        });

        Assert.That(_subject.IsSeen(1, "different-guid", "4A5B6C7D8E9F0123456789ABCDEF0123456789AB"), Is.True);
        Assert.That(_subject.IsSeen(1, null, "4a5b6c7d8e9f0123456789abcdef0123456789ab"), Is.True);
        Assert.That(_subject.IsSeen(2, null, "4a5b6c7d8e9f0123456789abcdef0123456789ab"), Is.False);
    }

    [Test]
    public void GetSeenGuids_should_return_guids_for_indexer_only()
    {
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 1,
            Guid = "guid-indexer1-a",
            FirstSeenUtc = DateTime.UtcNow,
            Status = RssSeenStatus.Grabbed
        });
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 1,
            Guid = "guid-indexer1-b",
            FirstSeenUtc = DateTime.UtcNow,
            Status = RssSeenStatus.Ignored
        });
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 2,
            Guid = "guid-indexer2-a",
            FirstSeenUtc = DateTime.UtcNow,
            Status = RssSeenStatus.Grabbed
        });

        var guids1 = _subject.GetSeenGuids(1);
        var guids2 = _subject.GetSeenGuids(2);

        Assert.That(guids1, Has.Count.EqualTo(2));
        Assert.That(guids1.Contains("guid-indexer1-a"), Is.True);
        Assert.That(guids1.Contains("guid-indexer1-b"), Is.True);
        Assert.That(guids1.Contains("guid-indexer2-a"), Is.False);

        Assert.That(guids2, Has.Count.EqualTo(1));
        Assert.That(guids2.Contains("guid-indexer2-a"), Is.True);
    }

    [Test]
    public void GetSeenInfoHashes_should_return_info_hashes_for_indexer_only()
    {
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 1,
            Guid = "g1",
            InfoHash = "hash1",
            FirstSeenUtc = DateTime.UtcNow,
            Status = RssSeenStatus.Grabbed
        });
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 2,
            Guid = "g2",
            InfoHash = "hash2",
            FirstSeenUtc = DateTime.UtcNow,
            Status = RssSeenStatus.Grabbed
        });

        var hashes1 = _subject.GetSeenInfoHashes(1);
        var hashes2 = _subject.GetSeenInfoHashes(2);

        Assert.That(hashes1, Has.Count.EqualTo(1));
        Assert.That(hashes1.Contains("hash1"), Is.True);
        Assert.That(hashes1.Contains("HASH1"), Is.True); // Case-insensitive check
        Assert.That(hashes1.Contains("hash2"), Is.False);

        Assert.That(hashes2, Has.Count.EqualTo(1));
        Assert.That(hashes2.Contains("hash2"), Is.True);
    }

    [Test]
    public void MarkSeen_should_insert_new_seen_release()
    {
        var release = new ReleaseInfo
        {
            Guid = "guid-new",
            InfoHash = "AABBCCDDEEFF00112233445566778899AABBCCDD",
            PublishDate = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc)
        };

        _subject.MarkSeen(1, release, RssSeenStatus.Grabbed, 42);

        Assert.That(_subject.IsSeen(1, "guid-new"), Is.True);
        var all = _subject.All().ToList();
        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].IndexerId, Is.EqualTo(1));
        Assert.That(all[0].Guid, Is.EqualTo("guid-new"));
        Assert.That(all[0].InfoHash, Is.EqualTo("aabbccddeeff00112233445566778899aabbccdd"));
        Assert.That(all[0].Status, Is.EqualTo(RssSeenStatus.Grabbed));
        Assert.That(all[0].MatchedRuleId, Is.EqualTo(42));
    }

    [Test]
    public void MarkSeen_should_update_existing_release_status_and_rule_id()
    {
        var release = new ReleaseInfo
        {
            Guid = "guid-update",
            InfoHash = "11223344556677889900aabbccddeeff11223344"
        };

        _subject.MarkSeen(1, release, RssSeenStatus.Ignored, null);
        var initial = _subject.All().ToList();
        Assert.That(initial, Has.Count.EqualTo(1));
        Assert.That(initial[0].Status, Is.EqualTo(RssSeenStatus.Ignored));
        Assert.That(initial[0].MatchedRuleId, Is.Null);

        _subject.MarkSeen(1, release, RssSeenStatus.Grabbed, 99);
        var updated = _subject.All().ToList();
        Assert.That(updated, Has.Count.EqualTo(1));
        Assert.That(updated[0].Status, Is.EqualTo(RssSeenStatus.Grabbed));
        Assert.That(updated[0].MatchedRuleId, Is.EqualTo(99));
    }

    [Test]
    public void MarkSeenBatch_should_insert_unseen_releases_and_ignore_seen_or_batch_duplicates()
    {
        var existing = new ReleaseInfo { Guid = "seen-before", InfoHash = "hash-seen" };
        _subject.MarkSeen(1, existing, RssSeenStatus.Ignored);

        var batch = new List<ReleaseInfo>
        {
            new() { Guid = "seen-before", InfoHash = "hash-seen" },
            new() { Guid = "item-1", InfoHash = "hash-1" },
            new() { Guid = "item-2", InfoHash = "hash-2" },
            new() { Guid = "item-1", InfoHash = "hash-1" }, // duplicate in batch
            new() { Guid = "item-3", InfoHash = "hash-2" }, // duplicate hash in batch
            new() { Guid = "item-4", InfoHash = "hash-4" }
        };

        _subject.MarkSeenBatch(1, batch, RssSeenStatus.Rejected);

        var all = _subject.All().ToList();
        // existing + item-1 + item-2 + item-4 = 4 items
        Assert.That(all, Has.Count.EqualTo(4));
        Assert.That(_subject.IsSeen(1, "item-1"), Is.True);
        Assert.That(_subject.IsSeen(1, "item-2"), Is.True);
        Assert.That(_subject.IsSeen(1, "item-4"), Is.True);
    }

    [Test]
    public void PurgeOlderThan_should_remove_records_older_than_timespan()
    {
        var now = DateTime.UtcNow;
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 1,
            Guid = "old-1",
            FirstSeenUtc = now.AddDays(-20),
            Status = RssSeenStatus.Ignored
        });
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 1,
            Guid = "old-2",
            FirstSeenUtc = now.AddDays(-15),
            Status = RssSeenStatus.Ignored
        });
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 1,
            Guid = "recent-1",
            FirstSeenUtc = now.AddDays(-5),
            Status = RssSeenStatus.Grabbed
        });
        _subject.Insert(new RssSeenRelease
        {
            IndexerId = 1,
            Guid = "recent-2",
            FirstSeenUtc = now.AddHours(-1),
            Status = RssSeenStatus.Grabbed
        });

        var purged = _subject.PurgeOlderThan(TimeSpan.FromDays(14));

        Assert.That(purged, Is.EqualTo(2));
        var remaining = _subject.All().ToList();
        Assert.That(remaining, Has.Count.EqualTo(2));
        Assert.That(remaining.Any(r => r.Guid == "recent-1"), Is.True);
        Assert.That(remaining.Any(r => r.Guid == "recent-2"), Is.True);
    }
}
