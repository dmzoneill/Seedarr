using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Blocklist;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Blocklist;

namespace NzbDrone.Core.Test.Blocklist;

[TestFixture]
public class BlocklistControllerTests
{
    private IConfigService _configService;
    private IPeerBlocklistSyncService _syncService;
    private BlocklistController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _syncService = Substitute.For<IPeerBlocklistSyncService>();
        _controller = new BlocklistController(_configService, _syncService);
    }

    [Test]
    public void GetBlocklist_should_return_status_and_rule_counts()
    {
        // Arrange
        var lastChecked = new DateTime(2026, 9, 20, 1, 0, 0, DateTimeKind.Utc);
        _configService.BlocklistEnabled.Returns(true);
        _configService.BlocklistUrl.Returns("https://example.com/rules.txt");
        _configService.BlocklistAutoUpdate.Returns(true);
        _configService.BlocklistUpdateIntervalDays.Returns(7);

        var activeRules = new List<string>
        {
            "Test IPv4 Rule: 192.168.1.0/24",
            "Test IPv6 Rule: 2001:db8::/32"
        };
        _syncService.ActiveRules.Returns(activeRules);
        _syncService.RuleCount.Returns(2);
        _syncService.LastCheckedUtc.Returns(lastChecked);
        _syncService.LastSyncStatus.Returns("Success");

        // Act
        var actionResult = _controller.GetBlocklist();

        // Assert
        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var resource = (BlocklistResource)okResult.Value;

        Assert.That(resource.Enabled, Is.True);
        Assert.That(resource.Url, Is.EqualTo("https://example.com/rules.txt"));
        Assert.That(resource.AutoUpdateEnabled, Is.True);
        Assert.That(resource.AutoUpdateIntervalDays, Is.EqualTo(7));
        Assert.That(resource.Ipv4RuleCount, Is.EqualTo(1));
        Assert.That(resource.Ipv6RuleCount, Is.EqualTo(1));
        Assert.That(resource.TotalRuleCount, Is.EqualTo(2));
        Assert.That(resource.LastUpdatedUtc, Is.EqualTo(lastChecked));
        Assert.That(resource.LastSyncStatus, Is.EqualTo("Success"));
        Assert.That(resource.NextScheduledSyncUtc, Is.EqualTo(lastChecked.AddDays(7)));
    }

    [Test]
    public void UpdateBlocklist_should_save_configuration()
    {
        // Arrange
        Dictionary<string, object> savedDict = null;
        _configService.When(x => x.SaveConfigDictionary(Arg.Any<Dictionary<string, object>>(), Arg.Any<bool>()))
            .Do(call => savedDict = call.Arg<Dictionary<string, object>>());

        var request = new BlocklistConfigRequest
        {
            Enabled = true,
            Url = "https://example.com/blocklist.gz",
            AutoUpdateEnabled = false,
            AutoUpdateIntervalDays = 3
        };

        // Act
        var actionResult = _controller.UpdateBlocklist(request);

        // Assert
        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        _configService.Received(1).SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
        Assert.That(savedDict, Is.Not.Null);
        Assert.That(savedDict["BlocklistEnabled"], Is.EqualTo(true));
        Assert.That(savedDict["BlocklistUrl"], Is.EqualTo("https://example.com/blocklist.gz"));
        Assert.That(savedDict["BlocklistAutoUpdate"], Is.EqualTo(false));
        Assert.That(savedDict["BlocklistUpdateIntervalDays"], Is.EqualTo(3));
    }

    [Test]
    public void UpdateBlocklist_with_null_request_should_return_bad_request()
    {
        var result = _controller.UpdateBlocklist(null);
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task SyncBlocklistAsync_should_trigger_sync_and_return_counts()
    {
        // Arrange
        var syncResult = new BlocklistSyncResult
        {
            Success = true,
            Status = "Success",
            Message = "Updated successfully",
            RuleCount = 500,
            HttpStatusCode = HttpStatusCode.OK
        };
        _syncService.SyncBlocklistAsync(force: true, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(syncResult));
        _syncService.RuleCount.Returns(500);
        _syncService.LastCheckedUtc.Returns(DateTime.UtcNow);

        // Act
        var actionResult = await _controller.SyncBlocklistAsync();

        // Assert
        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        var response = (BlocklistSyncResponse)okResult.Value;

        Assert.That(response.Success, Is.True);
        Assert.That(response.Status, Is.EqualTo("Success"));
        Assert.That(response.RuleCount, Is.EqualTo(500));
        await _syncService.Received(1).SyncBlocklistAsync(force: true, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public void TestIp_should_return_isBlocked_true_and_rule_when_matching_active_rule()
    {
        // Arrange
        var activeRules = new List<string>
        {
            "Bluetack Level 1: 198.51.100.0-198.51.100.255",
            "Suspicious IPv6: 2001:db8:abc::/48"
        };
        _syncService.ActiveRules.Returns(activeRules);
        _syncService.RuleCount.Returns(2);

        // Act - test blocked IPv4
        var resultV4 = _controller.TestIp(new BlocklistTestRequest { Ip = "198.51.100.42" });

        // Assert
        Assert.That(resultV4.Result, Is.InstanceOf<OkObjectResult>());
        var okV4 = (OkObjectResult)resultV4.Result;
        var respV4 = (BlocklistTestResponse)okV4.Value;
        Assert.That(respV4.IsBlocked, Is.True);
        Assert.That(respV4.Rule, Is.EqualTo("Bluetack Level 1: 198.51.100.0-198.51.100.255"));

        // Act - test blocked IPv6
        var resultV6 = _controller.TestIp(new BlocklistTestRequest { Ip = "2001:db8:abc:1::1" });

        // Assert
        Assert.That(resultV6.Result, Is.InstanceOf<OkObjectResult>());
        var okV6 = (OkObjectResult)resultV6.Result;
        var respV6 = (BlocklistTestResponse)okV6.Value;
        Assert.That(respV6.IsBlocked, Is.True);
        Assert.That(respV6.Rule, Is.EqualTo("Suspicious IPv6: 2001:db8:abc::/48"));
    }

    [Test]
    public void TestIp_should_return_isBlocked_false_when_ip_is_allowed()
    {
        // Arrange
        var activeRules = new List<string>
        {
            "Bluetack Level 1: 198.51.100.0-198.51.100.255"
        };
        _syncService.ActiveRules.Returns(activeRules);
        _syncService.RuleCount.Returns(1);

        // Act
        var result = _controller.TestIp(new BlocklistTestRequest { Ip = "203.0.113.1" });

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var response = (BlocklistTestResponse)okResult.Value;

        Assert.That(response.IsBlocked, Is.False);
        Assert.That(response.Rule, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-an-ip-address")]
    [TestCase("999.999.999.999")]
    public void TestIp_should_return_bad_request_for_invalid_ip(string ip)
    {
        var result = _controller.TestIp(new BlocklistTestRequest { Ip = ip });
        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }
}
