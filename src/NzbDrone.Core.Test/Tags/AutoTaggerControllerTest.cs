using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Tags;
using Seedarr.Api.V1.Tags;

namespace NzbDrone.Core.Test.Tags;

[TestFixture]
public class AutoTaggerControllerTest
{
    private IAutoTaggerService _autoTaggerService;
    private ITagService _tagService;
    private AutoTaggerController _controller;

    [SetUp]
    public void SetUp()
    {
        _autoTaggerService = Substitute.For<IAutoTaggerService>();
        _tagService = Substitute.For<ITagService>();
        _controller = new AutoTaggerController(_autoTaggerService, _tagService);
    }

    [Test]
    public void SaveRule_returns_bad_request_when_body_is_null()
    {
        var result = _controller.SaveRule(null);
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void SaveRule_returns_bad_request_when_name_is_empty(string name)
    {
        var resource = new AutoTaggerRuleResource
        {
            Name = name,
            Pattern = "Test",
            TagId = 1
        };

        var result = _controller.SaveRule(resource);
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void SaveRule_returns_bad_request_when_pattern_is_empty(string pattern)
    {
        var resource = new AutoTaggerRuleResource
        {
            Name = "Test Rule",
            Pattern = pattern,
            TagId = 1
        };

        var result = _controller.SaveRule(resource);
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-5)]
    public void SaveRule_returns_bad_request_when_tag_id_is_zero_or_negative(int tagId)
    {
        var resource = new AutoTaggerRuleResource
        {
            Name = "Test Rule",
            Pattern = "Test",
            TagId = tagId
        };

        var result = _controller.SaveRule(resource);
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Referenced tag does not exist."));
    }

    [Test]
    public void SaveRule_returns_bad_request_when_tag_does_not_exist()
    {
        _tagService.Get(99).Returns((Tag)null);

        var resource = new AutoTaggerRuleResource
        {
            Name = "Test Rule",
            Pattern = "Test",
            TagId = 99
        };

        var result = _controller.SaveRule(resource);
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Referenced tag does not exist."));
    }

    [Test]
    public void SaveRule_creates_new_rule_when_id_is_zero()
    {
        _tagService.Get(10).Returns(new Tag { Id = 10, Label = "4K" });

        var resource = new AutoTaggerRuleResource
        {
            Id = 0,
            Name = "New Rule",
            Pattern = "4K",
            TagId = 10,
            RuleType = AutoTaggerRuleType.Regex,
            IsEnabled = true,
            Priority = 1
        };

        var createdModel = new AutoTaggerRule
        {
            Id = 1,
            Name = "New Rule",
            Pattern = "4K",
            TagId = 10,
            RuleType = AutoTaggerRuleType.Regex,
            IsEnabled = true,
            Priority = 1
        };

        _autoTaggerService.AddRule(Arg.Any<AutoTaggerRule>()).Returns(createdModel);

        var result = _controller.SaveRule(resource);
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var ruleResource = okResult.Value as AutoTaggerRuleResource;
        Assert.That(ruleResource, Is.Not.Null);
        Assert.That(ruleResource.Id, Is.EqualTo(1));
        Assert.That(ruleResource.TagLabel, Is.EqualTo("4K"));
    }

    [Test]
    public void SaveRule_updates_rule_when_id_is_greater_than_zero()
    {
        _tagService.Get(10).Returns(new Tag { Id = 10, Label = "4K" });

        var resource = new AutoTaggerRuleResource
        {
            Id = 5,
            Name = "Updated Rule",
            Pattern = "4K",
            TagId = 10,
            RuleType = AutoTaggerRuleType.Regex,
            IsEnabled = true,
            Priority = 1
        };

        var updatedModel = new AutoTaggerRule
        {
            Id = 5,
            Name = "Updated Rule",
            Pattern = "4K",
            TagId = 10,
            RuleType = AutoTaggerRuleType.Regex,
            IsEnabled = true,
            Priority = 1
        };

        _autoTaggerService.UpdateRule(Arg.Any<AutoTaggerRule>()).Returns(updatedModel);

        var result = _controller.SaveRule(resource);
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var ruleResource = okResult.Value as AutoTaggerRuleResource;
        Assert.That(ruleResource, Is.Not.Null);
        Assert.That(ruleResource.Id, Is.EqualTo(5));
        Assert.That(ruleResource.TagLabel, Is.EqualTo("4K"));
    }

    [Test]
    public void GetRules_returns_all_rules_with_tag_labels()
    {
        var rules = new List<AutoTaggerRule>
        {
            new() { Id = 1, Name = "Rule 1", TagId = 10, Pattern = "P1" },
            new() { Id = 2, Name = "Rule 2", TagId = 20, Pattern = "P2" }
        };

        _autoTaggerService.GetAllRules().Returns(rules);
        _tagService.GetAll().Returns(new List<Tag>
        {
            new() { Id = 10, Label = "Tag10" },
            new() { Id = 20, Label = "Tag20" }
        });

        var result = _controller.GetRules();
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var list = okResult.Value as List<AutoTaggerRuleResource>;
        Assert.That(list, Has.Count.EqualTo(2));
        Assert.That(list[0].TagLabel, Is.EqualTo("Tag10"));
        Assert.That(list[1].TagLabel, Is.EqualTo("Tag20"));
    }

    [Test]
    public void DeleteRule_deletes_specified_rule()
    {
        var result = _controller.DeleteRule(5);
        Assert.That(result, Is.InstanceOf<OkResult>());
        _autoTaggerService.Received(1).DeleteRule(5);
    }

    [Test]
    public void Evaluate_evaluates_all_rules()
    {
        var result = _controller.Evaluate();
        Assert.That(result, Is.InstanceOf<OkResult>());
        _autoTaggerService.Received(1).EvaluateAll();
    }

    [Test]
    public void Controller_should_have_Authorize_Reader_attribute()
    {
        var type = typeof(AutoTaggerController);
        var attr = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.Reader));
    }

    [TestCase(nameof(AutoTaggerController.SaveRule))]
    [TestCase(nameof(AutoTaggerController.DeleteRule))]
    [TestCase(nameof(AutoTaggerController.Evaluate))]
    public void Mutating_methods_should_have_Authorize_Operator_attribute(string methodName)
    {
        var method = typeof(AutoTaggerController).GetMethods().FirstOrDefault(m => m.Name == methodName);
        Assert.That(method, Is.Not.Null);

        var attr = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.Operator));
    }
}
