using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private TorrentRepository _subject;

    [SetUp]
    public void SetUp()
    {
        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""Torrents"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""Name"" TEXT NOT NULL,
                ""Category"" TEXT NULL,
                ""InfoHash"" TEXT NULL,
                ""InfoHashV2"" TEXT NULL
            );
            CREATE TABLE ""PeerConnectionLogs"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""InfoHash"" TEXT NULL
            );
            CREATE TABLE ""TorrentMediaMetadata"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TorrentId"" INTEGER NOT NULL
            );
            CREATE TABLE ""TorrentFiles"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TorrentId"" INTEGER NOT NULL
            );
            CREATE TABLE ""TrackerEntries"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TorrentId"" INTEGER NOT NULL
            );
            CREATE TABLE ""TorrentEventLogs"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TorrentId"" INTEGER NOT NULL
            );";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new TorrentRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection.Close();
        _keepAliveConnection.Dispose();
    }

    private void InsertTorrent(string name, string category, string infoHash = null, string infoHashV2 = null)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"INSERT INTO ""Torrents"" (""Name"", ""Category"", ""InfoHash"", ""InfoHashV2"") VALUES (@Name, @Category, @InfoHash, @InfoHashV2)",
            new { Name = name, Category = category, InfoHash = infoHash, InfoHashV2 = infoHashV2 });
    }

    private string GetTorrentCategory(string name)
    {
        using var connection = _database.OpenConnection();
        return connection.QuerySingleOrDefault<string>(
            @"SELECT ""Category"" FROM ""Torrents"" WHERE ""Name"" = @Name",
            new { Name = name });
    }

    [Test]
    public void UpdateCategoryName_updates_matching_torrents_case_insensitively_and_trimmed()
    {
        InsertTorrent("Torrent 1", "Movies");
        InsertTorrent("Torrent 2", "movies");
        InsertTorrent("Torrent 3", "TV");

        _subject.UpdateCategoryName("  MOVIES  ", "  Films  ");

        Assert.That(GetTorrentCategory("Torrent 1"), Is.EqualTo("Films"));
        Assert.That(GetTorrentCategory("Torrent 2"), Is.EqualTo("Films"));
        Assert.That(GetTorrentCategory("Torrent 3"), Is.EqualTo("TV"));
    }

    [TestCase(null, "NewCat")]
    [TestCase("", "NewCat")]
    [TestCase("   ", "NewCat")]
    [TestCase("OldCat", null)]
    [TestCase("OldCat", "")]
    [TestCase("OldCat", "   ")]
    public void UpdateCategoryName_when_inputs_are_null_or_whitespace_does_nothing(string oldName, string newName)
    {
        InsertTorrent("Torrent 1", "OldCat");

        _subject.UpdateCategoryName(oldName, newName);

        Assert.That(GetTorrentCategory("Torrent 1"), Is.EqualTo("OldCat"));
    }

    [Test]
    public void ClearCategory_clears_matching_torrents_case_insensitively_and_trimmed()
    {
        InsertTorrent("Torrent 1", "Anime");
        InsertTorrent("Torrent 2", "anime");
        InsertTorrent("Torrent 3", "Movies");

        _subject.ClearCategory("  ANIME  ");

        Assert.That(GetTorrentCategory("Torrent 1"), Is.EqualTo(string.Empty));
        Assert.That(GetTorrentCategory("Torrent 2"), Is.EqualTo(string.Empty));
        Assert.That(GetTorrentCategory("Torrent 3"), Is.EqualTo("Movies"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ClearCategory_when_name_is_null_or_whitespace_does_nothing(string name)
    {
        InsertTorrent("Torrent 1", "Anime");

        _subject.ClearCategory(name);

        Assert.That(GetTorrentCategory("Torrent 1"), Is.EqualTo("Anime"));
    }

    [Test]
    public void ExistsByInfoHash_returns_true_when_hash_exists()
    {
        InsertTorrent("Torrent 1", "Movies", "hash123");

        Assert.That(_subject.ExistsByInfoHash("hash123"), Is.True);
        Assert.That(_subject.ExistsByInfoHash("nonexistent"), Is.False);
    }

    [Test]
    public void ExistsByInfoHash_matches_case_insensitively_and_trimmed()
    {
        InsertTorrent("Torrent 1", "Movies", "a1b2c3d4e5f6");

        Assert.That(_subject.ExistsByInfoHash("A1B2C3D4E5F6"), Is.True);
        Assert.That(_subject.ExistsByInfoHash("  a1b2c3d4e5f6  "), Is.True);
        Assert.That(_subject.ExistsByInfoHash("  A1B2c3D4e5F6  "), Is.True);
    }

    [Test]
    public void GetByInfoHash_matches_case_insensitively_and_trimmed()
    {
        InsertTorrent("Torrent 1", "Movies", "a1b2c3d4e5f6");

        var result = _subject.GetByInfoHash("  A1B2C3D4E5F6  ");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("Torrent 1"));
    }

    [Test]
    public void GetByInfoHash_finds_hybrid_torrent_by_v2_hash_when_v1_differs()
    {
        const string v1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string v2 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        InsertTorrent("Hybrid", "Movies", v1, v2);

        var result = _subject.GetByInfoHash(v2);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("Hybrid"));
        Assert.That(result.InfoHashV2, Is.EqualTo(v2));
    }

    [Test]
    public void ExistsByInfoHash_returns_true_when_only_InfoHashV2_matches()
    {
        const string v1 = "cccccccccccccccccccccccccccccccccccccccc";
        const string v2 = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        InsertTorrent("Hybrid", "TV", v1, v2);

        Assert.That(_subject.ExistsByInfoHash(v2), Is.True);
        Assert.That(_subject.ExistsByInfoHash(v1), Is.True);
    }

    [Test]
    public void GetByInfoHashes_includes_torrents_matched_by_InfoHashV2()
    {
        const string v1 = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        const string v2 = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        InsertTorrent("Hybrid", "Anime", v1, v2);

        var results = _subject.GetByInfoHashes(new[] { v2 });

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Name, Is.EqualTo("Hybrid"));
    }

    [Test]
    public void GetByInfoHashes_matches_case_insensitively_and_trimmed()
    {
        InsertTorrent("Torrent 1", "Movies", "a1b2c3d4e5f6");
        InsertTorrent("Torrent 2", "TV", "f6e5d4c3b2a1");

        var results = _subject.GetByInfoHashes(new[] { "  A1B2C3D4E5F6  ", "F6E5D4C3B2A1" });

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.Select(r => r.Name), Is.EquivalentTo(new[] { "Torrent 1", "Torrent 2" }));
    }

    [Test]
    public void GetByInfoHashes_returns_empty_when_input_is_null_or_empty()
    {
        Assert.That(_subject.GetByInfoHashes(null), Is.Empty);
        Assert.That(_subject.GetByInfoHashes(new List<string>()), Is.Empty);
        Assert.That(_subject.GetByInfoHashes(new[] { "   ", "" }), Is.Empty);
    }

    [Test]
    public void Delete_cascades_deletion_to_TorrentMediaMetadata_and_child_tables()
    {
        using (var connection = _database.OpenConnection())
        {
            connection.Execute(
                @"INSERT INTO ""Torrents"" (""Id"", ""Name"", ""InfoHash"") VALUES (1, 'Torrent 1', 'hash1')");
            connection.Execute(
                @"INSERT INTO ""Torrents"" (""Id"", ""Name"", ""InfoHash"") VALUES (2, 'Torrent 2', 'hash2')");

            connection.Execute(
                @"INSERT INTO ""TorrentMediaMetadata"" (""TorrentId"") VALUES (1), (2)");
            connection.Execute(
                @"INSERT INTO ""PeerConnectionLogs"" (""InfoHash"") VALUES ('hash1'), ('hash2')");
            connection.Execute(
                @"INSERT INTO ""TorrentFiles"" (""TorrentId"") VALUES (1), (2)");
            connection.Execute(
                @"INSERT INTO ""TrackerEntries"" (""TorrentId"") VALUES (1), (2)");
            connection.Execute(
                @"INSERT INTO ""TorrentEventLogs"" (""TorrentId"") VALUES (1), (2)");
        }

        _subject.Delete(1);

        using (var connection = _database.OpenConnection())
        {
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""Torrents"" WHERE ""Id"" = 1"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""Torrents"" WHERE ""Id"" = 2"), Is.EqualTo(1));

            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentMediaMetadata"" WHERE ""TorrentId"" = 1"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentMediaMetadata"" WHERE ""TorrentId"" = 2"), Is.EqualTo(1));

            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""PeerConnectionLogs"" WHERE ""InfoHash"" = 'hash1'"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""PeerConnectionLogs"" WHERE ""InfoHash"" = 'hash2'"), Is.EqualTo(1));

            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentFiles"" WHERE ""TorrentId"" = 1"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentFiles"" WHERE ""TorrentId"" = 2"), Is.EqualTo(1));

            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TrackerEntries"" WHERE ""TorrentId"" = 1"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TrackerEntries"" WHERE ""TorrentId"" = 2"), Is.EqualTo(1));

            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentEventLogs"" WHERE ""TorrentId"" = 1"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentEventLogs"" WHERE ""TorrentId"" = 2"), Is.EqualTo(1));
        }
    }

    [Test]
    public void DeleteMany_deletes_all_torrents_and_child_records_in_single_transaction()
    {
        using (var connection = _database.OpenConnection())
        {
            connection.Execute(
                @"INSERT INTO ""Torrents"" (""Id"", ""Name"", ""InfoHash"") VALUES (1, 'Torrent 1', 'hash1')");
            connection.Execute(
                @"INSERT INTO ""Torrents"" (""Id"", ""Name"", ""InfoHash"") VALUES (2, 'Torrent 2', 'hash2')");
            connection.Execute(
                @"INSERT INTO ""Torrents"" (""Id"", ""Name"", ""InfoHash"") VALUES (3, 'Torrent 3', 'hash3')");

            connection.Execute(
                @"INSERT INTO ""TorrentMediaMetadata"" (""TorrentId"") VALUES (1), (2), (3)");
            connection.Execute(
                @"INSERT INTO ""PeerConnectionLogs"" (""InfoHash"") VALUES ('hash1'), ('hash2'), ('hash3')");
            connection.Execute(
                @"INSERT INTO ""TorrentFiles"" (""TorrentId"") VALUES (1), (2), (3)");
            connection.Execute(
                @"INSERT INTO ""TrackerEntries"" (""TorrentId"") VALUES (1), (2), (3)");
            connection.Execute(
                @"INSERT INTO ""TorrentEventLogs"" (""TorrentId"") VALUES (1), (2), (3)");
        }

        _subject.DeleteMany(new List<int> { 1, 2 });

        using (var connection = _database.OpenConnection())
        {
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""Torrents"" WHERE ""Id"" IN (1, 2)"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""Torrents"" WHERE ""Id"" = 3"), Is.EqualTo(1));

            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentMediaMetadata"" WHERE ""TorrentId"" IN (1, 2)"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentMediaMetadata"" WHERE ""TorrentId"" = 3"), Is.EqualTo(1));

            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""PeerConnectionLogs"" WHERE ""InfoHash"" IN ('hash1', 'hash2')"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""PeerConnectionLogs"" WHERE ""InfoHash"" = 'hash3'"), Is.EqualTo(1));

            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentFiles"" WHERE ""TorrentId"" IN (1, 2)"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentFiles"" WHERE ""TorrentId"" = 3"), Is.EqualTo(1));

            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TrackerEntries"" WHERE ""TorrentId"" IN (1, 2)"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TrackerEntries"" WHERE ""TorrentId"" = 3"), Is.EqualTo(1));

            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentEventLogs"" WHERE ""TorrentId"" IN (1, 2)"), Is.EqualTo(0));
            Assert.That(connection.ExecuteScalar<int>(@"SELECT COUNT(1) FROM ""TorrentEventLogs"" WHERE ""TorrentId"" = 3"), Is.EqualTo(1));
        }
    }

    [Test]
    public void DeleteMany_handles_null_or_empty_list_without_error()
    {
        Assert.DoesNotThrow(() => _subject.DeleteMany(null));
        Assert.DoesNotThrow(() => _subject.DeleteMany(new List<int>()));
    }
}
