using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;

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
}
