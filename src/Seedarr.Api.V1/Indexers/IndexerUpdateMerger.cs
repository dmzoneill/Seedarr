using System;
using System.Collections.Generic;
using NzbDrone.Core.Indexers;

namespace Seedarr.Api.V1.Indexers;

internal static class IndexerUpdateMerger
{
    public static IndexerDefinition Merge(
        IndexerDefinition existing,
        IndexerDefinition incoming,
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

        var merged = existing.Clone();

        if (Present(presentPropertyKeys, "name"))
        {
            merged.Name = incoming.Name;
        }

        if (Present(presentPropertyKeys, "implementation"))
        {
            merged.Implementation = incoming.Implementation;
        }

        if (Present(presentPropertyKeys, "configContract"))
        {
            merged.ConfigContract = incoming.ConfigContract;
        }

        if (Present(presentPropertyKeys, "settings"))
        {
            merged.Settings = incoming.Settings;
        }

        if (Present(presentPropertyKeys, "enable"))
        {
            merged.Enable = incoming.Enable;
        }

        if (Present(presentPropertyKeys, "priority"))
        {
            merged.Priority = incoming.Priority;
        }

        if (Present(presentPropertyKeys, "indexerType"))
        {
            merged.IndexerType = incoming.IndexerType;
            if (!Present(presentPropertyKeys, "implementation")
                && !string.Equals(existing.IndexerType, incoming.IndexerType, StringComparison.Ordinal))
            {
                merged.Implementation = null;
            }
        }

        if (Present(presentPropertyKeys, "url"))
        {
            merged.Url = incoming.Url;
        }

        if (Present(presentPropertyKeys, "apiKey"))
        {
            merged.ApiKey = incoming.ApiKey;
        }

        if (Present(presentPropertyKeys, "apiPath"))
        {
            merged.ApiPath = incoming.ApiPath;
        }

        if (Present(presentPropertyKeys, "enableRss"))
        {
            merged.EnableRss = incoming.EnableRss;
        }

        if (Present(presentPropertyKeys, "enableSearch"))
        {
            merged.EnableSearch = incoming.EnableSearch;
        }

        if (Present(presentPropertyKeys, "categories"))
        {
            merged.Categories = incoming.Categories;
        }

        if (Present(presentPropertyKeys, "downloadClientId"))
        {
            merged.DownloadClientId = incoming.DownloadClientId;
        }

        if (Present(presentPropertyKeys, "tags"))
        {
            merged.Tags = incoming.Tags ?? new List<int>();
        }

        return merged;
    }

    private static bool Present(IReadOnlySet<string> presentPropertyKeys, string camelCaseName)
    {
        return presentPropertyKeys.Contains(camelCaseName);
    }
}
