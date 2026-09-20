using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentFileRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private TorrentFileRepository _subject;

    [SetUp]
    public void SetUp()
    {
        // TorrentFile -> type.Name + "s" = "TorrentFiles" which matches the migration table name
        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""TorrentFiles"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TorrentId"" INTEGER NOT NULL,
                ""Path"" TEXT NOT NULL,
                ""Size"" INTEGER NOT NULL DEFAULT 0,
                ""PieceOffset"" INTEGER NOT NULL DEFAULT 0,
                ""PieceCount"" INTEGER NOT NULL DEFAULT 0,
                ""IsPaddingFile"" INTEGER NOT NULL DEFAULT 0,
                ""Wanted"" INTEGER NOT NULL DEFAULT 1,
                ""Priority"" INTEGER NOT NULL DEFAULT 0,
                ""BytesCompleted"" INTEGER NOT NULL DEFAULT 0
            )";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new TorrentFileRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection.Close();
        _keepAliveConnection.Dispose();
    }

    private static TorrentFile MakeFile(int torrentId, string path, long size = 1024) =>
        new() { TorrentId = torrentId, Path = path, Size = size };

    [Test]
    public void GetByTorrentId_returns_empty_when_no_files_exist()
    {
        var result = _subject.GetByTorrentId(1);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetByTorrentId_returns_only_files_for_requested_torrent()
    {
        _subject.Insert(MakeFile(1, "file1.mkv"));
        _subject.Insert(MakeFile(2, "file2.mkv"));

        var result = _subject.GetByTorrentId(1);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].TorrentId, Is.EqualTo(1));
        Assert.That(result[0].Path, Is.EqualTo("file1.mkv"));
    }

    [Test]
    public void GetByTorrentId_returns_all_files_for_torrent()
    {
        _subject.Insert(MakeFile(1, "video.mkv", size: 734003200));
        _subject.Insert(MakeFile(1, "subs.srt", size: 12345));
        _subject.Insert(MakeFile(2, "other.mkv"));

        var result = _subject.GetByTorrentId(1);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(f => f.Path), Is.EquivalentTo(new[] { "video.mkv", "subs.srt" }));
    }

    [Test]
    public void GetByTorrentId_returns_files_ordered_by_path()
    {
        _subject.Insert(MakeFile(1, "z_video.mkv"));
        _subject.Insert(MakeFile(1, "a_audio.mp3"));
        _subject.Insert(MakeFile(1, "m_middle.txt"));

        var result = _subject.GetByTorrentId(1);

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Path, Is.EqualTo("a_audio.mp3"));
        Assert.That(result[1].Path, Is.EqualTo("m_middle.txt"));
        Assert.That(result[2].Path, Is.EqualTo("z_video.mkv"));
    }

    [Test]
    public void DeleteByTorrentId_removes_all_files_for_torrent()
    {
        _subject.Insert(MakeFile(1, "file1.mkv"));
        _subject.Insert(MakeFile(1, "file2.mkv"));

        _subject.DeleteByTorrentId(1);

        Assert.That(_subject.GetByTorrentId(1), Is.Empty);
    }

    [Test]
    public void DeleteByTorrentId_does_not_remove_files_for_other_torrents()
    {
        _subject.Insert(MakeFile(1, "file1.mkv"));
        _subject.Insert(MakeFile(2, "file2.mkv"));

        _subject.DeleteByTorrentId(1);
        var remaining = _subject.GetByTorrentId(2);

        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(remaining[0].TorrentId, Is.EqualTo(2));
    }

    [Test]
    public void DeleteByTorrentId_on_nonexistent_torrent_does_not_throw()
    {
        Assert.DoesNotThrow(() => _subject.DeleteByTorrentId(9999));
    }

    [Test]
    public void Insert_persists_all_file_fields()
    {
        var file = _subject.Insert(MakeFile(1, "big.mkv", size: 1073741824));

        var result = _subject.GetByTorrentId(1);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Size, Is.EqualTo(1073741824));
        Assert.That(result[0].Path, Is.EqualTo("big.mkv"));
        Assert.That(result[0].TorrentId, Is.EqualTo(1));
        Assert.That(result[0].IsPaddingFile, Is.False);
    }

    [Test]
    public void InsertMany_inserts_many_files_in_a_single_batch()
    {
        var files = Enumerable.Range(1, 500)
            .Select(i => MakeFile(1, $"path/to/file_{i:D4}.dat", size: i * 1024))
            .ToList();

        _subject.InsertMany(files);

        var retrieved = _subject.GetByTorrentId(1);
        Assert.That(retrieved, Has.Count.EqualTo(500));
        Assert.That(retrieved[0].Path, Is.EqualTo("path/to/file_0001.dat"));
        Assert.That(retrieved[499].Path, Is.EqualTo("path/to/file_0500.dat"));
    }

    [Test]
    public void InsertMany_rolls_back_atomically_when_error_occurs()
    {
        _subject.Insert(MakeFile(1, "existing.mkv"));

        var files = new[]
        {
            MakeFile(1, "batch1.mkv"),
            new TorrentFile { TorrentId = 1, Path = null } // Path NOT NULL constraint violation
        };

        Assert.Throws<SqliteException>(() => _subject.InsertMany(files));

        var retrieved = _subject.GetByTorrentId(1);
        Assert.That(retrieved, Has.Count.EqualTo(1));
        Assert.That(retrieved[0].Path, Is.EqualTo("existing.mkv"));
    }
}
