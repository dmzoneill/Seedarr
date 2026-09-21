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
    private IRssGrabHistoryRepository _grabHistoryRepository;
    private RssRuleController _controller;

    [SetUp]
    public void SetUp()
    {
        _rssRuleRepository = Substitute.For<IRssRuleRepository>();
        _indexerRepository = Substitute.For<IIndexerRepository>();
        _categoryService = Substitute.For<ICategoryService>();
        _grabHistoryRepository = Substitute.For<IRssGrabHistoryRepository>();
        _controller = new RssRuleController(_rssRuleRepository, _indexerRepository, _categoryService, _grabHistoryRepository);
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

    [Test]
    public void Create_with_quality_and_ingestion_mappings_succeeds()
    {
        _rssRuleRepository.All().Returns(new List<RssRule>());
        _rssRuleRepository.Insert(Arg.Any<RssRule>()).Returns(callInfo =>
        {
            var rule = callInfo.Arg<RssRule>();
            rule.Id = 42;
            return rule;
        });

        var resource = new RssRuleResource
        {
            Name = "4K Remux Rule",
            AllowedResolutions = new List<string> { "2160p" },
            AllowedSources = new List<string> { "Remux" },
            AllowedCodecs = new List<string> { "HEVC" },
            SavePath = "/downloads/4k",
            SequentialDownload = true,
            InitialStatus = "Paused"
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var created = (RssRuleResource)((OkObjectResult)result.Result).Value;
        Assert.That(created.AllowedResolutions, Does.Contain("2160p"));
        Assert.That(created.AllowedSources, Does.Contain("Remux"));
        Assert.That(created.AllowedCodecs, Does.Contain("HEVC"));
        Assert.That(created.SavePath, Is.EqualTo("/downloads/4k"));
        Assert.That(created.SequentialDownload, Is.True);
        Assert.That(created.InitialStatus, Is.EqualTo("Paused"));
    }

    [Test]
    public void Create_with_invalid_InitialStatus_returns_bad_request()
    {
        var resource = new RssRuleResource
        {
            Name = "Invalid Status Rule",
            InitialStatus = "InvalidStatusName"
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void GetHistory_should_return_records_from_repository()
    {
        var historyRecords = new List<RssGrabHistory>
        {
            new RssGrabHistory
            {
                Id = 1,
                ReleaseTitle = "Movie.2024.1080p",
                Status = "Grabbed",
                RuleId = 10,
                RuleName = "HD Rule"
            }
        };

        _grabHistoryRepository.GetCount(null, null).Returns(1);
        _grabHistoryRepository.GetHistory(null, null, 50, 0).Returns(historyRecords);

        // Mock controller response headers
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        var result = _controller.GetHistory();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var returned = (List<RssGrabHistoryResource>)((OkObjectResult)result.Result).Value;
        Assert.That(returned.Count, Is.EqualTo(1));
        Assert.That(returned[0].ReleaseTitle, Is.EqualTo("Movie.2024.1080p"));
        Assert.That(returned[0].Status, Is.EqualTo("Grabbed"));
    }

    [Test]
    public void SyncRss_invokes_service_and_returns_true_grabbed_count()
    {
        RssRuleController.ResetSyncCooldown();
        var rssSyncService = Substitute.For<IRssSyncService>();
        rssSyncService.Sync(true).Returns(5);

        var controller = new RssRuleController(
            _rssRuleRepository,
            _indexerRepository,
            _categoryService,
            _grabHistoryRepository,
            rssSyncService: rssSyncService);

        var response = controller.SyncRss();

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response.Result;
        var successProp = okResult.Value?.GetType().GetProperty("success")?.GetValue(okResult.Value);
        var grabbedCountProp = okResult.Value?.GetType().GetProperty("grabbedCount")?.GetValue(okResult.Value);
        Assert.That(successProp, Is.EqualTo(true));
        Assert.That(grabbedCountProp, Is.EqualTo(5));

        rssSyncService.Received(1).Sync(true);
    }
}
