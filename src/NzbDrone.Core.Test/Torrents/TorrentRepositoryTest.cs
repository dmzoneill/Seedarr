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
            )";
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
}
