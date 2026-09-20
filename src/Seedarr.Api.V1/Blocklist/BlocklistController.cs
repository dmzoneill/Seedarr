using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Blocklist;
using NzbDrone.Core.Configuration;
using Seedarr.Http;

namespace Seedarr.Api.V1.Blocklist;

[V1ApiController("blocklist")]
public class BlocklistController : Controller
{
    private readonly IConfigService _configService;
    private readonly IPeerBlocklistSyncService _syncService;

    public BlocklistController(IConfigService configService, IPeerBlocklistSyncService syncService)
    {
        _configService = configService;
        _syncService = syncService;
    }

    [HttpGet]
    public ActionResult<BlocklistResource> GetBlocklist()
    {
        return Ok(BuildResource());
    }

    [HttpPut]
    public ActionResult<BlocklistResource> UpdateBlocklist([FromBody] BlocklistConfigRequest request)
    {
        if (request == null)
        {
            return BadRequest("Request body cannot be empty.");
        }

        var updates = new Dictionary<string, object>();

        if (request.Enabled.HasValue)
        {
            updates["BlocklistEnabled"] = request.Enabled.Value;
        }

        if (request.Url != null)
        {
            updates["BlocklistUrl"] = request.Url.Trim();
        }

        if (request.AutoUpdateEnabled.HasValue)
        {
            updates["BlocklistAutoUpdate"] = request.AutoUpdateEnabled.Value;
        }

        if (request.AutoUpdateIntervalDays.HasValue && request.AutoUpdateIntervalDays.Value > 0)
        {
            updates["BlocklistUpdateIntervalDays"] = request.AutoUpdateIntervalDays.Value;
        }

        if (updates.Count > 0)
        {
            _configService.SaveConfigDictionary(updates);
        }

        return Ok(BuildResource());
    }

    [HttpPost("sync")]
    public async Task<ActionResult<BlocklistSyncResponse>> SyncBlocklistAsync(CancellationToken cancellationToken = default)
    {
        var result = await _syncService.SyncBlocklistAsync(force: true, cancellationToken: cancellationToken);
        var (v4, v6) = CountRules();
        var totalRules = _syncService.RuleCount > 0 ? _syncService.RuleCount : (v4 + v6);

        return Ok(new BlocklistSyncResponse
        {
            Success = result.Success,
            Status = result.Status,
            Message = result.Message,
            RuleCount = totalRules,
            TotalRuleCount = totalRules,
            Ipv4RuleCount = v4,
            Ipv6RuleCount = v6,
            LastUpdatedUtc = _syncService.LastCheckedUtc,
            NextAllowedSyncUtc = result.NextAllowedSyncUtc
        });
    }

    [HttpPost("test")]
    public ActionResult<BlocklistTestResponse> TestIp([FromBody] BlocklistTestRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Ip))
        {
            return BadRequest(new { message = "IP address is required." });
        }

        if (!IPAddress.TryParse(request.Ip.Trim(), out var address))
        {
            return BadRequest(new { message = "Invalid IP address format." });
        }

        var rules = _syncService.ActiveRules;
        if (rules == null || rules.Count == 0)
        {
            return Ok(new BlocklistTestResponse
            {
                IsBlocked = false,
                Rule = null
            });
        }

        var tree = Ipv6IntervalTree.Parse(rules);
        var isBlocked = tree.Contains(address);

        string matchedRule = null;
        if (isBlocked)
        {
            matchedRule = FindMatchingRule(address, rules);
        }

        return Ok(new BlocklistTestResponse
        {
            IsBlocked = isBlocked,
            Rule = matchedRule
        });
    }

    private BlocklistResource BuildResource()
    {
        var (v4, v6) = CountRules();
        var totalRules = _syncService.RuleCount > 0 ? _syncService.RuleCount : (v4 + v6);
        var lastChecked = _syncService.LastCheckedUtc;
        var autoUpdate = _configService.BlocklistAutoUpdate;
        var intervalDays = _configService.BlocklistUpdateIntervalDays;

        DateTime? nextScheduled = null;
        if (autoUpdate && intervalDays > 0 && lastChecked.HasValue)
        {
            nextScheduled = lastChecked.Value.AddDays(intervalDays);
        }

        return new BlocklistResource
        {
            Enabled = _configService.BlocklistEnabled,
            Url = _configService.BlocklistUrl,
            AutoUpdateEnabled = autoUpdate,
            AutoUpdateIntervalDays = intervalDays,
            Ipv4RuleCount = v4,
            Ipv6RuleCount = v6,
            TotalRuleCount = totalRules,
            RuleCount = totalRules,
            LastUpdatedUtc = lastChecked,
            LastSyncStatus = _syncService.LastSyncStatus ?? "Never Run",
            NextScheduledSyncUtc = nextScheduled,
            NextAllowedSyncUtc = _syncService.NextAllowedSyncUtc
        };
    }

    private (int V4Count, int V6Count) CountRules()
    {
        var rules = _syncService.ActiveRules;
        if (rules == null || rules.Count == 0)
        {
            return (0, 0);
        }

        var v4 = 0;
        var v6 = 0;
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                continue;
            }

            var trimmed = rule.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (Ipv4IntervalTree.TryParse(trimmed, out _))
            {
                v4++;
            }
            else if (Ipv6IntervalTree.TryParse(trimmed, out _))
            {
                v6++;
            }
        }

        return (v4, v6);
    }

    private static string FindMatchingRule(IPAddress address, IReadOnlyList<string> rules)
    {
        var isV4 = address.AddressFamily == AddressFamily.InterNetwork;
        var mappedV4 = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : null;
        var targetV4 = isV4 ? address : mappedV4;

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                continue;
            }

            var trimmed = rule.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (targetV4 != null && Ipv4IntervalTree.TryParse(trimmed, out var v4Range))
            {
                var v4Tree = new Ipv4IntervalTree(new[] { v4Range });
                if (v4Tree.Contains(targetV4))
                {
                    return trimmed;
                }
            }

            if (Ipv6IntervalTree.TryParse(trimmed, out var v6Range))
            {
                var v6Tree = new Ipv6IntervalTree(new[] { v6Range });
                if (v6Tree.Contains(address))
                {
                    return trimmed;
                }
            }
        }

        return "Active blocklist rule";
    }
}
