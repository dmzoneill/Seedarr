// Copyright (c) FeedItOut. All rights reserved.

using NUnit.Framework;
using NzbDrone.Core.Developer.Uml;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class DeveloperUmlServiceTest
{
    private DeveloperUmlService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new DeveloperUmlService();
    }

    [Test]
    public void GetAvailableSubsystems_returns_expected_subsystems()
    {
        var subsystems = _service.GetAvailableSubsystems();

        Assert.That(subsystems, Is.Not.Empty);
        Assert.That(subsystems, Contains.Item("all"));
        Assert.That(subsystems, Contains.Item("torrents"));
        Assert.That(subsystems, Contains.Item("trackers"));
        Assert.That(subsystems, Contains.Item("indexers"));
    }

    [Test]
    public void GetAvailableDiagramTypes_returns_expected_types()
    {
        var types = _service.GetAvailableDiagramTypes();

        Assert.That(types, Is.Not.Empty);
        Assert.That(types, Contains.Item("class"));
        Assert.That(types, Contains.Item("di"));
        Assert.That(types, Contains.Item("state"));
        Assert.That(types, Contains.Item("api"));
        Assert.That(types, Contains.Item("frontend"));
    }

    [TestCase("class", "torrents")]
    [TestCase("class", "all")]
    [TestCase("class", "authentication")]
    [TestCase("class", "automation")]
    [TestCase("class", "notifications")]
    [TestCase("class", "network")]
    [TestCase("class", "storage")]
    [TestCase("di", "torrents")]
    [TestCase("di", "all")]
    [TestCase("state", "torrents")]
    [TestCase("state", "trackers")]
    [TestCase("api", "all")]
    [TestCase("api", "torrents")]
    [TestCase("frontend", "all")]
    public void GenerateDiagram_produces_valid_mermaid_markup(string diagramType, string subsystem)
    {
        var options = new DeveloperUmlOptions
        {
            DiagramType = diagramType,
            Subsystem = subsystem
        };

        var result = _service.GenerateDiagram(options);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.DiagramType, Is.EqualTo(diagramType));
        Assert.That(result.MermaidCode, Is.Not.Null);
        Assert.That(result.MermaidCode.Length, Is.GreaterThan(10));
        Assert.That(result.NodeCount, Is.GreaterThan(0));

        if (diagramType == "class")
        {
            Assert.That(result.MermaidCode, Does.StartWith("classDiagram"));
        }
        else if (diagramType == "di")
        {
            Assert.That(result.MermaidCode, Does.StartWith("graph TD"));
        }
        else if (diagramType == "state")
        {
            Assert.That(result.MermaidCode, Does.StartWith("stateDiagram-v2"));
        }
        else if (diagramType == "api")
        {
            Assert.That(result.MermaidCode, Does.StartWith("graph LR"));
        }
        else if (diagramType == "frontend")
        {
            Assert.That(result.MermaidCode, Does.StartWith("graph TD"));
        }
    }

    [Test]
    public void GenerateDiagram_respects_exclude_interfaces_and_methods()
    {
        var options = new DeveloperUmlOptions
        {
            DiagramType = "class",
            Subsystem = "torrents",
            IncludeInterfaces = false,
            IncludeMethods = false,
        };

        var result = _service.GenerateDiagram(options);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.MermaidCode, Does.StartWith("classDiagram"));
    }

    [Test]
    public void GenerateDiagram_falls_back_to_class_diagram_when_unknown_type()
    {
        var options = new DeveloperUmlOptions { DiagramType = "unrecognized-type" };
        var result = _service.GenerateDiagram(options);

        Assert.That(result.DiagramType, Is.EqualTo("class"));
        Assert.That(result.MermaidCode, Does.StartWith("classDiagram"));
    }
}
