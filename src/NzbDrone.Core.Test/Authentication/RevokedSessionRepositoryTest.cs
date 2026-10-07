using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class RevokedSessionRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private RevokedSessionRepository _subject;

    [SetUp]
    public void SetUp()
    {
        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""RevokedSessions"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""SessionKey"" TEXT NOT NULL,
                ""RevokedAtUtc"" TEXT NOT NULL,
                ""ExpiresAtUtc"" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX ""IX_RevokedSessions_SessionKey"" ON ""RevokedSessions"" (""SessionKey"");";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new RevokedSessionRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection.Close();
        _keepAliveConnection.Dispose();
    }

    [Test]
    public void Upsert_inserts_when_no_row_exists()
    {
        var revokedAt = DateTime.UtcNow;
        var expiresAt = revokedAt.AddDays(30);

        _subject.Upsert("session-a", revokedAt, expiresAt);

        var active = _subject.GetActive(DateTime.UtcNow.AddYears(1)).ToList();
        Assert.That(active, Has.Count.EqualTo(1));
        Assert.That(active[0].SessionKey, Is.EqualTo("session-a"));
        Assert.That(active[0].RevokedAtUtc, Is.EqualTo(revokedAt).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Upsert_updates_only_when_newer_revocation()
    {
        var first = DateTime.UtcNow;
        var second = first.AddHours(1);
        var expiresFirst = first.AddDays(30);
        var expiresSecond = second.AddDays(30);

        _subject.Upsert("session-b", first, expiresFirst);
        _subject.Upsert("session-b", second, expiresSecond);

        var row = _subject.GetActive(DateTime.UtcNow.AddYears(1)).Single();
        Assert.That(row.RevokedAtUtc, Is.EqualTo(second).Within(TimeSpan.FromSeconds(1)));
        Assert.That(row.ExpiresAtUtc, Is.EqualTo(expiresSecond).Within(TimeSpan.FromSeconds(1)));

        var older = first.AddMinutes(-30);
        _subject.Upsert("session-b", older, older.AddDays(30));

        row = _subject.GetActive(DateTime.UtcNow.AddYears(1)).Single();
        Assert.That(row.RevokedAtUtc, Is.EqualTo(second).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Upsert_concurrent_inserts_same_session_key_do_not_throw()
    {
        var revokedAt = DateTime.UtcNow;
        var expiresAt = revokedAt.AddDays(30);
        var errors = 0;

        Parallel.For(0, 16, _ =>
        {
            try
            {
                _subject.Upsert("session-race", revokedAt, expiresAt);
            }
            catch
            {
                System.Threading.Interlocked.Increment(ref errors);
            }
        });

        Assert.That(errors, Is.EqualTo(0));
        var rows = _subject.GetActive(DateTime.UtcNow.AddYears(1)).ToList();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].SessionKey, Is.EqualTo("session-race"));
    }

    [Test]
    public void Upsert_ignores_blank_session_key()
    {
        _subject.Upsert("  ", DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        Assert.That(_subject.GetActive(DateTime.UtcNow.AddYears(1)), Is.Empty);
    }

    [Test]
    public void DeleteExpired_removes_rows_past_expires_at()
    {
        var revokedAt = DateTime.UtcNow.AddDays(-10);
        _subject.Upsert("session-expired", revokedAt, DateTime.UtcNow.AddMinutes(-1));
        _subject.Upsert("session-active", revokedAt, DateTime.UtcNow.AddDays(1));

        _subject.DeleteExpired(DateTime.UtcNow);

        Assert.That(_subject.GetActiveBySessionKey("session-expired", DateTime.UtcNow), Is.Null);
        Assert.That(_subject.GetActiveBySessionKey("session-active", DateTime.UtcNow), Is.Not.Null);
    }

    [Test]
    public void Upsert_case_variant_session_keys_collapses_to_one_row()
    {
        var revokedAt = DateTime.UtcNow;
        var expiresAt = revokedAt.AddDays(30);

        _subject.Upsert("admin", revokedAt, expiresAt);
        _subject.Upsert("Admin", revokedAt.AddMinutes(5), expiresAt);

        var rows = _subject.GetActive(DateTime.UtcNow.AddYears(1)).ToList();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].SessionKey, Is.EqualTo("admin"));
        Assert.That(rows[0].RevokedAtUtc, Is.EqualTo(revokedAt.AddMinutes(5)).Within(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void GetActiveBySessionKey_is_case_insensitive()
    {
        var revokedAt = DateTime.UtcNow;
        _subject.Upsert("User-A", revokedAt, revokedAt.AddDays(30));

        var active = _subject.GetActiveBySessionKey("user-a", DateTime.UtcNow);
        Assert.That(active, Is.Not.Null);
        Assert.That(active.SessionKey, Is.EqualTo("user-a"));
    }

    [Test]
    public void GetActiveBySessionKey_returns_active_row_only()
    {
        var revokedAt = DateTime.UtcNow.AddHours(-1);
        var expiresAt = revokedAt.AddDays(30);
        _subject.Upsert("session-lookup", revokedAt, expiresAt);

        var active = _subject.GetActiveBySessionKey("session-lookup", DateTime.UtcNow);
        Assert.That(active, Is.Not.Null);
        Assert.That(active.SessionKey, Is.EqualTo("session-lookup"));

        _subject.Upsert("session-expired", revokedAt.AddDays(-40), revokedAt.AddDays(-10));
        Assert.That(_subject.GetActiveBySessionKey("session-expired", DateTime.UtcNow), Is.Null);
        Assert.That(_subject.GetActiveBySessionKey("  ", DateTime.UtcNow), Is.Null);
    }
}
