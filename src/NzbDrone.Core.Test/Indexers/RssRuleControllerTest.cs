using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using Seedarr.Api.V1.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class RssRuleControllerTest
{
    private IRssRuleRepository _rssRuleRepository;
    private RssRuleController _controller;

    [SetUp]
    public void SetUp()
    {
        _rssRuleRepository = Substitute.For<IRssRuleRepository>();
        _controller = new RssRuleController(_rssRuleRepository);
    }

    [Test]
    public void Create_duplicate_name_returns_bad_request()
    {
        var existingRule = new RssRule
        {
            Id = 1,
            Name = "1080p Releases",
            IsEnabled = true
        };

        _rssRuleRepository.All().Returns(new List<RssRule> { existingRule });

        var newRule = new RssRuleResource
        {
            Name = "1080P RELEASES",
            IsEnabled = true
        };

        var result = _controller.Create(newRule);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void Create_unique_name_succeeds()
    {
        _rssRuleRepository.All().Returns(new List<RssRule>());
        _rssRuleRepository.Insert(Arg.Any<RssRule>()).Returns(callInfo =>
        {
            var rule = callInfo.Arg<RssRule>();
            rule.Id = 10;
            return rule;
        });

        var newRule = new RssRuleResource
        {
            Name = "4K HDR Only",
            IsEnabled = true,
            Tags = new List<int> { 1, 2 }
        };

        var result = _controller.Create(newRule);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var created = (RssRuleResource)okResult.Value;
        Assert.That(created.Name, Is.EqualTo("4K HDR Only"));
        Assert.That(created.Tags, Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public void Update_duplicate_name_on_different_rule_returns_bad_request()
    {
        var rule1 = new RssRule { Id = 1, Name = "Rule 1" };
        var rule2 = new RssRule { Id = 2, Name = "Rule 2" };

        _rssRuleRepository.All().Returns(new List<RssRule> { rule1, rule2 });
        _rssRuleRepository.Get(2).Returns(rule2);

        var updateResource = new RssRuleResource
        {
            Id = 2,
            Name = "rule 1"
        };

        var result = _controller.Update(2, updateResource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void Update_same_name_on_same_rule_succeeds()
    {
        var rule1 = new RssRule { Id = 1, Name = "Rule 1", Tags = new List<int>() };

        _rssRuleRepository.All().Returns(new List<RssRule> { rule1 });
        _rssRuleRepository.Get(1).Returns(rule1);

        var updateResource = new RssRuleResource
        {
            Id = 1,
            Name = "Rule 1",
            Tags = new List<int> { 5 }
        };

        var result = _controller.Update(1, updateResource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var updated = (RssRuleResource)okResult.Value;
        Assert.That(updated.Tags, Is.EquivalentTo(new[] { 5 }));
        _rssRuleRepository.Received(1).Update(Arg.Is<RssRule>(r => r.Id == 1 && r.Tags.Contains(5)));
    }
}
