using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Blocklist;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Validation;
using Seedarr.Http;

namespace Seedarr.Api.V1.Blocklist;

[V1ApiController("blocklist")]
[Authorize(Policy = Policies.Reader)]
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
    [Authorize(Policy = Policies.AdminOnly)]
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
            var trimmedUrl = request.Url.Trim();
            if (!string.IsNullOrEmpty(trimmedUrl) && !UrlValidator.IsSafeUrl(trimmedUrl))
            {
                return BadRequest("Blocklist URL must be a valid public HTTP or HTTPS URL.");
            }

            updates["BlocklistUrl"] = trimmedUrl;

            var previousUrl = _configService.BlocklistUrl ?? string.Empty;
            if (!string.Equals(previousUrl.Trim(), trimmedUrl, StringComparison.Ordinal))
            {
                updates["BlocklistETag"] = string.Empty;
                updates["BlocklistLastModified"] = string.Empty;
                updates["BlocklistValidatorUrl"] = string.Empty;
            }
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
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BlocklistSyncResponse>> SyncBlocklistAsync(CancellationToken cancellationToken = default)
    {
        if (!_configService.BlocklistEnabled)
        {
            return Conflict(new
            {
                message = "Blocklist is disabled. Enable blocklist enforcement before syncing rules from upstream."
            });
        }

        var result = await _syncService.SyncBlocklistAsync(force: true, cancellationToken: cancellationToken);
        var (v4, v6) = GetRuleCounts();
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
    [Authorize(Policy = Policies.AdminOnly)]
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

        var isBlocked = _syncService.IsBlocked(address);

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
        var (v4, v6) = GetRuleCounts();
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

    private (int V4Count, int V6Count) GetRuleCounts()
    {
        var tree = _syncService.IntervalTree;
        if (tree != null)
        {
            var v4 = tree.Ipv4Tree?.IntervalCount ?? 0;
            var v6 = Math.Max(0, tree.IntervalCount - v4);
            return (v4, v6);
        }

        return CountRules();
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
            if (trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal))
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
        var targetV4Val = targetV4?.ToUInt32() ?? 0;
        var targetV6Val = address.ToUInt128();

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                continue;
            }

            var trimmed = rule.Trim();
            if (trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith(';'))
            {
                continue;
            }

            if (targetV4 != null && Ipv4IntervalTree.TryParse(trimmed, out var v4Range))
            {
                if (targetV4Val >= v4Range.Start && targetV4Val <= v4Range.End)
                {
                    return trimmed;
                }
            }

            if (Ipv6IntervalTree.TryParse(trimmed, out var v6Range))
            {
                if (targetV6Val >= v6Range.Start && targetV6Val <= v6Range.End)
                {
                    return trimmed;
                }
            }
        }

        return "Active blocklist rule";
    }
}
