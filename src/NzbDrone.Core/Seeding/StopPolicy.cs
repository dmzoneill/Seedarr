using System;
using System.Collections.Generic;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public class StopPolicy : IStopPolicy
{
    private readonly IConfigService _configService;
    private readonly Random _random;

    public StopPolicy(IConfigService configService, Random random = null)
    {
        _configService = configService;
        _random = random ?? Random.Shared;
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
}
