using System;
using System.Collections.Generic;
using NzbDrone.Core.Indexers;

namespace Seedarr.Api.V1.Indexers;

internal static class RssRuleUpdateMerger
{
    public static RssRule Merge(
        RssRule existing,
        RssRule incoming,
        IReadOnlySet<string> presentPropertyKeys)
    {
        if (existing == null)
        {
            throw new ArgumentNullException(nameof(existing));
        }

        if (incoming == null)
        {
            throw new ArgumentNullException(nameof(incoming));
        }

        var merged = Copy(existing);

        if (Present(presentPropertyKeys, "name"))
        {
            merged.Name = incoming.Name;
        }

        if (Present(presentPropertyKeys, "isEnabled"))
        {
            merged.IsEnabled = incoming.IsEnabled;
        }

        if (Present(presentPropertyKeys, "mustContain"))
        {
            merged.MustContain = incoming.MustContain;
        }

        if (Present(presentPropertyKeys, "mustNotContain"))
        {
            merged.MustNotContain = incoming.MustNotContain;
        }

        if (Present(presentPropertyKeys, "minSeeders"))
        {
            merged.MinSeeders = incoming.MinSeeders;
        }

        if (Present(presentPropertyKeys, "allowUnknownSeeders"))
        {
            merged.AllowUnknownSeeders = incoming.AllowUnknownSeeders;
        }

        if (Present(presentPropertyKeys, "priority"))
        {
            merged.Priority = incoming.Priority;
        }

        if (Present(presentPropertyKeys, "minSizeBytes"))
        {
            merged.MinSizeBytes = incoming.MinSizeBytes;
        }

        if (Present(presentPropertyKeys, "maxSizeBytes"))
        {
            merged.MaxSizeBytes = incoming.MaxSizeBytes;
        }

        if (Present(presentPropertyKeys, "maxAgeDays"))
        {
            merged.MaxAgeDays = incoming.MaxAgeDays;
        }

        if (Present(presentPropertyKeys, "freeleechOnly"))
        {
            merged.FreeleechOnly = incoming.FreeleechOnly;
        }

        if (Present(presentPropertyKeys, "categoryId"))
        {
            merged.CategoryId = incoming.CategoryId;
        }

        if (Present(presentPropertyKeys, "indexerIds"))
        {
            merged.IndexerIds = incoming.IndexerIds != null ? new List<int>(incoming.IndexerIds) : new List<int>();
        }

        if (Present(presentPropertyKeys, "tags") || Present(presentPropertyKeys, "tagIds"))
        {
            merged.Tags = incoming.Tags != null ? new List<int>(incoming.Tags) : new List<int>();
        }

        if (Present(presentPropertyKeys, "allowedResolutions"))
        {
            merged.AllowedResolutions = incoming.AllowedResolutions != null
                ? new List<string>(incoming.AllowedResolutions)
                : new List<string>();
        }

        if (Present(presentPropertyKeys, "allowedSources"))
        {
            merged.AllowedSources = incoming.AllowedSources != null
                ? new List<string>(incoming.AllowedSources)
                : new List<string>();
        }

        if (Present(presentPropertyKeys, "allowedCodecs"))
        {
            merged.AllowedCodecs = incoming.AllowedCodecs != null
                ? new List<string>(incoming.AllowedCodecs)
                : new List<string>();
        }

        if (Present(presentPropertyKeys, "savePath"))
        {
            merged.SavePath = incoming.SavePath;
        }

        if (Present(presentPropertyKeys, "sequentialDownload"))
        {
            merged.SequentialDownload = incoming.SequentialDownload;
        }

        if (Present(presentPropertyKeys, "initialStatus"))
        {
            merged.InitialStatus = incoming.InitialStatus;
        }

        return merged;
    }

    private static RssRule Copy(RssRule source)
    {
        return new RssRule
        {
            Id = source.Id,
            Name = source.Name,
            IsEnabled = source.IsEnabled,
            MustContain = source.MustContain,
            MustNotContain = source.MustNotContain,
            MinSeeders = source.MinSeeders,
            AllowUnknownSeeders = source.AllowUnknownSeeders,
            Priority = source.Priority,
            MinSizeBytes = source.MinSizeBytes,
            MaxSizeBytes = source.MaxSizeBytes,
            MaxAgeDays = source.MaxAgeDays,
            FreeleechOnly = source.FreeleechOnly,
            CategoryId = source.CategoryId,
            IndexerIds = source.IndexerIds != null ? new List<int>(source.IndexerIds) : new List<int>(),
            Tags = source.Tags != null ? new List<int>(source.Tags) : new List<int>(),
            AllowedResolutions = source.AllowedResolutions != null ? new List<string>(source.AllowedResolutions) : new List<string>(),
            AllowedSources = source.AllowedSources != null ? new List<string>(source.AllowedSources) : new List<string>(),
            AllowedCodecs = source.AllowedCodecs != null ? new List<string>(source.AllowedCodecs) : new List<string>(),
            SavePath = source.SavePath,
            SequentialDownload = source.SequentialDownload,
            InitialStatus = source.InitialStatus,
        };
    }

    private static bool Present(IReadOnlySet<string> presentPropertyKeys, string camelCaseName)
    {
        return presentPropertyKeys.Contains(camelCaseName);
    }
}
