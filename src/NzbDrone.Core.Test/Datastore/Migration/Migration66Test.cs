using System;
using System.IO;
using System.Linq;
using Dapper;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore.Migration;

[TestFixture]
public class Migration66Test
{
    private string _tempDbPath;

    [SetUp]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"seedarr_migration_test_{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempDbPath))
        {
            File.Delete(_tempDbPath);
        }
    }

    [Test]
    public void DbFactory_Create_runs_migration_66_and_adds_indexes_cleanly()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var indexes = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='index'").ToList();

        Assert.That(indexes, Does.Contain("IX_Torrents_Status"));
        Assert.That(indexes, Does.Contain("IX_Torrents_SortOrder"));
        Assert.That(indexes, Does.Contain("IX_DownloadHistory_InfoHash"));
        Assert.That(indexes, Does.Contain("IX_DownloadHistory_DateAdded"));
        Assert.That(indexes, Does.Contain("IX_PeerConnectionLogs_InfoHash_Timestamp"));
        Assert.That(indexes, Does.Contain("IX_TrackerMetrics_TrackerUrl"));
        Assert.That(indexes, Does.Contain("IX_TorrentEventLogs_TorrentId_TimeStamp"));
    }

    [Test]
    public void DbFactory_Create_is_idempotent_and_does_not_fail_on_subsequent_runs()
    {
        var factory = new DbFactory();
        var db1 = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");
        var db2 = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db2.OpenConnection();
        var indexes = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='index'").ToList();

        Assert.That(indexes, Does.Contain("IX_Torrents_Status"));
        Assert.That(indexes, Does.Contain("IX_Torrents_SortOrder"));
        Assert.That(indexes, Does.Contain("IX_DownloadHistory_InfoHash"));
        Assert.That(indexes, Does.Contain("IX_DownloadHistory_DateAdded"));
        Assert.That(indexes, Does.Contain("IX_PeerConnectionLogs_InfoHash_Timestamp"));
        Assert.That(indexes, Does.Contain("IX_TrackerMetrics_TrackerUrl"));
        Assert.That(indexes, Does.Contain("IX_TorrentEventLogs_TorrentId_TimeStamp"));
    }
}
