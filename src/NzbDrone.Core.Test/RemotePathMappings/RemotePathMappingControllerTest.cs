using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.RemotePathMappings;
using Seedarr.Api.V1.RemotePathMappings;

namespace NzbDrone.Core.Test.RemotePathMappings;

[TestFixture]
public class RemotePathMappingControllerTest
{
    private IRemotePathMappingService _service;
    private RemotePathMappingController _controller;

    [SetUp]
    public void SetUp()
    {
        _service = Substitute.For<IRemotePathMappingService>();
        _controller = new RemotePathMappingController(_service);
    }

    [Test]
    public void RemotePathMappingController_should_have_proper_authorization_policies()
    {
        var classAttr = typeof(RemotePathMappingController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.That(classAttr.Policy, Is.EqualTo(Policies.Reader));

        var createAttr = typeof(RemotePathMappingController).GetMethod("Create")!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.That(createAttr.Policy, Is.EqualTo(Policies.AdminOnly));

        var updateAttr = typeof(RemotePathMappingController).GetMethod("Update")!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.That(updateAttr.Policy, Is.EqualTo(Policies.AdminOnly));

        var deleteAttr = typeof(RemotePathMappingController).GetMethod("Delete")!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.That(deleteAttr.Policy, Is.EqualTo(Policies.AdminOnly));

        var testAttr = typeof(RemotePathMappingController).GetMethod("Test")!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.That(testAttr.Policy, Is.EqualTo(Policies.AdminOnly));
    }

    [Test]
    public void GetAll_should_return_mappings()
    {
        _service.All().Returns(new List<RemotePathMapping>
        {
            new() { Id = 1, Host = "host1", RemotePath = "/r", LocalPath = "/l" }
        });

        var result = _controller.GetAll();
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
    }
}
