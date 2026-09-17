using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public class StopPolicy : IStopPolicy
{
    private readonly IConfigService _configService;
    private readonly IRandomNumberGenerator _random;
    private readonly ITagService _tagService;

    public StopPolicy(IConfigService configService, IRandomNumberGenerator random = null, ITagService tagService = null)
    {
        _configService = configService;
        _random = random ?? new RandomNumberGenerator();
        _tagService = tagService;
    }

    public HashSet<int> SelectStoppedTorrents(List<Torrent> torrents)
    {
        return SelectStoppedTorrents(
            torrents,
            _configService.UploadStoppedMinPercentage,
            _configService.UploadStoppedMaxPercentage);
    }

    public HashSet<int> SelectDownloadStoppedTorrents(List<Torrent> torrents)
    {
        return SelectStoppedTorrents(
            torrents,
            _configService.DownloadStoppedMinPercentage,
            _configService.DownloadStoppedMaxPercentage);
    }

    public HashSet<int> SelectStoppedTorrents(List<Torrent> torrents, double minPct, double maxPct)
    {
        var torrentCount = torrents.Count;
        if (maxPct <= 0 || torrentCount == 0)
        {
            return new HashSet<int>();
        }

        var eligibleIndices = new List<int>();
        for (var i = 0; i < torrentCount; i++)
        {
            if (!torrents[i].ForceStart)
            {
                eligibleIndices.Add(i);
            }
        }

        if (eligibleIndices.Count == 0)
        {
            return new HashSet<int>();
        }

        var stoppedPct = minPct + (_random.NextDouble() * (maxPct - minPct));
        var stoppedCount = (int)Math.Ceiling(eligibleIndices.Count * (stoppedPct / 100.0));
        stoppedCount = Math.Min(stoppedCount, eligibleIndices.Count - 1);

        if (stoppedCount <= 0)
        {
            return new HashSet<int>();
        }

        // Deduplicated single Fisher-Yates shuffle
        for (var j = eligibleIndices.Count - 1; j > 0; j--)
        {
            var k = _random.Next(j + 1);
            (eligibleIndices[j], eligibleIndices[k]) = (eligibleIndices[k], eligibleIndices[j]);
        }

        var stopped = new HashSet<int>();
        for (var i = 0; i < stoppedCount; i++)
        {
            stopped.Add(eligibleIndices[i]);
        }

        return stopped;
    }

    public bool ShouldStop(Torrent torrent, List<Tag> tags = null, double? globalRatioLimit = null, int? globalTimeLimitSeconds = null)
    {
        if (torrent == null || torrent.ForceStart)
        {
            return false;
        }

        var assignedTags = tags ?? GetTagsForTorrent(torrent);
        var policyTags = assignedTags?
            .Where(t => (t.MinSeedRatio.HasValue && t.MinSeedRatio.Value > 0) ||
                        (t.MinSeedTimeSeconds.HasValue && t.MinSeedTimeSeconds.Value > 0))
            .ToList();

        if (policyTags != null && policyTags.Count > 0)
        {
            return policyTags.All(tag =>
            {
                var ratioRequired = tag.MinSeedRatio.HasValue && tag.MinSeedRatio.Value > 0;
                var timeRequired = tag.MinSeedTimeSeconds.HasValue && tag.MinSeedTimeSeconds.Value > 0;

                if (ratioRequired && timeRequired)
                {
                    return torrent.Ratio >= tag.MinSeedRatio.Value ||
                           torrent.SeedingTime >= tag.MinSeedTimeSeconds.Value;
                }

                if (ratioRequired)
                {
                    return torrent.Ratio >= tag.MinSeedRatio.Value;
                }

                return torrent.SeedingTime >= tag.MinSeedTimeSeconds.Value;
            });
        }

        if (torrent.RatioLimit.HasValue && torrent.RatioLimit.Value > 0 && torrent.Ratio >= torrent.RatioLimit.Value)
        {
            return true;
        }

        if (torrent.SeedingTimeLimit.HasValue && torrent.SeedingTimeLimit.Value > 0 && torrent.SeedingTime >= torrent.SeedingTimeLimit.Value)
        {
            return true;
        }

        var effectiveGlobalRatio = globalRatioLimit ?? _configService?.GlobalSeedRatioLimit ?? 0;
        if (effectiveGlobalRatio > 0 && torrent.Ratio >= effectiveGlobalRatio)
        {
            return true;
        }

        if (globalTimeLimitSeconds.HasValue && globalTimeLimitSeconds.Value > 0 && torrent.SeedingTime >= globalTimeLimitSeconds.Value)
        {
            return true;
        }

        return false;
    }

    private List<Tag> GetTagsForTorrent(Torrent torrent)
    {
        if (_tagService == null || torrent.TagIds == null || torrent.TagIds.Count == 0)
        {
            return new List<Tag>();
        }

        var allTags = _tagService.GetAll();
        if (allTags != null && allTags.Count > 0)
        {
            var matched = allTags.Where(t => torrent.TagIds.Contains(t.Id)).ToList();
            if (matched.Count > 0)
            {
                return matched;
            }
        }

        var tags = new List<Tag>();
        foreach (var id in torrent.TagIds)
        {
            var tag = _tagService.Get(id);
            if (tag != null)
            {
                tags.Add(tag);
            }
        }

        return tags;
    }
}
