using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
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
}
