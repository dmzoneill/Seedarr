using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Categories;

[TestFixture]
public class CategoryServiceTest
{
    private ICategoryRepository _repository;
    private IEventAggregator _eventAggregator;
    private ITorrentRepository _torrentRepository;
    private IAutomationScriptRepository _automationScriptRepository;
    private CategoryService _subject;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<ICategoryRepository>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _torrentRepository = Substitute.For<ITorrentRepository>();
        _automationScriptRepository = Substitute.For<IAutomationScriptRepository>();
        _subject = new CategoryService(_repository, _eventAggregator, _torrentRepository, automationScriptRepository: _automationScriptRepository);
    }

    [Test]
    public void GetAll_returns_categories_ordered_by_name()
    {
        var catB = new Category { Id = 1, Name = "B" };
        var catA = new Category { Id = 2, Name = "A" };
        _repository.All().Returns(new[] { catB, catA });

        var result = _subject.GetAll();

        Assert.That(result, Is.EqualTo(new[] { catA, catB }));
    }

    [Test]
    public void Get_returns_category_by_id()
    {
        var cat = new Category { Id = 1, Name = "Movies" };
        _repository.Get(1).Returns(cat);

        var result = _subject.Get(1);

        Assert.That(result, Is.SameAs(cat));
    }

    [Test]
    public void GetByName_with_empty_name_returns_default()
    {
        var defaultCat = new Category { Id = 1, Name = "Default", IsDefault = true };
        _repository.GetDefault().Returns(defaultCat);

        var result = _subject.GetByName("");

        Assert.That(result, Is.SameAs(defaultCat));
        _repository.Received(1).GetDefault();
    }

    [Test]
    public void GetByName_with_name_returns_category()
    {
        var cat = new Category { Id = 2, Name = "TV" };
        _repository.GetByName("TV").Returns(cat);

        var result = _subject.GetByName("TV");

        Assert.That(result, Is.SameAs(cat));
        _repository.Received(1).GetByName("TV");
    }

    [Test]
    public void Add_null_category_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _subject.Add(null));
    }

    [Test]
    public void Add_non_default_category_inserts_and_does_not_call_set_exclusive_default()
    {
        var cat = new Category { Name = "Movies", IsDefault = false };
        _repository.Insert(cat).Returns(callInfo =>
        {
            var inserted = callInfo.Arg<Category>();
            inserted.Id = 10;
            return inserted;
        });

        var result = _subject.Add(cat);

        Assert.That(result.Id, Is.EqualTo(10));
        _repository.DidNotReceive().SetExclusiveDefault(Arg.Any<int>());
        _eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryUpdatedEvent>(e => e.Category == result));
    }

    [Test]
    public void Add_default_category_inserts_and_calls_set_exclusive_default_with_new_id()
    {
        var cat = new Category { Name = "DefaultCat", IsDefault = true };
        _repository.Insert(cat).Returns(callInfo =>
        {
            var inserted = callInfo.Arg<Category>();
            inserted.Id = 42;
            return inserted;
        });

        var result = _subject.Add(cat);

        Assert.That(result.Id, Is.EqualTo(42));
        _repository.Received(1).SetExclusiveDefault(42);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryUpdatedEvent>(e => e.Category == result));
    }

    [Test]
    public void Update_null_category_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _subject.Update(null));
    }

    [Test]
    public void Update_non_default_category_does_not_call_set_exclusive_default()
    {
        var cat = new Category { Id = 5, Name = "Movies", IsDefault = false };
        _repository.Get(5).Returns(new Category { Id = 5, Name = "Movies", IsDefault = false });
        _repository.Update(cat).Returns(cat);

        var result = _subject.Update(cat);

        Assert.That(result, Is.SameAs(cat));
        _repository.DidNotReceive().SetExclusiveDefault(Arg.Any<int>());
        _eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryUpdatedEvent>(e => e.Category == cat));
    }

    [Test]
    public void Update_default_category_calls_set_exclusive_default()
    {
        var cat = new Category { Id = 7, Name = "Movies", IsDefault = true };
        _repository.Get(7).Returns(new Category { Id = 7, Name = "Movies", IsDefault = false });
        _repository.Update(cat).Returns(cat);

        var result = _subject.Update(cat);

        Assert.That(result, Is.SameAs(cat));
        _repository.Received(1).SetExclusiveDefault(7);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryUpdatedEvent>(e => e.Category == cat));
    }

    [Test]
    public void Update_renamed_category_updates_associated_torrents_and_synchronizes_label()
    {
        var oldCat = new Category { Id = 1, Name = "OldName", IsDefault = false };
        var newCat = new Category { Id = 1, Name = "NewName", IsDefault = false };

        var torrent1 = new Torrent { Id = 1, Category = "OldName", Label = "OldName, 1080p" };
        var torrent2 = new Torrent { Id = 2, Category = "Other", Label = "Action; OldName" };
        var torrent3 = new Torrent { Id = 3, Category = "Unrelated", Label = "Comedy" };

        _repository.Get(1).Returns(oldCat);
        _repository.Update(newCat).Returns(newCat);
        _torrentRepository.All().Returns(new[] { torrent1, torrent2, torrent3 });

        _subject.Update(newCat);

        Assert.That(torrent1.Category, Is.EqualTo("NewName"));
        Assert.That(torrent1.Label, Is.EqualTo("NewName, 1080p"));
        Assert.That(torrent2.Category, Is.EqualTo("Other"));
        Assert.That(torrent2.Label, Is.EqualTo("Action; NewName"));
        Assert.That(torrent3.Category, Is.EqualTo("Unrelated"));
        Assert.That(torrent3.Label, Is.EqualTo("Comedy"));

        _torrentRepository.Received(1).Update(torrent1);
        _torrentRepository.Received(1).Update(torrent2);
        _torrentRepository.DidNotReceive().Update(torrent3);
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == torrent1));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == torrent2));
        _eventAggregator.Received().PublishEvent(Arg.Is<ModelEvent<Torrent>>(e => e.Model == torrent1 && e.Action == ModelAction.Updated));
    }

    [Test]
    public void Update_renamed_category_with_torrent_service_updates_via_torrent_service()
    {
        var oldCat = new Category { Id = 1, Name = "OldName", IsDefault = false };
        var newCat = new Category { Id = 1, Name = "NewName", IsDefault = false };
        var torrent = new Torrent { Id = 10, Category = "OldName", Label = "OldName" };

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var service = new CategoryService(_repository, _eventAggregator, _torrentRepository, new Lazy<ITorrentService>(() => torrentService));

        _repository.Get(1).Returns(oldCat);
        _repository.Update(newCat).Returns(newCat);

        service.Update(newCat);

        Assert.That(torrent.Category, Is.EqualTo("NewName"));
        Assert.That(torrent.Label, Is.EqualTo("NewName"));
        torrentService.Received(1).Update(torrent);
    }

    [Test]
    public void Update_when_category_name_unchanged_does_not_update_torrents()
    {
        var oldCat = new Category { Id = 1, Name = "SameName", IsDefault = false };
        var newCat = new Category { Id = 1, Name = "SameName", IsDefault = false };

        _repository.Get(1).Returns(oldCat);
        _repository.Update(newCat).Returns(newCat);

        _subject.Update(newCat);

        _torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public void Delete_when_category_does_not_exist_does_nothing()
    {
        _repository.Get(99).Returns((Category)null);

        Assert.DoesNotThrow(() => _subject.Delete(99));

        _repository.DidNotReceive().Delete(Arg.Any<int>());
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<CategoryDeletedEvent>());
    }

    [Test]
    public void Delete_when_category_is_default_throws_InvalidOperationException()
    {
        var defaultCat = new Category { Id = 3, Name = "Standard", IsDefault = true };
        _repository.Get(3).Returns(defaultCat);

        var ex = Assert.Throws<InvalidOperationException>(() => _subject.Delete(3));

        Assert.That(ex.Message, Does.Contain("Cannot delete category 'Standard' because it is configured as the default category"));
        _repository.DidNotReceive().Delete(Arg.Any<int>());
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<CategoryDeletedEvent>());
    }

    [Test]
    public void Delete_when_category_is_not_default_deletes_clears_torrent_categories_scrubs_label_and_publishes_events()
    {
        var cat = new Category { Id = 5, Name = "Anime", IsDefault = false };
        var torrent1 = new Torrent { Id = 1, Category = "Anime", Label = "Anime, 1080p" };
        var torrent2 = new Torrent { Id = 2, Category = "Other", Label = "Drama; Anime" };
        var torrent3 = new Torrent { Id = 3, Category = "Anime", Label = "Anime" };
        var torrent4 = new Torrent { Id = 4, Category = "Movies", Label = "Unrelated" };

        _repository.Get(5).Returns(cat);
        _torrentRepository.All().Returns(new[] { torrent1, torrent2, torrent3, torrent4 });

        _subject.Delete(5);

        Assert.That(torrent1.Category, Is.EqualTo(string.Empty));
        Assert.That(torrent1.Label, Is.EqualTo("1080p"));
        Assert.That(torrent2.Category, Is.EqualTo("Other"));
        Assert.That(torrent2.Label, Is.EqualTo("Drama"));
        Assert.That(torrent3.Category, Is.EqualTo(string.Empty));
        Assert.That(torrent3.Label, Is.EqualTo(string.Empty));
        Assert.That(torrent4.Category, Is.EqualTo("Movies"));
        Assert.That(torrent4.Label, Is.EqualTo("Unrelated"));

        _torrentRepository.Received(1).Update(torrent1);
        _torrentRepository.Received(1).Update(torrent2);
        _torrentRepository.Received(1).Update(torrent3);
        _torrentRepository.DidNotReceive().Update(torrent4);
        _repository.Received(1).Delete(5);

        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == torrent1));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == torrent2));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent == torrent3));
        _eventAggregator.Received().PublishEvent(Arg.Is<CategoryDeletedEvent>(e =>
            e.CategoryId == 5 &&
            e.CategoryName == "Anime" &&
            e.AffectedTorrentIds.Count == 3 &&
            e.AffectedTorrentIds.Contains(1) &&
            e.AffectedTorrentIds.Contains(2) &&
            e.AffectedTorrentIds.Contains(3)));
    }

    [Test]
    public void SynchronizeLabelOnRename_replaces_exact_and_delimited_labels()
    {
        Assert.That(CategoryService.SynchronizeLabelOnRename("Movies", "Movies", "Films"), Is.EqualTo("Films"));
        Assert.That(CategoryService.SynchronizeLabelOnRename("movies", "Movies", "Films"), Is.EqualTo("Films"));
        Assert.That(CategoryService.SynchronizeLabelOnRename("Movies, 1080p", "Movies", "Films"), Is.EqualTo("Films, 1080p"));
        Assert.That(CategoryService.SynchronizeLabelOnRename("1080p, Movies", "Movies", "Films"), Is.EqualTo("1080p, Films"));
        Assert.That(CategoryService.SynchronizeLabelOnRename("SciFi; Movies; Action", "Movies", "Films"), Is.EqualTo("SciFi; Films; Action"));
        Assert.That(CategoryService.SynchronizeLabelOnRename("Movies 1080p", "Movies", "Films"), Is.EqualTo("Movies 1080p"));
        Assert.That(CategoryService.SynchronizeLabelOnRename(null, "Movies", "Films"), Is.Null);
    }

    [Test]
    public void ScrubLabelOnDelete_removes_exact_and_delimited_labels()
    {
        Assert.That(CategoryService.ScrubLabelOnDelete("Anime", "Anime"), Is.EqualTo(string.Empty));
        Assert.That(CategoryService.ScrubLabelOnDelete("anime", "Anime"), Is.EqualTo(string.Empty));
        Assert.That(CategoryService.ScrubLabelOnDelete("Anime, 1080p", "Anime"), Is.EqualTo("1080p"));
        Assert.That(CategoryService.ScrubLabelOnDelete("1080p, Anime", "Anime"), Is.EqualTo("1080p"));
        Assert.That(CategoryService.ScrubLabelOnDelete("SciFi; Anime; Action", "Anime"), Is.EqualTo("SciFi; Action"));
        Assert.That(CategoryService.ScrubLabelOnDelete("Anime; Anime", "Anime"), Is.EqualTo(string.Empty));
        Assert.That(CategoryService.ScrubLabelOnDelete("Anime 1080p", "Anime"), Is.EqualTo("Anime 1080p"));
        Assert.That(CategoryService.ScrubLabelOnDelete(null, "Anime"), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Add_with_accessible_save_path_creates_directory_and_normalizes_trailing_slash()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_cat_test_" + Guid.NewGuid().ToString("N"));
        var cat = new Category { Name = "ValidPathCat", SavePath = tempDir + "//" };
        _repository.Insert(Arg.Any<Category>()).Returns(callInfo => callInfo.Arg<Category>());

        try
        {
            var result = _subject.Add(cat);
            Assert.That(result.SavePath, Is.EqualTo(tempDir));
            Assert.That(Directory.Exists(tempDir), Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void Add_with_inaccessible_save_path_throws_InvalidOperationException()
    {
        var inaccessiblePath = OperatingSystem.IsWindows()
            ? "Z:\\NonExistentDrive\\Inaccessible"
            : "/root/seedarr_inaccessible_test_dir_" + Guid.NewGuid().ToString("N");
        var cat = new Category { Name = "InaccessibleCat", SavePath = inaccessiblePath };

        var ex = Assert.Throws<InvalidOperationException>(() => _subject.Add(cat));
        Assert.That(ex.Message, Does.Contain("Cannot access or write to save path"));
        _repository.DidNotReceive().Insert(Arg.Any<Category>());
    }

    [Test]
    public void Update_with_accessible_save_path_normalizes_trailing_slash_and_verifies_access()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_cat_test_" + Guid.NewGuid().ToString("N"));
        var cat = new Category { Id = 1, Name = "Movies", SavePath = tempDir + "/" };
        _repository.Get(1).Returns(new Category { Id = 1, Name = "Movies", SavePath = "/old/path" });
        _repository.Update(Arg.Any<Category>()).Returns(callInfo => callInfo.Arg<Category>());

        try
        {
            var result = _subject.Update(cat);
            Assert.That(result.SavePath, Is.EqualTo(tempDir));
            Assert.That(Directory.Exists(tempDir), Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void Update_with_inaccessible_save_path_throws_InvalidOperationException()
    {
        var inaccessiblePath = OperatingSystem.IsWindows()
            ? "Z:\\NonExistentDrive\\Inaccessible"
            : "/root/seedarr_inaccessible_test_dir_" + Guid.NewGuid().ToString("N");
        var cat = new Category { Id = 1, Name = "Movies", SavePath = inaccessiblePath };
        _repository.Get(1).Returns(new Category { Id = 1, Name = "Movies", SavePath = "/old/path" });

        var ex = Assert.Throws<InvalidOperationException>(() => _subject.Update(cat));
        Assert.That(ex.Message, Does.Contain("Cannot access or write to save path"));
        _repository.DidNotReceive().Update(Arg.Any<Category>());
    }

    [Test]
    public void NormalizeSavePath_normalizes_trailing_slashes_and_handles_root()
    {
        Assert.That(CategoryService.NormalizeSavePath("/downloads/movies/"), Is.EqualTo("/downloads/movies"));
        Assert.That(CategoryService.NormalizeSavePath("/downloads/movies///"), Is.EqualTo("/downloads/movies"));
        Assert.That(CategoryService.NormalizeSavePath("/downloads//movies/"), Is.EqualTo("/downloads/movies"));
        Assert.That(CategoryService.NormalizeSavePath("C:\\downloads/movies"), Is.EqualTo("C:\\downloads\\movies"));
        Assert.That(CategoryService.NormalizeSavePath("/"), Is.EqualTo("/"));
        Assert.That(CategoryService.NormalizeSavePath(""), Is.EqualTo(string.Empty));
        Assert.That(CategoryService.NormalizeSavePath(null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void GetByName_caches_result_and_does_not_hit_repository_again()
    {
        var cat = new Category { Id = 10, Name = "TV" };
        _repository.GetByName("TV").Returns(cat);

        var first = _subject.GetByName("TV");
        var second = _subject.GetByName("TV");
        var third = _subject.GetByName("tv");

        Assert.That(first, Is.SameAs(cat));
        Assert.That(second, Is.SameAs(cat));
        Assert.That(third, Is.SameAs(cat));
        _repository.Received(1).GetByName("TV");
    }

    [Test]
    public void GetAll_caches_categories_and_does_not_query_repository_multiple_times()
    {
        var catA = new Category { Id = 1, Name = "Anime" };
        var catB = new Category { Id = 2, Name = "Movies" };
        _repository.All().Returns(new[] { catA, catB });

        var first = _subject.GetAll();
        var second = _subject.GetAll();

        Assert.That(first.Count(), Is.EqualTo(2));
        Assert.That(second.Count(), Is.EqualTo(2));
        _repository.Received(1).All();
    }

    [Test]
    public void Add_enforces_case_insensitive_name_uniqueness_and_throws_InvalidOperationException()
    {
        var existing = new Category { Id = 1, Name = "Movies" };
        _repository.All().Returns(new[] { existing });

        var duplicateCat = new Category { Name = "movies" };

        var ex = Assert.Throws<InvalidOperationException>(() => _subject.Add(duplicateCat));
        Assert.That(ex.Message, Does.Contain("already exists"));
        _repository.DidNotReceive().Insert(Arg.Any<Category>());
    }

    [Test]
    public void Add_with_relative_save_path_throws_ArgumentException()
    {
        var cat = new Category { Name = "RelativeCat", SavePath = "relative/downloads/path" };

        var ex = Assert.Throws<ArgumentException>(() => _subject.Add(cat));
        Assert.That(ex.Message, Does.Contain("Save path must be an absolute path"));
        _repository.DidNotReceive().Insert(Arg.Any<Category>());
    }

    [Test]
    public void Add_with_traversal_save_path_throws_ArgumentException()
    {
        var cat = new Category { Name = "TraversalCat", SavePath = "/downloads/../etc" };

        var ex = Assert.Throws<ArgumentException>(() => _subject.Add(cat));
        Assert.That(ex.Message, Does.Contain("Save path cannot contain directory traversal sequences"));
        _repository.DidNotReceive().Insert(Arg.Any<Category>());
    }

    [Test]
    public void Add_updates_in_memory_cache()
    {
        var cat = new Category { Name = "Documentaries", IsDefault = false };
        _repository.Insert(cat).Returns(callInfo =>
        {
            var c = callInfo.Arg<Category>();
            c.Id = 99;
            return c;
        });

        _subject.Add(cat);

        var fetched = _subject.GetByName("documentaries");
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched.Id, Is.EqualTo(99));
        _repository.DidNotReceive().GetByName(Arg.Any<string>());
    }

    [Test]
    public void Update_updates_in_memory_cache_and_removes_old_name()
    {
        var oldCat = new Category { Id = 5, Name = "OldName", IsDefault = false };
        var updatedCat = new Category { Id = 5, Name = "NewName", IsDefault = false };

        _repository.Get(5).Returns(oldCat);
        _repository.Update(updatedCat).Returns(updatedCat);

        _subject.Update(updatedCat);

        var byNewName = _subject.GetByName("newname");
        Assert.That(byNewName, Is.SameAs(updatedCat));

        _repository.GetByName("OldName").Returns((Category)null);
        var byOldName = _subject.GetByName("OldName");
        Assert.That(byOldName, Is.Null);
    }

    [Test]
    public void Delete_invalidates_in_memory_cache()
    {
        var cat = new Category { Id = 12, Name = "ToDelete", IsDefault = false };
        _repository.Get(12).Returns(cat);
        _repository.All().Returns(new[] { cat });

        _subject.Get(12);
        _subject.GetByName("ToDelete");

        _subject.Delete(12);

        _repository.Get(12).Returns((Category)null);
        _repository.GetByName("ToDelete").Returns((Category)null);

        Assert.That(_subject.Get(12), Is.Null);
        Assert.That(_subject.GetByName("ToDelete"), Is.Null);
    }

    [Test]
    public void ValidateSavePath_validates_rooted_and_sanitized_paths()
    {
        Assert.DoesNotThrow(() => CategoryService.ValidateSavePath("/data/downloads"));
        Assert.DoesNotThrow(() => CategoryService.ValidateSavePath("C:\\Downloads"));
        Assert.DoesNotThrow(() => CategoryService.ValidateSavePath(""));
        Assert.DoesNotThrow(() => CategoryService.ValidateSavePath(null));

        var relativeEx = Assert.Throws<ArgumentException>(() => CategoryService.ValidateSavePath("some/relative/path"));
        Assert.That(relativeEx.Message, Does.Contain("absolute"));

        var traversalEx = Assert.Throws<ArgumentException>(() => CategoryService.ValidateSavePath("/var/../etc"));
        Assert.That(traversalEx.Message, Does.Contain("traversal"));

        var nullByteEx = Assert.Throws<ArgumentException>(() => CategoryService.ValidateSavePath("/data/\0test"));
        Assert.That(nullByteEx.Message, Does.Contain("null"));
    }

    [Test]
    public void CanDownload_with_category_max_active_downloads_enforces_limit()
    {
        var category = new Category { Id = 1, Name = "Movies", MaxActiveDownloads = 2 };
        _repository.GetByName("Movies").Returns(category);
        _repository.All().Returns(new[] { category });

        var t1 = new Torrent { Id = 1, Category = "Movies" };
        var t2 = new Torrent { Id = 2, Category = "Movies" };
        var newTorrent = new Torrent { Id = 3, Category = "Movies" };

        var canDownloadWithTwoActive = _subject.CanDownload(newTorrent, new[] { t1, t2 });
        var canDownloadWithOneActive = _subject.CanDownload(newTorrent, new[] { t1 });

        Assert.That(canDownloadWithTwoActive, Is.False);
        Assert.That(canDownloadWithOneActive, Is.True);
    }

    [Test]
    public void CanUpload_with_category_max_active_uploads_enforces_limit()
    {
        var category = new Category { Id = 1, Name = "TV", MaxActiveUploads = 1 };
        _repository.GetByName("TV").Returns(category);
        _repository.All().Returns(new[] { category });

        var t1 = new Torrent { Id = 1, Category = "TV" };
        var newTorrent = new Torrent { Id = 2, Category = "TV" };

        var canUploadWithOneActive = _subject.CanUpload(newTorrent, new[] { t1 });
        var canUploadWithZeroActive = _subject.CanUpload(newTorrent, Array.Empty<Torrent>());

        Assert.That(canUploadWithOneActive, Is.False);
        Assert.That(canUploadWithZeroActive, Is.True);
    }

    [Test]
    public void EvaluateDownloadQueue_promotes_reserved_slots_first()
    {
        var catPriority = new Category { Id = 1, Name = "Priority", ReservedDownloadSlots = 2 };
        var catNormal = new Category { Id = 2, Name = "Normal", ReservedDownloadSlots = 0 };
        _repository.All().Returns(new[] { catPriority, catNormal });
        _repository.GetByName("Priority").Returns(catPriority);
        _repository.GetByName("Normal").Returns(catNormal);

        var normal1 = new Torrent { Id = 1, Category = "Normal", SortOrder = 1 };
        var priority1 = new Torrent { Id = 2, Category = "Priority", SortOrder = 2 };
        var priority2 = new Torrent { Id = 3, Category = "Priority", SortOrder = 3 };
        var normal2 = new Torrent { Id = 4, Category = "Normal", SortOrder = 4 };

        var queued = new[] { normal1, priority1, priority2, normal2 };

        // Global limit of 2; Priority has 2 reserved slots, so both priority torrents should be promoted before normal1
        var promoted = _subject.EvaluateDownloadQueue(queued, Array.Empty<Torrent>(), 2);

        Assert.That(promoted.Select(t => t.Id), Is.EqualTo(new[] { 2, 3 }));
    }

    [Test]
    public void EvaluateDownloadQueue_respects_category_max_active_downloads()
    {
        var cat = new Category { Id = 1, Name = "Capped", MaxActiveDownloads = 1 };
        _repository.All().Returns(new[] { cat });
        _repository.GetByName("Capped").Returns(cat);

        var t1 = new Torrent { Id = 1, Category = "Capped", SortOrder = 1 };
        var t2 = new Torrent { Id = 2, Category = "Capped", SortOrder = 2 };
        var t3 = new Torrent { Id = 3, Category = "Capped", SortOrder = 3 };

        var queued = new[] { t1, t2, t3 };
        var promoted = _subject.EvaluateDownloadQueue(queued, Array.Empty<Torrent>(), 10);

        Assert.That(promoted.Select(t => t.Id), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void EvaluateDownloadQueue_does_not_exceed_global_limit()
    {
        var cat = new Category { Id = 1, Name = "General" };
        _repository.All().Returns(new[] { cat });
        _repository.GetByName("General").Returns(cat);

        var torrents = Enumerable.Range(1, 5).Select(i => new Torrent { Id = i, Category = "General", SortOrder = i }).ToList();

        var promoted = _subject.EvaluateDownloadQueue(torrents, Array.Empty<Torrent>(), 2);

        Assert.That(promoted.Count, Is.EqualTo(2));
        Assert.That(promoted.Select(t => t.Id), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void GetSavePathForCategory_with_configured_category_save_path_returns_category_save_path()
    {
        var cat = new Category { Id = 1, Name = "Movies", SavePath = "/data/movies" };
        _repository.GetByName("Movies").Returns(cat);

        var result = _subject.GetSavePathForCategory("Movies", "/default/path");

        Assert.That(result, Is.EqualTo("/data/movies"));
    }

    [Test]
    public void GetSavePathForCategory_with_empty_category_save_path_falls_back_to_default_category_save_path()
    {
        var cat = new Category { Id = 1, Name = "Movies", SavePath = "" };
        var defaultCat = new Category { Id = 2, Name = "Default", SavePath = "/data/default", IsDefault = true };
        _repository.GetByName("Movies").Returns(cat);
        _repository.GetDefault().Returns(defaultCat);

        var result = _subject.GetSavePathForCategory("Movies", "/default/path");

        Assert.That(result, Is.EqualTo("/data/default"));
    }

    [Test]
    public void GetSavePathForCategory_with_no_category_and_no_default_save_path_returns_provided_default_path()
    {
        _repository.GetByName("NonExistent").Returns((Category)null);
        _repository.GetDefault().Returns((Category)null);

        var result = _subject.GetSavePathForCategory("NonExistent", "/default/path");

        Assert.That(result, Is.EqualTo("/default/path"));
    }

    [Test]
    public void CanDownload_bypasses_stalled_and_slow_downloading_torrents()
    {
        var category = new Category { Id = 1, Name = "Movies", MaxActiveDownloads = 1 };
        _repository.GetByName("Movies").Returns(category);
        _repository.All().Returns(new[] { category });

        var stalledDl = new Torrent { Id = 1, Category = "Movies", Status = TorrentStatus.Downloading, DownloadSpeed = 0 };
        var slowDl = new Torrent { Id = 2, Category = "Movies", Status = TorrentStatus.Downloading, DownloadSpeed = 5 * 1024 };
        var candidate = new Torrent { Id = 3, Category = "Movies" };

        // With ignoreSlowTorrents = true (default), stalled and slow active downloads do not block candidate
        Assert.That(_subject.CanDownload(candidate, new[] { stalledDl }, 1), Is.True);
        Assert.That(_subject.CanDownload(candidate, new[] { slowDl }, 1), Is.True);

        // With ignoreSlowTorrents = false, stalled and slow active downloads do block candidate
        Assert.That(_subject.CanDownload(candidate, new[] { stalledDl }, 1, ignoreSlowTorrents: false), Is.False);
        Assert.That(_subject.CanDownload(candidate, new[] { slowDl }, 1, ignoreSlowTorrents: false), Is.False);
    }

    [Test]
    public void EvaluateDownloadQueue_bypasses_slow_downloads_under_10kbps()
    {
        var cat = new Category { Id = 1, Name = "General" };
        _repository.All().Returns(new[] { cat });
        _repository.GetByName("General").Returns(cat);

        var slowActive = new Torrent { Id = 1, Category = "General", Status = TorrentStatus.Downloading, DownloadSpeed = 9 * 1024 };
        var queued = new Torrent { Id = 2, Category = "General", SortOrder = 1 };

        // Global limit is 1, active has 1 slow download (< 10 KB/s). Queued should be promoted.
        var promoted = _subject.EvaluateDownloadQueue(new[] { queued }, new[] { slowActive }, 1, ignoreSlowTorrents: true);

        Assert.That(promoted.Select(t => t.Id), Is.EqualTo(new[] { 2 }));

        // With ignoreSlowTorrents = false, slowActive consumes the slot.
        var notPromoted = _subject.EvaluateDownloadQueue(new[] { queued }, new[] { slowActive }, 1, ignoreSlowTorrents: false);

        Assert.That(notPromoted, Is.Empty);
    }

    [Test]
    public void Add_persists_target_ratio_target_seed_time_and_autostop()
    {
        var category = new Category
        {
            Name = "Anime",
            TargetRatio = 2.5,
            TargetSeedTimeMinutes = 120,
            AutoStop = true
        };

        _repository.Insert(category).Returns(category);

        var result = _subject.Add(category);

        Assert.That(result.TargetRatio, Is.EqualTo(2.5));
        Assert.That(result.TargetSeedTimeMinutes, Is.EqualTo(120));
        Assert.That(result.AutoStop, Is.True);
        _repository.Received(1).Insert(category);
    }

    [Test]
    public void Update_persists_target_ratio_target_seed_time_and_autostop()
    {
        var category = new Category
        {
            Id = 1,
            Name = "Anime",
            TargetRatio = 3.0,
            TargetSeedTimeMinutes = 240,
            AutoStop = false
        };

        _repository.Get(1).Returns(new Category { Id = 1, Name = "Anime" });
        _repository.Update(category).Returns(category);

        var result = _subject.Update(category);

        Assert.That(result.TargetRatio, Is.EqualTo(3.0));
        Assert.That(result.TargetSeedTimeMinutes, Is.EqualTo(240));
        Assert.That(result.AutoStop, Is.False);
        _repository.Received(1).Update(category);
    }

    [Test]
    public void Update_renamed_category_updates_matching_names_in_automation_scripts()
    {
        var oldCat = new Category { Id = 1, Name = "Movies", IsDefault = false };
        var newCat = new Category { Id = 1, Name = "Films", IsDefault = false };

        var script1 = new AutomationScript
        {
            Id = 1,
            Name = "Movie Pipeline",
            TargetCategories = new List<string> { "Movies", "Anime" }
        };
        var script2 = new AutomationScript
        {
            Id = 2,
            Name = "Case Insensitive Pipeline",
            TargetCategories = new List<string> { "movies" }
        };
        var script3 = new AutomationScript
        {
            Id = 3,
            Name = "TV Pipeline",
            TargetCategories = new List<string> { "TV" }
        };

        _repository.Get(1).Returns(oldCat);
        _repository.Update(newCat).Returns(newCat);
        _automationScriptRepository.All().Returns(new List<AutomationScript> { script1, script2, script3 });

        _subject.Update(newCat);

        _automationScriptRepository.Received(1).UpdateMany(Arg.Is<IEnumerable<AutomationScript>>(scripts =>
            scripts.Count() == 2 &&
            scripts.Any(s => s.Id == 1 && s.TargetCategories.Contains("Films") && !s.TargetCategories.Contains("Movies") && s.TargetCategories.Contains("Anime")) &&
            scripts.Any(s => s.Id == 2 && s.TargetCategories.Contains("Films") && !s.TargetCategories.Contains("movies"))));
    }

    [Test]
    public void Delete_removes_matching_categories_from_automation_scripts()
    {
        var cat = new Category { Id = 5, Name = "Anime", IsDefault = false };

        var script1 = new AutomationScript
        {
            Id = 1,
            Name = "Anime Pipeline",
            TargetCategories = new List<string> { "Anime", "Movies" }
        };
        var script2 = new AutomationScript
        {
            Id = 2,
            Name = "Only Anime Pipeline",
            TargetCategories = new List<string> { "anime" }
        };
        var script3 = new AutomationScript
        {
            Id = 3,
            Name = "Unrelated Pipeline",
            TargetCategories = new List<string> { "TV" }
        };

        _repository.Get(5).Returns(cat);
        _automationScriptRepository.All().Returns(new List<AutomationScript> { script1, script2, script3 });

        _subject.Delete(5);

        _automationScriptRepository.Received(1).UpdateMany(Arg.Is<IEnumerable<AutomationScript>>(scripts =>
            scripts.Count() == 2 &&
            scripts.Any(s => s.Id == 1 && !s.TargetCategories.Contains("Anime") && s.TargetCategories.Contains("Movies")) &&
            scripts.Any(s => s.Id == 2 && s.TargetCategories.Count == 0)));
    }
}
