using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Tags;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class BasicRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private BasicRepository<Tag> _subject;

    [SetUp]
    public void SetUp()
    {
        // Named in-memory DB with shared cache: multiple connections see the same data.
        // The keepalive connection holds the DB alive for the duration of the test.
        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""Tags"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""Label"" TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new BasicRepository<Tag>(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection.Close();
        _keepAliveConnection.Dispose();
    }

    [Test]
    public void All_returns_empty_when_no_records_exist()
    {
        var result = _subject.All();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void All_returns_all_inserted_records()
    {
        _subject.Insert(new Tag { Label = "Action" });
        _subject.Insert(new Tag { Label = "Comedy" });

        var result = _subject.All().ToList();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(t => t.Label), Is.EquivalentTo(new[] { "Action", "Comedy" }));
    }

    [Test]
    public void Get_returns_record_by_id()
    {
        var inserted = _subject.Insert(new Tag { Label = "Drama" });

        var result = _subject.Get(inserted.Id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Label, Is.EqualTo("Drama"));
        Assert.That(result.Id, Is.EqualTo(inserted.Id));
    }

    [Test]
    public void Get_returns_null_when_id_not_found()
    {
        var result = _subject.Get(9999);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Insert_persists_record_and_assigns_generated_id()
    {
        var tag = new Tag { Label = "Horror" };

        var result = _subject.Insert(tag);

        Assert.That(result.Id, Is.GreaterThan(0));
        Assert.That(result.Label, Is.EqualTo("Horror"));
    }

    [Test]
    public void Insert_assigns_sequential_ids()
    {
        var first = _subject.Insert(new Tag { Label = "First" });
        var second = _subject.Insert(new Tag { Label = "Second" });

        Assert.That(second.Id, Is.GreaterThan(first.Id));
    }

    [Test]
    public void Insert_returns_same_model_instance_with_id_set()
    {
        var tag = new Tag { Label = "SameInstance" };

        var result = _subject.Insert(tag);

        Assert.That(result, Is.SameAs(tag));
        Assert.That(tag.Id, Is.GreaterThan(0));
    }

    [Test]
    public void Update_modifies_existing_record()
    {
        var inserted = _subject.Insert(new Tag { Label = "Original" });
        inserted.Label = "Updated";

        _subject.Update(inserted);
        var result = _subject.Get(inserted.Id);

        Assert.That(result.Label, Is.EqualTo("Updated"));
    }

    [Test]
    public void Update_returns_the_same_model_instance()
    {
        var inserted = _subject.Insert(new Tag { Label = "Original" });
        inserted.Label = "Updated";

        var result = _subject.Update(inserted);

        Assert.That(result, Is.SameAs(inserted));
    }

    [Test]
    public void Delete_by_id_removes_record()
    {
        var inserted = _subject.Insert(new Tag { Label = "ToDelete" });

        _subject.Delete(inserted.Id);
        var result = _subject.Get(inserted.Id);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Delete_by_id_does_not_affect_other_records()
    {
        var keep = _subject.Insert(new Tag { Label = "Keep" });
        var remove = _subject.Insert(new Tag { Label = "Remove" });

        _subject.Delete(remove.Id);
        var result = _subject.Get(keep.Id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Label, Is.EqualTo("Keep"));
    }

    [Test]
    public void Delete_by_model_removes_record()
    {
        var inserted = _subject.Insert(new Tag { Label = "ToDeleteByModel" });

        _subject.Delete(inserted);
        var result = _subject.Get(inserted.Id);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Delete_by_model_delegates_to_delete_by_id()
    {
        var first = _subject.Insert(new Tag { Label = "First" });
        var second = _subject.Insert(new Tag { Label = "Second" });

        _subject.Delete(first);

        var allRemaining = _subject.All().ToList();
        Assert.That(allRemaining, Has.Count.EqualTo(1));
        Assert.That(allRemaining[0].Label, Is.EqualTo("Second"));
    }

    [Test]
    public void All_reflects_deletions()
    {
        _subject.Insert(new Tag { Label = "One" });
        var two = _subject.Insert(new Tag { Label = "Two" });
        _subject.Insert(new Tag { Label = "Three" });

        _subject.Delete(two.Id);

        var result = _subject.All().ToList();
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(t => t.Label), Is.EquivalentTo(new[] { "One", "Three" }));
    }
}
