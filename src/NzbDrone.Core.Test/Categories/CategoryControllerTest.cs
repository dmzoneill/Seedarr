using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.SignalR;
using Seedarr.Api.V1.Categories;

namespace NzbDrone.Core.Test.Categories;

[TestFixture]
public class CategoryControllerTest
{
    private ICategoryService _categoryService;
    private IBroadcastSignalRMessage _signalRBroadcaster;
    private CategoryController _controller;

    [SetUp]
    public void SetUp()
    {
        _categoryService = Substitute.For<ICategoryService>();
        _signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        _controller = new CategoryController(_categoryService, _signalRBroadcaster);
    }

    [Test]
    public void Add_with_valid_category_and_save_path_succeeds()
    {
        var resource = new CategoryResource
        {
            Name = "Movies",
            SavePath = "/downloads/movies"
        };

        _categoryService.GetByName("Movies").Returns((Category)null);
        _categoryService.Add(Arg.Any<Category>()).Returns(callInfo =>
        {
            var cat = callInfo.Arg<Category>();
            cat.Id = 1;
            return cat;
        });

        var result = _controller.Add(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var created = (CategoryResource)okResult.Value;
        Assert.That(created.Name, Is.EqualTo("Movies"));
        Assert.That(created.SavePath, Is.EqualTo("/downloads/movies"));
    }

    [Test]
    public void Add_with_empty_save_path_succeeds()
    {
        var resource = new CategoryResource
        {
            Name = "DefaultCat",
            SavePath = ""
        };

        _categoryService.GetByName("DefaultCat").Returns((Category)null);
        _categoryService.Add(Arg.Any<Category>()).Returns(callInfo =>
        {
            var cat = callInfo.Arg<Category>();
            cat.Id = 2;
            return cat;
        });

        var result = _controller.Add(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public void Add_with_directory_traversal_in_save_path_returns_bad_request()
    {
        var resource = new CategoryResource
        {
            Name = "ExploitCat",
            SavePath = "/downloads/../../etc"
        };

        _categoryService.GetByName("ExploitCat").Returns((Category)null);

        var result = _controller.Add(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _categoryService.DidNotReceive().Add(Arg.Any<Category>());
    }

    [Test]
    public void Add_with_null_bytes_in_save_path_returns_bad_request()
    {
        var resource = new CategoryResource
        {
            Name = "NullByteCat",
            SavePath = "/downloads/test\0hidden"
        };

        _categoryService.GetByName("NullByteCat").Returns((Category)null);

        var result = _controller.Add(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _categoryService.DidNotReceive().Add(Arg.Any<Category>());
    }

    [Test]
    public void Update_with_directory_traversal_in_save_path_returns_bad_request()
    {
        var resource = new CategoryResource
        {
            Id = 1,
            Name = "Movies",
            SavePath = "../secret"
        };

        var existing = new Category
        {
            Id = 1,
            Name = "Movies",
            SavePath = "/downloads/movies"
        };

        _categoryService.Get(1).Returns(existing);

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _categoryService.DidNotReceive().Update(Arg.Any<Category>());
    }

    [Test]
    public void Update_with_null_bytes_in_save_path_returns_bad_request()
    {
        var resource = new CategoryResource
        {
            Id = 1,
            Name = "Movies",
            SavePath = "/path\0bad"
        };

        var existing = new Category
        {
            Id = 1,
            Name = "Movies",
            SavePath = "/downloads/movies"
        };

        _categoryService.Get(1).Returns(existing);

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _categoryService.DidNotReceive().Update(Arg.Any<Category>());
    }

    [Test]
    public void Update_with_valid_save_path_succeeds()
    {
        var resource = new CategoryResource
        {
            Id = 1,
            Name = "Movies",
            SavePath = "/new/downloads/movies"
        };

        var existing = new Category
        {
            Id = 1,
            Name = "Movies",
            SavePath = "/downloads/movies"
        };

        _categoryService.Get(1).Returns(existing);
        _categoryService.GetByName("Movies").Returns(existing);
        _categoryService.Update(Arg.Any<Category>()).Returns(callInfo => callInfo.Arg<Category>());

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _categoryService.Received(1).Update(Arg.Is<Category>(c => c.SavePath == "/new/downloads/movies"));
    }

    [Test]
    public void Handle_with_null_model_does_not_throw_and_does_not_broadcast()
    {
        _signalRBroadcaster.IsConnected.Returns(true);
        var modelEvent = new ModelEvent<Category>(null, ModelAction.Updated);

        Assert.DoesNotThrow(() => _controller.Handle(modelEvent));
        _signalRBroadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Handle_when_not_connected_does_not_broadcast()
    {
        _signalRBroadcaster.IsConnected.Returns(false);
        var category = new Category { Id = 1, Name = "Movies", SavePath = "/downloads" };
        var modelEvent = new ModelEvent<Category>(category, ModelAction.Updated);

        _controller.Handle(modelEvent);
        _signalRBroadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Handle_with_valid_model_broadcasts_when_connected()
    {
        _signalRBroadcaster.IsConnected.Returns(true);
        var category = new Category { Id = 1, Name = "Movies", SavePath = "/downloads" };
        var modelEvent = new ModelEvent<Category>(category, ModelAction.Updated);

        _controller.Handle(modelEvent);
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Action == ModelAction.Updated &&
            m.Name == "category" &&
            m.Body is CategoryResource));
    }

    [Test]
    public void Delete_with_valid_id_returns_no_content()
    {
        var result = _controller.Delete(1);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        _categoryService.Received(1).Delete(1);
    }

    [Test]
    public void Delete_when_service_throws_invalid_operation_exception_returns_bad_request()
    {
        _categoryService.When(x => x.Delete(1)).Do(_ =>
            throw new InvalidOperationException("Cannot delete category 'Default' because it is configured as the default category. Designate another category as default before deleting this one."));

        var result = _controller.Delete(1);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result;
        Assert.That(badRequest.Value, Does.Contain("Cannot delete category 'Default'"));
    }

    [Test]
    public void Add_with_relative_save_path_returns_bad_request()
    {
        var resource = new CategoryResource
        {
            Name = "RelativeCat",
            SavePath = "downloads/movies"
        };

        _categoryService.GetByName("RelativeCat").Returns((Category)null);

        var result = _controller.Add(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Does.Contain("Save path must be an absolute path"));
        _categoryService.DidNotReceive().Add(Arg.Any<Category>());
    }

    [Test]
    public void Update_with_relative_save_path_returns_bad_request()
    {
        var resource = new CategoryResource
        {
            Id = 1,
            Name = "RelativeCat",
            SavePath = "downloads/movies"
        };

        var existing = new Category { Id = 1, Name = "RelativeCat", SavePath = "/downloads" };
        _categoryService.Get(1).Returns(existing);

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Does.Contain("Save path must be an absolute path"));
        _categoryService.DidNotReceive().Update(Arg.Any<Category>());
    }

    [Test]
    public void Add_normalizes_trailing_slashes_in_save_path()
    {
        var resource = new CategoryResource
        {
            Name = "NormalizedCat",
            SavePath = "/downloads/movies///"
        };

        _categoryService.GetByName("NormalizedCat").Returns((Category)null);
        _categoryService.Add(Arg.Any<Category>()).Returns(callInfo =>
        {
            var cat = callInfo.Arg<Category>();
            cat.Id = 1;
            return cat;
        });

        var result = _controller.Add(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _categoryService.Received(1).Add(Arg.Is<Category>(c => c.SavePath == "/downloads/movies"));
    }

    [Test]
    public void Update_normalizes_trailing_slashes_in_save_path()
    {
        var resource = new CategoryResource
        {
            Id = 1,
            Name = "Movies",
            SavePath = "/new/downloads/movies/"
        };

        var existing = new Category { Id = 1, Name = "Movies", SavePath = "/downloads/movies" };
        _categoryService.Get(1).Returns(existing);
        _categoryService.GetByName("Movies").Returns(existing);
        _categoryService.Update(Arg.Any<Category>()).Returns(callInfo => callInfo.Arg<Category>());

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _categoryService.Received(1).Update(Arg.Is<Category>(c => c.SavePath == "/new/downloads/movies"));
    }

    [Test]
    public void Add_when_service_throws_invalid_operation_exception_returns_bad_request()
    {
        var resource = new CategoryResource
        {
            Name = "PermissionDeniedCat",
            SavePath = "/root/forbidden"
        };

        _categoryService.GetByName("PermissionDeniedCat").Returns((Category)null);
        _categoryService.When(x => x.Add(Arg.Any<Category>())).Do(_ =>
            throw new InvalidOperationException("Cannot access or write to save path '/root/forbidden': Permission denied"));

        var result = _controller.Add(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Does.Contain("Cannot access or write to save path '/root/forbidden'"));
    }

    [Test]
    public void Update_when_service_throws_invalid_operation_exception_returns_bad_request()
    {
        var resource = new CategoryResource
        {
            Id = 1,
            Name = "Movies",
            SavePath = "/root/forbidden"
        };

        var existing = new Category { Id = 1, Name = "Movies", SavePath = "/downloads/movies" };
        _categoryService.Get(1).Returns(existing);
        _categoryService.GetByName("Movies").Returns(existing);
        _categoryService.When(x => x.Update(Arg.Any<Category>())).Do(_ =>
            throw new InvalidOperationException("Cannot access or write to save path '/root/forbidden': Permission denied"));

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Does.Contain("Cannot access or write to save path '/root/forbidden'"));
    }
}
