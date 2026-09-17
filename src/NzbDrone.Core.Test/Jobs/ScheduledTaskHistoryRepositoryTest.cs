using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Test.Jobs;

[TestFixture]
public class ScheduledTaskHistoryRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private ScheduledTaskHistoryRepository _subject;

    [SetUp]
    public void SetUp()
    {
        TableMapping.Register<ScheduledTaskHistory>("ScheduledTaskHistory");

        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""ScheduledTaskHistory"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TaskId"" INTEGER NOT NULL,
                ""TypeName"" TEXT NOT NULL,
                ""StartedAt"" TEXT NOT NULL,
                ""FinishedAt"" TEXT NOT NULL,
                ""DurationMs"" INTEGER NOT NULL,
                ""Status"" INTEGER NOT NULL DEFAULT 0,
                ""TriggerSource"" INTEGER NOT NULL DEFAULT 0,
                ""ErrorMessage"" TEXT,
                ""ExceptionDetails"" TEXT
            );";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new ScheduledTaskHistoryRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection?.Close();
        _keepAliveConnection?.Dispose();
    }

    [Test]
    public void GetByTaskId_should_return_records_in_descending_order()
    {
        var now = DateTime.UtcNow;
        var h1 = new ScheduledTaskHistory
        {
            TaskId = 1,
            TypeName = "NzbDrone.Core.Jobs.TaskA",
            StartedAt = now.AddMinutes(-20),
            FinishedAt = now.AddMinutes(-19),
            DurationMs = 60000,
            Status = ScheduledTaskHistoryStatus.Success,
            TriggerSource = ScheduledTaskTriggerSource.Scheduler
        };
        var h2 = new ScheduledTaskHistory
        {
            TaskId = 1,
            TypeName = "NzbDrone.Core.Jobs.TaskA",
            StartedAt = now.AddMinutes(-5),
            FinishedAt = now.AddMinutes(-4),
            DurationMs = 60000,
            Status = ScheduledTaskHistoryStatus.Failed,
            TriggerSource = ScheduledTaskTriggerSource.Manual,
            ErrorMessage = "Failed"
        };
        var hOther = new ScheduledTaskHistory
        {
            TaskId = 2,
            TypeName = "NzbDrone.Core.Jobs.TaskB",
            StartedAt = now.AddMinutes(-1),
            FinishedAt = now,
            DurationMs = 60000,
            Status = ScheduledTaskHistoryStatus.Success
        };

        _subject.Insert(h1);
        _subject.Insert(h2);
        _subject.Insert(hOther);

        var results = _subject.GetByTaskId(1, 10);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].StartedAt, Is.GreaterThan(results[1].StartedAt));
        Assert.That(results[0].Status, Is.EqualTo(ScheduledTaskHistoryStatus.Failed));
        Assert.That(results[1].Status, Is.EqualTo(ScheduledTaskHistoryStatus.Success));
    }

    [Test]
    public void GetByTypeName_should_match_full_name_and_short_name()
    {
        var now = DateTime.UtcNow;
        var full = new ScheduledTaskHistory
        {
            TaskId = 5,
            TypeName = "NzbDrone.Core.Housekeeping.CleanDatabase",
            StartedAt = now.AddMinutes(-10),
            FinishedAt = now.AddMinutes(-9),
            DurationMs = 60000,
            Status = ScheduledTaskHistoryStatus.Success
        };
        _subject.Insert(full);

        var byFullName = _subject.GetByTypeName("NzbDrone.Core.Housekeeping.CleanDatabase", 10);
        var byShortName = _subject.GetByTypeName("CleanDatabase", 10);

        Assert.That(byFullName, Has.Count.EqualTo(1));
        Assert.That(byShortName, Has.Count.EqualTo(1));
        Assert.That(byShortName[0].TypeName, Is.EqualTo("NzbDrone.Core.Housekeeping.CleanDatabase"));
    }

    [Test]
    public void PurgeOldHistory_should_purge_records_older_than_specified_cutoff_and_enforce_retention()
    {
        var now = DateTime.UtcNow;
        var old = new ScheduledTaskHistory
        {
            TaskId = 1,
            TypeName = "TaskA",
            StartedAt = now.AddDays(-40),
            FinishedAt = now.AddDays(-40),
            DurationMs = 100,
            Status = ScheduledTaskHistoryStatus.Success
        };
        var recent1 = new ScheduledTaskHistory
        {
            TaskId = 1,
            TypeName = "TaskA",
            StartedAt = now.AddMinutes(-30),
            FinishedAt = now.AddMinutes(-29),
            DurationMs = 100,
            Status = ScheduledTaskHistoryStatus.Success
        };
        var recent2 = new ScheduledTaskHistory
        {
            TaskId = 1,
            TypeName = "TaskA",
            StartedAt = now.AddMinutes(-10),
            FinishedAt = now.AddMinutes(-9),
            DurationMs = 100,
            Status = ScheduledTaskHistoryStatus.Success
        };

        _subject.Insert(old);
        _subject.Insert(recent1);
        _subject.Insert(recent2);

        // Keep at most 1 per type, older than 30 days deleted
        _subject.PurgeOldHistory(1, now.AddDays(-30));

        var remaining = _subject.GetByTypeName("TaskA", 10);
        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(remaining[0].StartedAt, Is.EqualTo(recent2.StartedAt));
    }
}
