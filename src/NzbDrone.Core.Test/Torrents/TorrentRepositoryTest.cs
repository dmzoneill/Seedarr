using System;
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
                ""InfoHash"" TEXT NULL
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

    private void InsertTorrent(string name, string category, string infoHash = null)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"INSERT INTO ""Torrents"" (""Name"", ""Category"", ""InfoHash"") VALUES (@Name, @Category, @InfoHash)",
            new { Name = name, Category = category, InfoHash = infoHash });
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
                @"INSERT INTO ""PeerConnectionLogs"" (""InfoHash"") VALUES ('hash1', 'hash2')");
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
}
