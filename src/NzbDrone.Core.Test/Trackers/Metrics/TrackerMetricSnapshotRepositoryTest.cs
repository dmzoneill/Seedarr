using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Trackers.Metrics;

namespace NzbDrone.Core.Test.Trackers.Metrics;

[TestFixture]
public class TrackerMetricSnapshotRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private TrackerMetricSnapshotRepository _subject;

    [SetUp]
    public void SetUp()
    {
        TableMapping.Register<TrackerMetricSnapshot>("TrackerMetricSnapshots");

        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""TrackerMetricSnapshots"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TrackerMetricId"" INTEGER NOT NULL,
                ""TrackerUrl"" TEXT NOT NULL,
                ""Timestamp"" TEXT NOT NULL,
                ""ResponseTimeMs"" INTEGER NOT NULL DEFAULT 0,
                ""Uploaded"" INTEGER NOT NULL DEFAULT 0,
                ""Downloaded"" INTEGER NOT NULL DEFAULT 0,
                ""Seeders"" INTEGER NOT NULL DEFAULT 0,
                ""Leechers"" INTEGER NOT NULL DEFAULT 0,
                ""PeersDiscovered"" INTEGER NOT NULL DEFAULT 0,
                ""IsSuccess"" INTEGER NOT NULL DEFAULT 1,
                ""Operation"" TEXT NOT NULL DEFAULT 'Announce'
            );";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new TrackerMetricSnapshotRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection?.Close();
        _keepAliveConnection?.Dispose();
    }

    [Test]
    public void PruneOlderThan_should_remove_snapshots_before_cutoff()
    {
        var now = DateTime.UtcNow;
        var oldSnapshot = new TrackerMetricSnapshot
        {
            TrackerMetricId = 1,
            TrackerUrl = "http://tracker.example/announce",
            Timestamp = now.AddDays(-10),
            Operation = "Announce",
            IsSuccess = true
        };
        var recentSnapshot = new TrackerMetricSnapshot
        {
            TrackerMetricId = 1,
            TrackerUrl = "http://tracker.example/announce",
            Timestamp = now.AddDays(-2),
            Operation = "Announce",
            IsSuccess = true
        };

        _subject.InsertMany(new List<TrackerMetricSnapshot> { oldSnapshot, recentSnapshot });

        _subject.PruneOlderThan(now.AddDays(-7));

        var remaining = _subject.GetRecentSnapshots(now.AddDays(-30));
        Assert.That(remaining.Count, Is.EqualTo(1));
        Assert.That(remaining[0].Timestamp.Day, Is.EqualTo(recentSnapshot.Timestamp.Day));
    }

    [Test]
    public void GetHourlyAggregatedMetrics_should_aggregate_metrics_by_hour()
    {
        var baseTime = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        var snapshots = new List<TrackerMetricSnapshot>
        {
            new()
            {
                TrackerMetricId = 1,
                TrackerUrl = "http://tracker.example/announce",
                Timestamp = baseTime.AddMinutes(5),
                ResponseTimeMs = 100,
                Uploaded = 1000,
                Downloaded = 500,
                PeersDiscovered = 10,
                Operation = "Announce",
                IsSuccess = true
            },
            new()
            {
                TrackerMetricId = 1,
                TrackerUrl = "http://tracker.example/announce",
                Timestamp = baseTime.AddMinutes(25),
                ResponseTimeMs = 200,
                Uploaded = 2000,
                Downloaded = 1000,
                PeersDiscovered = 20,
                Operation = "Announce",
                IsSuccess = true
            },
            new()
            {
                TrackerMetricId = 1,
                TrackerUrl = "http://tracker.example/announce",
                Timestamp = baseTime.AddMinutes(40),
                ResponseTimeMs = 50,
                Uploaded = 0,
                Downloaded = 0,
                PeersDiscovered = 0,
                Operation = "Scrape",
                IsSuccess = true
            },
            new()
            {
                TrackerMetricId = 1,
                TrackerUrl = "http://tracker.example/announce",
                Timestamp = baseTime.AddHours(1).AddMinutes(15),
                ResponseTimeMs = 150,
                Uploaded = 5000,
                Downloaded = 2500,
                PeersDiscovered = 50,
                Operation = "Announce",
                IsSuccess = true
            }
        };

        _subject.InsertMany(snapshots);

        var result = _subject.GetHourlyAggregatedMetrics(baseTime.AddHours(-1));

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));

        var firstHour = result[0];
        Assert.That(firstHour.Uploaded, Is.EqualTo(3000));
        Assert.That(firstHour.Downloaded, Is.EqualTo(1500));
        Assert.That(firstHour.Announces, Is.EqualTo(2));
        Assert.That(firstHour.PeersDiscovered, Is.EqualTo(30));
        Assert.That(firstHour.AvgLatencyMs, Is.EqualTo(116.666).Within(0.01));

        var secondHour = result[1];
        Assert.That(secondHour.Uploaded, Is.EqualTo(5000));
        Assert.That(secondHour.Downloaded, Is.EqualTo(2500));
        Assert.That(secondHour.Announces, Is.EqualTo(1));
        Assert.That(secondHour.PeersDiscovered, Is.EqualTo(50));
        Assert.That(secondHour.AvgLatencyMs, Is.EqualTo(150.0));
    }
}
