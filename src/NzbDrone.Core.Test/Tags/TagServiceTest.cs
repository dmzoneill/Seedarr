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
using NzbDrone.Core.Exceptions;
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
    public void Add_should_reject_duplicate_tag_labels_case_insensitively()
    {
        _repo.All().Returns(new List<Tag>
        {
            new() { Id = 1, Label = "Action" }
        });

        Assert.Throws<DuplicateTagException>(() => _subject.Add(new Tag { Label = "action" }));
        Assert.Throws<DuplicateTagException>(() => _subject.Add(new Tag { Label = "ACTION" }));
        Assert.Throws<DuplicateTagException>(() => _subject.Add(new Tag { Label = "  Action  " }));
        _repo.DidNotReceive().Insert(Arg.Any<Tag>());
    }

    [Test]
    public void Add_should_throw_when_tag_label_is_empty_or_whitespace()
    {
        Assert.Throws<ArgumentException>(() => _subject.Add(new Tag { Label = "" }));
        Assert.Throws<ArgumentException>(() => _subject.Add(new Tag { Label = "   " }));
        Assert.Throws<ArgumentNullException>(() => _subject.Add(null));
        _repo.DidNotReceive().Insert(Arg.Any<Tag>());
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
    public void Update_should_reject_duplicate_tag_labels_case_insensitively()
    {
        _repo.All().Returns(new List<Tag>
        {
            new() { Id = 1, Label = "Action" },
            new() { Id = 2, Label = "Comedy" }
        });

        Assert.Throws<DuplicateTagException>(() => _subject.Update(new Tag { Id = 2, Label = "action" }));
        Assert.Throws<DuplicateTagException>(() => _subject.Update(new Tag { Id = 2, Label = "ACTION" }));
        Assert.Throws<DuplicateTagException>(() => _subject.Update(new Tag { Id = 2, Label = "  Action  " }));
        _repo.DidNotReceive().Update(Arg.Is<Tag>(t => t.Id == 2));
    }

    [Test]
    public void Update_should_allow_same_tag_to_keep_or_change_case_of_its_own_label()
    {
        var tag = new Tag { Id = 1, Label = "action" };
        _repo.All().Returns(new List<Tag>
        {
            new() { Id = 1, Label = "Action" },
            new() { Id = 2, Label = "Comedy" }
        });
        _repo.Update(tag).Returns(tag);

        var result = _subject.Update(tag);

        Assert.That(result.Label, Is.EqualTo("action"));
        _repo.Received(1).Update(tag);
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
    public void Delete_should_clean_tags_from_all_repositories_using_targeted_update_and_scrub_label()
    {
        var torrentRepo = Substitute.For<ITorrentRepository>();
        var notifRepo = Substitute.For<INotificationRepository>();
        var indexerRepo = Substitute.For<IIndexerRepository>();
        var dlClientRepo = Substitute.For<IDownloadClientRepository>();
        var arrRepo = Substitute.For<IArrConnectionRepository>();
        var scriptRepo = Substitute.For<IAutomationScriptRepository>();

        _repo.Get(5).Returns(new Tag { Id = 5, Label = "VPN" });

        torrentRepo.All().Returns(new List<Torrent>
        {
            new() { Id = 1, TagIds = new List<int> { 5, 10 }, Label = "VPN, Anime" },
            new() { Id = 2, TagIds = new List<int> { 10 }, Label = "Anime" }
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

        torrentRepo.Received(1).UpdateTagsAndLabels(Arg.Is<IEnumerable<Torrent>>(items =>
            items.Count() == 1 &&
            items.First().Id == 1 &&
            !items.First().TagIds.Contains(5) &&
            items.First().TagIds.Contains(10) &&
            items.First().Label == "Anime"));

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
    public void Delete_should_remove_tag_id_and_scrub_label_using_torrent_service()
    {
        var torrentService = Substitute.For<ITorrentService>();
        _repo.Get(5).Returns(new Tag { Id = 5, Label = "VPN" });

        torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { Id = 1, TagIds = new List<int> { 5, 10 }, Label = "Movies, VPN, Anime" }
        });

        var subject = new TagService(
            _repo,
            _eventAggregator,
            torrentService: torrentService,
            database: null);

        subject.Delete(5);

        torrentService.Received(1).UpdateUserFields(Arg.Is<Torrent>(t =>
            t.Id == 1 &&
            !t.TagIds.Contains(5) &&
            t.TagIds.Contains(10) &&
            t.Label == "Movies, Anime"));
        _repo.Received(1).Delete(5);
    }

    [Test]
    public void Delete_should_not_call_Update_when_no_entities_contain_tag()
    {
        var torrentRepo = Substitute.For<ITorrentRepository>();
        var notifRepo = Substitute.For<INotificationRepository>();
        var indexerRepo = Substitute.For<IIndexerRepository>();
        var dlClientRepo = Substitute.For<IDownloadClientRepository>();
        var arrRepo = Substitute.For<IArrConnectionRepository>();
        var scriptRepo = Substitute.For<IAutomationScriptRepository>();

        torrentRepo.All().Returns(new List<Torrent> { new() { Id = 1, TagIds = new List<int> { 10 }, Label = "Anime" } });
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

        torrentRepo.DidNotReceive().UpdateTagsAndLabels(Arg.Any<IEnumerable<Torrent>>());
        notifRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<NotificationDefinition>>());
        indexerRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<IndexerDefinition>>());
        dlClientRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<DownloadClientDefinition>>());
        arrRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<ArrConnectionDefinition>>());
        scriptRepo.DidNotReceive().UpdateMany(Arg.Any<IEnumerable<AutomationScript>>());
        _repo.Received(1).Delete(5);
    }

    [Test]
    public void Delete_with_null_repositories_should_log_warnings_and_not_throw()
    {
        _repo.Get(5).Returns(new Tag { Id = 5, Label = "VPN" });

        var subject = new TagService(
            _repo,
            _eventAggregator,
            database: null);

        Assert.DoesNotThrow(() => subject.Delete(5));
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
                CREATE TABLE ""Torrents"" (""Id"" INTEGER PRIMARY KEY, ""TagIds"" TEXT, ""Label"" TEXT);
                CREATE TABLE ""NotificationDefinitions"" (""Id"" INTEGER PRIMARY KEY, ""Tags"" TEXT);
                CREATE TABLE ""IndexerDefinitions"" (""Id"" INTEGER PRIMARY KEY, ""Tags"" TEXT);
                CREATE TABLE ""DownloadClientDefinitions"" (""Id"" INTEGER PRIMARY KEY, ""Tags"" TEXT);
                CREATE TABLE ""ArrConnectionDefinitions"" (""Id"" INTEGER PRIMARY KEY, ""Tags"" TEXT);
                CREATE TABLE ""AutomationScripts"" (""Id"" INTEGER PRIMARY KEY, ""TargetTagIds"" TEXT);

                INSERT INTO ""Tags"" (""Id"", ""Label"") VALUES (42, 'VPN');
                INSERT INTO ""Tags"" (""Id"", ""Label"") VALUES (99, 'Keep');

                INSERT INTO ""Torrents"" (""Id"", ""TagIds"", ""Label"") VALUES (1, '[42,99]', 'VPN, Anime');
                INSERT INTO ""Torrents"" (""Id"", ""TagIds"", ""Label"") VALUES (2, '[42]', 'VPN');
                INSERT INTO ""Torrents"" (""Id"", ""TagIds"", ""Label"") VALUES (3, '[99]', 'Keep');

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

            var torrent1Label = verifyConn.ExecuteScalar<string>("SELECT \"Label\" FROM \"Torrents\" WHERE \"Id\" = 1");
            Assert.That(torrent1Label, Is.EqualTo("Anime"));

            var torrent2Tags = verifyConn.ExecuteScalar<string>("SELECT \"TagIds\" FROM \"Torrents\" WHERE \"Id\" = 2");
            Assert.That(torrent2Tags, Is.EqualTo("[]"));

            var torrent2Label = verifyConn.ExecuteScalar<string>("SELECT \"Label\" FROM \"Torrents\" WHERE \"Id\" = 2");
            Assert.That(torrent2Label, Is.EqualTo(string.Empty));

            var torrent3Tags = verifyConn.ExecuteScalar<string>("SELECT \"TagIds\" FROM \"Torrents\" WHERE \"Id\" = 3");
            Assert.That(torrent3Tags, Is.EqualTo("[99]"));

            var torrent3Label = verifyConn.ExecuteScalar<string>("SELECT \"Label\" FROM \"Torrents\" WHERE \"Id\" = 3");
            Assert.That(torrent3Label, Is.EqualTo("Keep"));

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
                CREATE TABLE ""Torrents"" (""Id"" INTEGER PRIMARY KEY, ""TagIds"" TEXT, ""Label"" TEXT);

                INSERT INTO ""Tags"" (""Id"", ""Label"") VALUES (42, 'VPN');
                INSERT INTO ""Torrents"" (""Id"", ""TagIds"", ""Label"") VALUES (1, '[42,99]', 'VPN, Anime');

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

            var torrentLabel = verifyConn.ExecuteScalar<string>("SELECT \"Label\" FROM \"Torrents\" WHERE \"Id\" = 1");
            Assert.That(torrentLabel, Is.EqualTo("VPN, Anime"));
        }

        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<ModelEvent<Tag>>());
    }

    [Test]
    public void SyncTagsFromLabels_should_resolve_existing_tags_and_insert_missing_tags()
    {
        var existingTags = new List<Tag>
        {
            new() { Id = 1, Label = "Action" },
            new() { Id = 2, Label = "comedy" }
        };
        _repo.All().Returns(existingTags);

        var nextId = 10;
        _repo.Insert(Arg.Any<Tag>()).Returns(ci =>
        {
            var t = ci.Arg<Tag>();
            var newTag = new Tag { Id = nextId++, Label = t.Label };
            existingTags.Add(newTag);
            return newTag;
        });

        var result = _subject.SyncTagsFromLabels(new[] { "action", "COMEDY", "Drama", "Sci-Fi, Horror", "", null, "   " });

        Assert.That(result, Is.EqualTo(new List<int> { 1, 2, 10, 11, 12 }));
        _repo.Received(3).Insert(Arg.Any<Tag>());
    }

    [Test]
    public void SyncTagsFromLabels_should_return_empty_for_null_or_empty_input()
    {
        Assert.That(_subject.SyncTagsFromLabels(null), Is.Empty);
        Assert.That(_subject.SyncTagsFromLabels(new List<string>()), Is.Empty);
        Assert.That(_subject.SyncTagsFromLabels(new[] { "", "   ", null }), Is.Empty);
    }

    [Test]
    public void GetLabelsForTagIds_should_return_corresponding_labels_in_order()
    {
        _repo.All().Returns(new List<Tag>
        {
            new() { Id = 1, Label = "4K" },
            new() { Id = 2, Label = "Anime" },
            new() { Id = 3, Label = "HDR" }
        });

        var result = _subject.GetLabelsForTagIds(new[] { 3, 1 });

        Assert.That(result, Is.EqualTo(new List<string> { "HDR", "4K" }));
    }

    [Test]
    public void GetLabelsForTagIds_should_ignore_unknown_ids_and_return_empty_for_null()
    {
        _repo.All().Returns(new List<Tag>
        {
            new() { Id = 1, Label = "4K" }
        });

        Assert.That(_subject.GetLabelsForTagIds(null), Is.Empty);
        Assert.That(_subject.GetLabelsForTagIds(new[] { 999 }), Is.Empty);
    }
}
