using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Plugins;
using Seedarr.Api.V1.Plugins;

namespace NzbDrone.Core.Test.Plugins;

[TestFixture]
public class PluginControllerTest
{
    private IPluginService _pluginService;
    private PluginController _controller;

    [SetUp]
    public void SetUp()
    {
        _pluginService = Substitute.For<IPluginService>();
        _controller = new PluginController(_pluginService);
    }

    [Test]
    public void GetAll_should_return_mapped_plugin_resources()
    {
        var manifest = new PluginManifest
        {
            Id = "test-plugin",
            Name = "Test Plugin",
            Version = "1.0.0",
            Entrypoint = "bin/run.sh",
            Type = "sidecar",
            Capabilities = new[] { "indexer" }
        };

        var pluginInfo = new PluginInfo
        {
            Manifest = manifest,
            State = PluginState.Running,
            Enabled = true,
            ProcessId = 1234
        };

        _pluginService.GetAll().Returns(new List<PluginInfo> { pluginInfo });

        var actionResult = _controller.GetAll();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var resources = okResult.Value as List<PluginResource>;
        Assert.That(resources, Is.Not.Null);
        Assert.That(resources.Count, Is.EqualTo(1));
        Assert.That(resources[0].Id, Is.EqualTo("test-plugin"));
        Assert.That(resources[0].Status, Is.EqualTo("Running"));
        Assert.That(resources[0].Enabled, Is.True);
        Assert.That(resources[0].ProcessId, Is.EqualTo(1234));
    }

    [Test]
    public void Enable_should_return_NotFound_when_plugin_does_not_exist()
    {
        _pluginService.Enable("unknown").Returns((PluginInfo)null);

        var actionResult = _controller.Enable("unknown");
        Assert.That(actionResult.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void Disable_should_return_Ok_when_plugin_exists()
    {
        var manifest = new PluginManifest { Id = "active-plugin", Name = "Active Plugin", Version = "1.0.0" };
        var pluginInfo = new PluginInfo { Manifest = manifest, State = PluginState.Disabled, Enabled = false };

        _pluginService.Disable("active-plugin").Returns(pluginInfo);

        var actionResult = _controller.Disable("active-plugin");
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var resource = okResult.Value as PluginResource;
        Assert.That(resource, Is.Not.Null);
        Assert.That(resource.Id, Is.EqualTo("active-plugin"));
        Assert.That(resource.Enabled, Is.False);
    }
}
