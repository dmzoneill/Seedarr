using System;
using System.Collections.Generic;
using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Categories;

[TestFixture]
public class CategoryServiceTest
{
    private ICategoryRepository _repository;
    private IEventAggregator _eventAggregator;
    private ITorrentRepository _torrentRepository;
    private CategoryService _subject;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<ICategoryRepository>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _torrentRepository = Substitute.For<ITorrentRepository>();
        _subject = new CategoryService(_repository, _eventAggregator, _torrentRepository);
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
    public void Update_renamed_category_updates_associated_torrents()
    {
        var oldCat = new Category { Id = 1, Name = "OldName", IsDefault = false };
        var newCat = new Category { Id = 1, Name = "NewName", IsDefault = false };

        _repository.Get(1).Returns(oldCat);
        _repository.Update(newCat).Returns(newCat);

        _subject.Update(newCat);

        _torrentRepository.Received(1).UpdateCategoryName("OldName", "NewName");
    }

    [Test]
    public void Update_when_category_name_unchanged_does_not_update_torrents()
    {
        var oldCat = new Category { Id = 1, Name = "SameName", IsDefault = false };
        var newCat = new Category { Id = 1, Name = "SameName", IsDefault = false };

        _repository.Get(1).Returns(oldCat);
        _repository.Update(newCat).Returns(newCat);

        _subject.Update(newCat);

        _torrentRepository.DidNotReceive().UpdateCategoryName(Arg.Any<string>(), Arg.Any<string>());
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
    public void Delete_when_category_is_not_default_deletes_and_clears_torrent_categories()
    {
        var cat = new Category { Id = 5, Name = "Anime", IsDefault = false };

        _repository.Get(5).Returns(cat);

        _subject.Delete(5);

        _torrentRepository.Received(1).ClearCategory("Anime");
        _repository.Received(1).Delete(5);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryDeletedEvent>(e =>
            e.CategoryId == 5 &&
            e.CategoryName == "Anime"));
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
        Assert.That(CategoryService.NormalizeSavePath("/"), Is.EqualTo("/"));
        Assert.That(CategoryService.NormalizeSavePath(""), Is.EqualTo(string.Empty));
        Assert.That(CategoryService.NormalizeSavePath(null), Is.EqualTo(string.Empty));
    }
}
