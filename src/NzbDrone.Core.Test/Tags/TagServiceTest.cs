using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Tags;

[TestFixture]
public class TagServiceTest
{
    private ITagRepository _repo;
    private IEventAggregator _eventAggregator;
    private TagService _subject;

    [SetUp]
    public void SetUp()
    {
        _repo = Substitute.For<ITagRepository>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _subject = new TagService(_repo, _eventAggregator);
    }

    [Test]
    public void GetAll_should_return_all_tags()
    {
        _repo.All().Returns(new List<Tag>
        {
            new() { Id = 1, Label = "Action" },
            new() { Id = 2, Label = "Comedy" }
        });

        var result = _subject.GetAll();

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetAll_should_return_empty_list_when_no_tags()
    {
        _repo.All().Returns(new List<Tag>());

        var result = _subject.GetAll();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Get_should_return_tag_by_id()
    {
        var tag = new Tag { Id = 1, Label = "Drama" };
        _repo.Get(1).Returns(tag);

        var result = _subject.Get(1);

        Assert.That(result.Label, Is.EqualTo("Drama"));
    }

    [Test]
    public void Add_should_insert_and_return_result()
    {
        var tag = new Tag { Label = "NewTag" };
        var inserted = new Tag { Id = 1, Label = "NewTag" };
        _repo.Insert(tag).Returns(inserted);

        var result = _subject.Add(tag);

        Assert.That(result.Id, Is.EqualTo(1));
    }

    [Test]
    public void Add_should_publish_model_event()
    {
        var tag = new Tag { Label = "NewTag" };
        _repo.Insert(tag).Returns(tag);

        _subject.Add(tag);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Tag>>(e => e.Action == ModelAction.Created));
    }

    [Test]
    public void Update_should_call_repo_update()
    {
        var tag = new Tag { Id = 1, Label = "Updated" };

        _subject.Update(tag);

        _repo.Received(1).Update(tag);
    }

    [Test]
    public void Update_should_publish_model_event()
    {
        var tag = new Tag { Id = 1, Label = "Updated" };

        _subject.Update(tag);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Tag>>(e => e.Action == ModelAction.Updated));
    }

    [Test]
    public void Update_should_return_the_same_tag()
    {
        var tag = new Tag { Id = 1, Label = "Updated" };
        _repo.Update(tag).Returns(tag);

        var result = _subject.Update(tag);

        Assert.That(result, Is.SameAs(tag));
    }

    [Test]
    public void Delete_should_call_repo_delete()
    {
        _subject.Delete(5);

        _repo.Received(1).Delete(5);
    }

    [Test]
    public void Delete_should_publish_model_event_when_tag_exists()
    {
        var tag = new Tag { Id = 5, Label = "ToDelete" };
        _repo.Get(5).Returns(tag);

        _subject.Delete(5);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Tag>>(e => e.Action == ModelAction.Deleted));
    }

    [Test]
    public void Delete_should_clean_tags_from_all_repositories_using_UpdateMany()
    {
        var torrentRepo = Substitute.For<ITorrentRepository>();
        var notifRepo = Substitute.For<INotificationRepository>();
        var indexerRepo = Substitute.For<IIndexerRepository>();
        var dlClientRepo = Substitute.For<IDownloadClientRepository>();
        var arrRepo = Substitute.For<IArrConnectionRepository>();
        var scriptRepo = Substitute.For<IAutomationScriptRepository>();

        torrentRepo.All().Returns(new List<Torrent>
        {
            new() { Id = 1, TagIds = new List<int> { 5, 10 } },
            new() { Id = 2, TagIds = new List<int> { 10 } }
        });

        notifRepo.All().Returns(new List<NotificationDefinition>
        {
            new() { Id = 1, Tags = new List<int> { 5, 20 } }
        });

        indexerRepo.All().Returns(new List<IndexerDefinition>
        {
            new() { Id = 1, Tags = new List<int> { 5 } }
        });

        dlClientRepo.All().Returns(new List<DownloadClientDefinition>
        {
            new() { Id = 1, Tags = new List<int> { 5, 30 } }
        });

        arrRepo.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Id = 1, Tags = new List<int> { 5 } }
        });

        scriptRepo.All().Returns(new List<AutomationScript>
        {
            new() { Id = 1, TargetTagIds = new List<int> { 5, 40 } }
        });

        var subject = new TagService(
            _repo,
            _eventAggregator,
            torrentRepo,
            notifRepo,
            indexerRepo,
            dlClientRepo,
            arrRepo,
            scriptRepo,
            database: null);

        subject.Delete(5);

        torrentRepo.Received(1).UpdateMany(Arg.Is<IEnumerable<Torrent>>(items =>
            items.Count() == 1 &&
            items.First().Id == 1 &&
            !items.First().TagIds.Contains(5) &&
            items.First().TagIds.Contains(10)));

        notifRepo.Received(1).UpdateMany(Arg.Is<IEnumerable<NotificationDefinition>>(items =>
            items.Count() == 1 &&
            items.First().Id == 1 &&
            !items.First().Tags.Contains(5) &&
            items.First().Tags.Contains(20)));

        indexerRepo.Received(1).UpdateMany(Arg.Is<IEnumerable<IndexerDefinition>>(items =>
            items.Count() == 1 &&
            items.First().Id == 1 &&
            !items.First().Tags.Contains(5)));

        dlClientRepo.Received(1).UpdateMany(Arg.Is<IEnumerable<DownloadClientDefinition>>(items =>
            items.Count() == 1 &&
            items.First().Id == 1 &&
            !items.First().Tags.Contains(5) &&
            items.First().Tags.Contains(30)));

        arrRepo.Received(1).UpdateMany(Arg.Is<IEnumerable<ArrConnectionDefinition>>(items =>
            items.Count() == 1 &&
            items.First().Id == 1 &&
            !items.First().Tags.Contains(5)));

        scriptRepo.Received(1).UpdateMany(Arg.Is<IEnumerable<AutomationScript>>(items =>
            items.Count() == 1 &&
            items.First().Id == 1 &&
            !items.First().TargetTagIds.Contains(5) &&
            items.First().TargetTagIds.Contains(40)));

        _repo.Received(1).Delete(5);
    }

    [Test]
    public void Delete_should_not_call_UpdateMany_when_no_entities_contain_tag()
    {
        var torrentRepo = Substitute.For<ITorrentRepository>();
        var notifRepo = Substitute.For<INotificationRepository>();
        var indexerRepo = Substitute.For<IIndexerRepository>();
        var dlClientRepo = Substitute.For<IDownloadClientRepository>();
        var arrRepo = Substitute.For<IArrConnectionRepository>();
        var scriptRepo = Substitute.For<IAutomationScriptRepository>();

        torrentRepo.All().Returns(new List<Torrent> { new() { Id = 1, TagIds = new List<int> { 10 } } });
        notifRepo.All().Returns(new List<NotificationDefinition> { new() { Id = 1, Tags = new List<int> { 20 } } });
        indexerRepo.All().Returns(new List<IndexerDefinition> { new() { Id = 1, Tags = new List<int> { 30 } } });
        dlClientRepo.All().Returns(new List<DownloadClientDefinition> { new() { Id = 1, Tags = new List<int> { 40 } } });
        arrRepo.All().Returns(new List<ArrConnectionDefinition> { new() { Id = 1, Tags = new List<int> { 50 } } });
        scriptRepo.All().Returns(new List<AutomationScript> { new() { Id = 1, TargetTagIds = new List<int> { 60 } } });

        var subject = new TagService(
            _repo,
            _eventAggregator,
            torrentRepo,
            notifRepo,
            indexerRepo,
            dlClientRepo,
            arrRepo,
            scriptRepo,
            database: null);

        subject.Delete(5);

        torrentRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<Torrent>>());
        notifRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<NotificationDefinition>>());
        indexerRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<IndexerDefinition>>());
        dlClientRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<DownloadClientDefinition>>());
        arrRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<ArrConnectionDefinition>>());
        scriptRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<AutomationScript>>());
        _repo.Received(1).Delete(5);
    }

    [Test]
    public void Delete_with_database_should_execute_atomic_cascading_updates_in_transaction()
    {
        var dbName = $"tag_test_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        keepAlive.Open();

        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE ""Tags"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT, ""Label"" TEXT NOT NULL);
                CREATE TABLE ""Torrents"" (""Id"" INTEGER PRIMARY KEY, ""TagIds"" TEXT);
                CREATE TABLE ""NotificationDefinitions"" (""Id"" INTEGER PRIMARY KEY, ""Tags"" TEXT);
                CREATE TABLE ""IndexerDefinitions"" (""Id"" INTEGER PRIMARY KEY, ""Tags"" TEXT);
                CREATE TABLE ""DownloadClientDefinitions"" (""Id"" INTEGER PRIMARY KEY, ""Tags"" TEXT);
                CREATE TABLE ""ArrConnectionDefinitions"" (""Id"" INTEGER PRIMARY KEY, ""Tags"" TEXT);
                CREATE TABLE ""AutomationScripts"" (""Id"" INTEGER PRIMARY KEY, ""TargetTagIds"" TEXT);

                INSERT INTO ""Tags"" (""Id"", ""Label"") VALUES (42, 'VPN');
                INSERT INTO ""Tags"" (""Id"", ""Label"") VALUES (99, 'Keep');

                INSERT INTO ""Torrents"" (""Id"", ""TagIds"") VALUES (1, '[42,99]');
                INSERT INTO ""Torrents"" (""Id"", ""TagIds"") VALUES (2, '[42]');
                INSERT INTO ""Torrents"" (""Id"", ""TagIds"") VALUES (3, '[99]');

                INSERT INTO ""NotificationDefinitions"" (""Id"", ""Tags"") VALUES (1, '[42,10]');
                INSERT INTO ""IndexerDefinitions"" (""Id"", ""Tags"") VALUES (1, '[42]');
                INSERT INTO ""DownloadClientDefinitions"" (""Id"", ""Tags"") VALUES (1, '[42,20]');
                INSERT INTO ""ArrConnectionDefinitions"" (""Id"", ""Tags"") VALUES (1, '[42]');
                INSERT INTO ""AutomationScripts"" (""Id"", ""TargetTagIds"") VALUES (1, '[42,30]');
            ";
            cmd.ExecuteNonQuery();
        }

        var database = new Database(() => new SqliteConnection(connStr), DatabaseType.SQLite);
        var tagRepo = new TagRepository(database);

        var subject = new TagService(
            tagRepo,
            _eventAggregator,
            database: database);

        subject.Delete(42);

        using (var verifyConn = new SqliteConnection(connStr))
        {
            verifyConn.Open();

            var tagCount = verifyConn.ExecuteScalar<int>("SELECT COUNT(1) FROM \"Tags\" WHERE \"Id\" = 42");
            Assert.That(tagCount, Is.EqualTo(0));

            var keepTagCount = verifyConn.ExecuteScalar<int>("SELECT COUNT(1) FROM \"Tags\" WHERE \"Id\" = 99");
            Assert.That(keepTagCount, Is.EqualTo(1));

            var torrent1Tags = verifyConn.ExecuteScalar<string>("SELECT \"TagIds\" FROM \"Torrents\" WHERE \"Id\" = 1");
            Assert.That(torrent1Tags, Is.EqualTo("[99]"));

            var torrent2Tags = verifyConn.ExecuteScalar<string>("SELECT \"TagIds\" FROM \"Torrents\" WHERE \"Id\" = 2");
            Assert.That(torrent2Tags, Is.EqualTo("[]"));

            var torrent3Tags = verifyConn.ExecuteScalar<string>("SELECT \"TagIds\" FROM \"Torrents\" WHERE \"Id\" = 3");
            Assert.That(torrent3Tags, Is.EqualTo("[99]"));

            var notifTags = verifyConn.ExecuteScalar<string>("SELECT \"Tags\" FROM \"NotificationDefinitions\" WHERE \"Id\" = 1");
            Assert.That(notifTags, Is.EqualTo("[10]"));

            var indexerTags = verifyConn.ExecuteScalar<string>("SELECT \"Tags\" FROM \"IndexerDefinitions\" WHERE \"Id\" = 1");
            Assert.That(indexerTags, Is.EqualTo("[]"));

            var dlClientTags = verifyConn.ExecuteScalar<string>("SELECT \"Tags\" FROM \"DownloadClientDefinitions\" WHERE \"Id\" = 1");
            Assert.That(dlClientTags, Is.EqualTo("[20]"));

            var arrTags = verifyConn.ExecuteScalar<string>("SELECT \"Tags\" FROM \"ArrConnectionDefinitions\" WHERE \"Id\" = 1");
            Assert.That(arrTags, Is.EqualTo("[]"));

            var scriptTags = verifyConn.ExecuteScalar<string>("SELECT \"TargetTagIds\" FROM \"AutomationScripts\" WHERE \"Id\" = 1");
            Assert.That(scriptTags, Is.EqualTo("[30]"));
        }

        _eventAggregator.Received(1).PublishEvent(Arg.Is<ModelEvent<Tag>>(e => e.Action == ModelAction.Deleted && e.Model.Id == 42));
    }

    [Test]
    public void Delete_with_database_should_rollback_transaction_on_error()
    {
        var dbName = $"tag_test_rollback_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        using var keepAlive = new SqliteConnection(connStr);
        keepAlive.Open();

        using (var cmd = keepAlive.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE ""Tags"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT, ""Label"" TEXT NOT NULL);
                CREATE TABLE ""Torrents"" (""Id"" INTEGER PRIMARY KEY, ""TagIds"" TEXT);

                INSERT INTO ""Tags"" (""Id"", ""Label"") VALUES (42, 'VPN');
                INSERT INTO ""Torrents"" (""Id"", ""TagIds"") VALUES (1, '[42,99]');

                CREATE TRIGGER fail_delete BEFORE DELETE ON ""Tags""
                BEGIN
                    SELECT RAISE(FAIL, 'Simulated failure in trigger');
                END;
            ";
            cmd.ExecuteNonQuery();
        }

        var database = new Database(() => new SqliteConnection(connStr), DatabaseType.SQLite);
        var tagRepo = new TagRepository(database);

        var subject = new TagService(
            tagRepo,
            _eventAggregator,
            database: database);

        Assert.Throws<SqliteException>(() => subject.Delete(42));

        using (var verifyConn = new SqliteConnection(connStr))
        {
            verifyConn.Open();

            var tagCount = verifyConn.ExecuteScalar<int>("SELECT COUNT(1) FROM \"Tags\" WHERE \"Id\" = 42");
            Assert.That(tagCount, Is.EqualTo(1));

            var torrentTags = verifyConn.ExecuteScalar<string>("SELECT \"TagIds\" FROM \"Torrents\" WHERE \"Id\" = 1");
            Assert.That(torrentTags, Is.EqualTo("[42,99]"));
        }

        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ModelEvent<Tag>>());
    }
}
