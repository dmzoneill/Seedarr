using System;
using System.Collections.Generic;
using NzbDrone.Core.ArrIntegration;

namespace Seedarr.Api.V1.ArrIntegration;

internal static class ArrConnectionUpdateMerger
{
    public static ArrConnectionDefinition Merge(
        ArrConnectionDefinition existing,
        ArrConnectionDefinition incoming,
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

        if (Present(presentPropertyKeys, "url"))
        {
            merged.Url = incoming.Url;
        }

        if (Present(presentPropertyKeys, "apiKey"))
        {
            merged.ApiKey = incoming.ApiKey;
        }

        if (Present(presentPropertyKeys, "arrType"))
        {
            merged.ArrType = incoming.ArrType;
        }

        if (Present(presentPropertyKeys, "syncIntervalMinutes"))
        {
            merged.SyncIntervalMinutes = incoming.SyncIntervalMinutes;
        }

        if (Present(presentPropertyKeys, "syncEnabled"))
        {
            merged.SyncEnabled = incoming.SyncEnabled;
        }

        if (Present(presentPropertyKeys, "enableAutomaticAdd"))
        {
            merged.EnableAutomaticAdd = incoming.EnableAutomaticAdd;
        }

        if (Present(presentPropertyKeys, "webhookEnabled"))
        {
            merged.WebhookEnabled = incoming.WebhookEnabled;
        }

        if (Present(presentPropertyKeys, "webhookHost"))
        {
            merged.WebhookHost = incoming.WebhookHost;
        }

        if (Present(presentPropertyKeys, "acceptInvalidCertificates"))
        {
            merged.AcceptInvalidCertificates = incoming.AcceptInvalidCertificates;
        }

        if (Present(presentPropertyKeys, "category"))
        {
            merged.Category = incoming.Category;
        }

        if (Present(presentPropertyKeys, "savePath"))
        {
            merged.SavePath = incoming.SavePath;
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
