using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentRepositoryConcurrencyTest
{
    private string _connectionString;
    private SqliteConnection _keepAlive;
    private IDatabase _database;
    private TorrentRepository _repository;

    [SetUp]
    public void SetUp()
    {
        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE ""Torrents"" (
    ""Id"" INTEGER PRIMARY KEY,
    ""Status"" INTEGER NOT NULL,
    ""Uploaded"" INTEGER NOT NULL,
    ""Downloaded"" INTEGER NOT NULL,
    ""SeedingTime"" INTEGER NOT NULL,
    ""SessionUploaded"" INTEGER NOT NULL,
    ""SessionDownloaded"" INTEGER NOT NULL,
    ""Ratio"" REAL NOT NULL,
    ""TotalSize"" INTEGER NOT NULL,
    ""UploadSpeed"" INTEGER NOT NULL,
    ""DownloadSpeed"" INTEGER NOT NULL,
    ""Progress"" REAL NOT NULL,
    ""Eta"" INTEGER NOT NULL,
    ""LastActive"" TEXT NULL,
    ""Active"" INTEGER NOT NULL,
    ""Seeders"" INTEGER NOT NULL,
    ""Leechers"" INTEGER NOT NULL,
    ""Availability"" REAL NOT NULL,
    ""Threshold"" INTEGER NOT NULL,
    ""IsVpnPaused"" INTEGER NOT NULL,
    ""ForceStart"" INTEGER NOT NULL,
    ""SuperSeeding"" INTEGER NOT NULL,
    ""SortOrder"" INTEGER NOT NULL,
    ""Label"" TEXT NULL,
    ""Category"" TEXT NULL,
    ""Name"" TEXT NULL,
    ""Priority"" INTEGER NOT NULL
);";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _repository = new TorrentRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAlive.Dispose();
    }

    [Test]
    public void Runtime_write_does_not_roll_back_counters_or_clobber_a_pause_or_user_fields()
    {
        Insert(1, TorrentStatus.Seeding, uploaded: 1_000, label: "old", category: "old", priority: 1);

        var applied = _repository.UpdateRuntimeStats(new[]
        {
            Runtime(1, TorrentStatus.Seeding, uploaded: 5_000, seeders: 3)
        });
        Assert.That(applied, Is.EqualTo(1));

        _repository.UpdateExecutionStates(new[]
        {
            new TorrentExecutionWrite
            {
                Id = 1,
                Status = (int)TorrentStatus.Paused,
                Active = false,
                UploadSpeed = 0,
                DownloadSpeed = 0
            }
        });
        SetUserFields(1, "user-label", "movies", 9);

        var stale = _repository.UpdateRuntimeStats(new[]
        {
            Runtime(1, TorrentStatus.Seeding, uploaded: 1_500, seeders: 1)
        });
        Assert.That(stale, Is.EqualTo(0));

        var row = Read(1);
        Assert.That(row.Status, Is.EqualTo((int)TorrentStatus.Paused));
        Assert.That(row.Uploaded, Is.EqualTo(5_000));
        Assert.That(row.Label, Is.EqualTo("user-label"));
        Assert.That(row.Category, Is.EqualTo("movies"));
        Assert.That(row.Priority, Is.EqualTo(9));
        Assert.That(row.Active, Is.EqualTo(0));
    }

    [Test]
    public void Runtime_write_keeps_the_higher_counter_when_status_still_matches()
    {
        Insert(2, TorrentStatus.Seeding, uploaded: 80_000, label: "keep", category: "linux", priority: 2);

        var applied = _repository.UpdateRuntimeStats(new[]
        {
            Runtime(2, TorrentStatus.Seeding, uploaded: 6_000, seeders: 4)
        });

        Assert.That(applied, Is.EqualTo(1));
        var row = Read(2);
        Assert.That(row.Uploaded, Is.EqualTo(80_000));
        Assert.That(row.Seeders, Is.EqualTo(4));
        Assert.That(row.Label, Is.EqualTo("keep"));
        Assert.That(row.Status, Is.EqualTo((int)TorrentStatus.Seeding));
    }

    [Test]
    public void Swarm_and_execution_writes_leave_counters_alone()
    {
        Insert(3, TorrentStatus.Downloading, uploaded: 42_000, label: "stay", category: "tv", priority: 3);

        _repository.UpdateSwarmStats(3, 11, 6, 2.5);
        _repository.UpdateExecutionStates(new[]
        {
            new TorrentExecutionWrite
            {
                Id = 3,
                Status = (int)TorrentStatus.Paused,
                Active = false
            }
        });

        var row = Read(3);
        Assert.That(row.Uploaded, Is.EqualTo(42_000));
        Assert.That(row.Seeders, Is.EqualTo(11));
        Assert.That(row.Leechers, Is.EqualTo(6));
        Assert.That(row.Availability, Is.EqualTo(2.5).Within(0.001));
        Assert.That(row.Status, Is.EqualTo((int)TorrentStatus.Paused));
        Assert.That(row.Label, Is.EqualTo("stay"));
        Assert.That(row.Category, Is.EqualTo("tv"));
    }

    [Test]
    public void Concurrent_runtime_and_pause_writers_do_not_deadlock_or_decrease_counters()
    {
        const int torrentCount = 100;
        for (var id = 1; id <= torrentCount; id++)
        {
            Insert(id, TorrentStatus.Seeding, uploaded: 0, label: "batch", category: "iso", priority: 1);
        }

        var errors = new List<Exception>();
        var gate = new object();

        var writers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            try
            {
                for (var pass = 1; pass <= 20; pass++)
                {
                    var writes = new List<TorrentRuntimeWrite>(torrentCount);
                    for (var id = 1; id <= torrentCount; id++)
                    {
                        writes.Add(Runtime(id, TorrentStatus.Seeding, uploaded: (worker * 20L) + pass, seeders: pass));
                    }

                    _repository.UpdateRuntimeStats(writes);
                    if ((worker + pass) % 5 == 0)
                    {
                        _repository.UpdateSwarmStats((worker % torrentCount) + 1, pass, pass, pass);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    errors.Add(ex);
                }
            }
        })).ToArray();

        var pausers = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            try
            {
                for (var pass = 0; pass < 10; pass++)
                {
                    var id = ((worker * 17) + pass) % torrentCount + 1;
                    _repository.UpdateExecutionStates(new[]
                    {
                        new TorrentExecutionWrite
                        {
                            Id = id,
                            Status = pass % 2 == 0 ? (int)TorrentStatus.Paused : (int)TorrentStatus.Seeding,
                            Active = pass % 2 != 0
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    errors.Add(ex);
                }
            }
        })).ToArray();

        Task.WaitAll(writers.Concat(pausers).ToArray());

        Assert.That(errors, Is.Empty);
        for (var id = 1; id <= torrentCount; id++)
        {
            var row = Read(id);
            Assert.That(row.Uploaded, Is.GreaterThanOrEqualTo(0));
            Assert.That(row.Uploaded, Is.LessThanOrEqualTo(8 * 20));
            Assert.That(row.Label, Is.EqualTo("batch"));
            Assert.That(row.Category, Is.EqualTo("iso"));
            Assert.That(row.Priority, Is.EqualTo(1));
        }
    }

    private static TorrentRuntimeWrite Runtime(int id, TorrentStatus expected, long uploaded, int seeders)
    {
        return new TorrentRuntimeWrite
        {
            Id = id,
            ExpectedStatus = (int)expected,
            Uploaded = uploaded,
            Downloaded = 100,
            SessionUploaded = uploaded,
            SessionDownloaded = 100,
            UploadSpeed = 10,
            DownloadSpeed = 0,
            Progress = 1,
            Eta = 0,
            SeedingTime = 5,
            LastActive = DateTime.UtcNow,
            Active = true,
            Seeders = seeders,
            Leechers = 1,
            Availability = 1,
            Threshold = 100
        };
    }

    private void Insert(int id, TorrentStatus status, long uploaded, string label, string category, int priority)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"INSERT INTO ""Torrents"" (
                ""Id"", ""Status"", ""Uploaded"", ""Downloaded"", ""SeedingTime"", ""SessionUploaded"", ""SessionDownloaded"",
                ""Ratio"", ""TotalSize"", ""UploadSpeed"", ""DownloadSpeed"", ""Progress"", ""Eta"", ""LastActive"", ""Active"",
                ""Seeders"", ""Leechers"", ""Availability"", ""Threshold"", ""IsVpnPaused"", ""ForceStart"", ""SuperSeeding"",
                ""SortOrder"", ""Label"", ""Category"", ""Name"", ""Priority"")
              VALUES (
                @Id, @Status, @Uploaded, 100, 1, 0, 0,
                0, 1000, 0, 0, 1, 0, NULL, 1,
                0, 0, 1, 0, 0, 0, 0,
                @Id, @Label, @Category, @Name, @Priority)",
            new
            {
                Id = id,
                Status = (int)status,
                Uploaded = uploaded,
                Label = label,
                Category = category,
                Name = "t" + id,
                Priority = priority
            });
    }

    private Row Read(int id)
    {
        using var connection = _database.OpenConnection();
        return connection.QuerySingle<Row>(
            @"SELECT ""Status"", ""Uploaded"", ""Label"", ""Category"", ""Priority"", ""Active"", ""Seeders"", ""Leechers"", ""Availability""
              FROM ""Torrents"" WHERE ""Id"" = @Id",
            new { Id = id });
    }

    private void SetUserFields(int id, string label, string category, int priority)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            @"UPDATE ""Torrents"" SET ""Label"" = @Label, ""Category"" = @Category, ""Priority"" = @Priority WHERE ""Id"" = @Id",
            new { Id = id, Label = label, Category = category, Priority = priority });
    }

    private sealed class Row
    {
        public int Status { get; set; }

        public long Uploaded { get; set; }

        public string Label { get; set; }

        public string Category { get; set; }

        public int Priority { get; set; }

        public int Active { get; set; }

        public int Seeders { get; set; }

        public int Leechers { get; set; }

        public double Availability { get; set; }
    }
}
