using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Indexers;
using Seedarr.Api.V1.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class RssRuleControllerTest
{
    private IRssRuleRepository _rssRuleRepository;
    private IIndexerRepository _indexerRepository;
    private ICategoryService _categoryService;
    private RssRuleController _controller;

    [SetUp]
    public void SetUp()
    {
        _rssRuleRepository = Substitute.For<IRssRuleRepository>();
        _indexerRepository = Substitute.For<IIndexerRepository>();
        _categoryService = Substitute.For<ICategoryService>();
        _controller = new RssRuleController(_rssRuleRepository, _indexerRepository, _categoryService);
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
    public void Create_inverted_size_bounds_returns_bad_request()
    {
        var newRule = new RssRuleResource
        {
            Name = "Inverted Size Rule",
            MinSizeBytes = 5000,
            MaxSizeBytes = 1000
        };

        var result = _controller.Create(newRule);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(-1, 0, 0, 0, 0)]
    [TestCase(0, -1, 0, 0, 0)]
    [TestCase(0, 0, -1, 0, 0)]
    [TestCase(0, 0, 0, -1, 0)]
    [TestCase(0, 0, 0, 0, -1)]
    public void Create_negative_numerical_constraints_returns_bad_request(
        int minSeeders, long minSize, long maxSize, int maxAge, int priority)
    {
        var newRule = new RssRuleResource
        {
            Name = "Negative Constraints Rule",
            MinSeeders = minSeeders,
            MinSizeBytes = minSize,
            MaxSizeBytes = maxSize,
            MaxAgeDays = maxAge,
            Priority = priority
        };

        var result = _controller.Create(newRule);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void Create_catastrophic_backtracking_regex_returns_bad_request_due_to_timeout()
    {
        var newRule = new RssRuleResource
        {
            Name = "ReDoS Rule",
            MustContain = "(a+)+$"
        };

        var result = _controller.Create(newRule);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void Create_non_existent_indexer_id_returns_bad_request()
    {
        _indexerRepository.Get(99).Returns((IndexerDefinition)null);

        var newRule = new RssRuleResource
        {
            Name = "Invalid Indexer Rule",
            IndexerIds = new List<int> { 99 }
        };

        var result = _controller.Create(newRule);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void Create_non_existent_category_id_returns_bad_request()
    {
        _categoryService.Get(42).Returns((Category)null);

        var newRule = new RssRuleResource
        {
            Name = "Invalid Category Rule",
            CategoryId = 42
        };

        var result = _controller.Create(newRule);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void GetAll_returns_rules_ordered_by_priority_then_id()
    {
        var rule1 = new RssRule { Id = 1, Name = "Priority 10", Priority = 10 };
        var rule2 = new RssRule { Id = 2, Name = "Priority 0, Id 2", Priority = 0 };
        var rule3 = new RssRule { Id = 3, Name = "Priority 0, Id 3", Priority = 0 };
        var rule4 = new RssRule { Id = 4, Name = "Priority 5", Priority = 5 };

        _rssRuleRepository.All().Returns(new List<RssRule> { rule1, rule2, rule3, rule4 });

        var result = _controller.GetAll();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var rules = (List<RssRuleResource>)okResult.Value;

        Assert.That(rules.Select(r => r.Id), Is.EqualTo(new[] { 2, 3, 4, 1 }));
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
