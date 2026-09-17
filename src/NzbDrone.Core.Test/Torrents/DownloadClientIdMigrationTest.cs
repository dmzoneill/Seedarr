using System;
using System.IO;
using System.Linq;
using Dapper;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class DownloadClientIdMigrationTest
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
    public void DbFactory_Create_runs_migration_and_adds_download_client_id_to_torrents()
    {
        var factory = new DbFactory();
        var db = factory.Create(DatabaseType.SQLite, $"Data Source={_tempDbPath}");

        using var conn = db.OpenConnection();
        var columns = conn.Query<string>("SELECT name FROM pragma_table_info('Torrents')").ToList();

        Assert.That(columns, Does.Contain("DownloadClientId"));
    }
}
