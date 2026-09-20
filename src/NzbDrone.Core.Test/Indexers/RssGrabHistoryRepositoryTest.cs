using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class RssGrabHistoryRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private RssGrabHistoryRepository _subject;

    [SetUp]
    public void SetUp()
    {
        TableMapping.Register<RssGrabHistory>("RssGrabHistory");

        var dbName = $"testdb_grab_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""RssGrabHistory"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""ReleaseTitle"" TEXT NOT NULL,
                ""IndexerName"" TEXT,
                ""RuleId"" INTEGER,
                ""RuleName"" TEXT,
                ""InfoHash"" TEXT,
                ""Size"" INTEGER NOT NULL DEFAULT 0,
                ""GrabTimestamp"" TEXT NOT NULL,
                ""Status"" TEXT NOT NULL DEFAULT 'Grabbed',
                ""ErrorMessage"" TEXT
            );
            CREATE INDEX ""IX_RssGrabHistory_RuleId"" ON ""RssGrabHistory"" (""RuleId"");
            CREATE INDEX ""IX_RssGrabHistory_GrabTimestamp"" ON ""RssGrabHistory"" (""GrabTimestamp"");
            CREATE INDEX ""IX_RssGrabHistory_InfoHash"" ON ""RssGrabHistory"" (""InfoHash"");";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new RssGrabHistoryRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection?.Close();
        _keepAliveConnection?.Dispose();
    }

    [Test]
    public void Insert_and_GetHistory_should_return_persisted_records()
    {
        var record = new RssGrabHistory
        {
            ReleaseTitle = "Movie.2024.1080p.WEB-DL",
            IndexerName = "Prowlarr",
            RuleId = 1,
            RuleName = "Default Rule",
            InfoHash = "aabbccddeeff",
            Size = 1000000,
            GrabTimestamp = DateTime.UtcNow,
            Status = "Grabbed"
        };

        var inserted = _subject.Insert(record);
        Assert.That(inserted.Id, Is.GreaterThan(0));

        var history = _subject.GetHistory();
        Assert.That(history.Count, Is.EqualTo(1));
        Assert.That(history[0].ReleaseTitle, Is.EqualTo("Movie.2024.1080p.WEB-DL"));
        Assert.That(history[0].Status, Is.EqualTo("Grabbed"));
    }

    [Test]
    public void GetHistory_should_filter_by_ruleId_and_status()
    {
        _subject.Insert(new RssGrabHistory
        {
            ReleaseTitle = "Release 1",
            RuleId = 1,
            Status = "Grabbed",
            GrabTimestamp = DateTime.UtcNow
        });

        _subject.Insert(new RssGrabHistory
        {
            ReleaseTitle = "Release 2",
            RuleId = 2,
            Status = "Failed",
            ErrorMessage = "Download failed",
            GrabTimestamp = DateTime.UtcNow
        });

        var rule1Records = _subject.GetHistory(ruleId: 1);
        Assert.That(rule1Records.Count, Is.EqualTo(1));
        Assert.That(rule1Records[0].ReleaseTitle, Is.EqualTo("Release 1"));

        var failedRecords = _subject.GetHistory(status: "Failed");
        Assert.That(failedRecords.Count, Is.EqualTo(1));
        Assert.That(failedRecords[0].ReleaseTitle, Is.EqualTo("Release 2"));
    }

    [Test]
    public void GetCount_and_pagination_should_work_correctly()
    {
        for (var i = 1; i <= 5; i++)
        {
            _subject.Insert(new RssGrabHistory
            {
                ReleaseTitle = $"Release {i}",
                RuleId = 1,
                Status = "Grabbed",
                GrabTimestamp = DateTime.UtcNow.AddMinutes(i)
            });
        }

        Assert.That(_subject.GetCount(), Is.EqualTo(5));

        var paged = _subject.GetHistory(limit: 2, offset: 1);
        Assert.That(paged.Count, Is.EqualTo(2));
    }

    [Test]
    public void ClearHistory_should_remove_all_records()
    {
        _subject.Insert(new RssGrabHistory
        {
            ReleaseTitle = "Release 1",
            Status = "Grabbed",
            GrabTimestamp = DateTime.UtcNow
        });

        Assert.That(_subject.GetCount(), Is.EqualTo(1));
        _subject.ClearHistory();
        Assert.That(_subject.GetCount(), Is.EqualTo(0));
    }
}
