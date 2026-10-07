using System;
using System.IO;
using System.Linq;
using Dapper;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.RemotePathMappings;

[TestFixture]
public class RemotePathMappingUniqueIndexMigrationTest
{
    private string _tempDbPath;

    [SetUp]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"seedarr_rpm_migration_{Guid.NewGuid():N}.db");
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
    public void DbFactory_Create_adds_unique_index_on_host_and_remote_path()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var indexes = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='index'").ToList();

        Assert.That(indexes, Does.Contain("IX_RemotePathMappings_Host_RemotePath"));
    }
}
