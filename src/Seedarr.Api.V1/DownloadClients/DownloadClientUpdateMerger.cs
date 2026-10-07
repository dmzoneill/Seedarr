#nullable enable
using System;
using System.Collections.Generic;
using NzbDrone.Core.DownloadClients;

namespace Seedarr.Api.V1.DownloadClients;

internal static class DownloadClientUpdateMerger
{
    public const string PasswordMask = "********"; // NOSONAR

    public static DownloadClientDefinition Merge(
        DownloadClientDefinition existing,
        DownloadClientDefinition incoming,
        IReadOnlySet<string> presentPropertyKeys)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        var merged = new DownloadClientDefinition
        {
            Id = existing.Id,
            Name = Present(presentPropertyKeys, "name") ? incoming.Name : existing.Name,
            Implementation = Present(presentPropertyKeys, "implementation") ? incoming.Implementation : existing.Implementation,
            ConfigContract = Present(presentPropertyKeys, "configContract") ? incoming.ConfigContract : existing.ConfigContract,
            Settings = Present(presentPropertyKeys, "settings") ? incoming.Settings : existing.Settings,
            Enable = Present(presentPropertyKeys, "enable") ? incoming.Enable : existing.Enable,
            Priority = Present(presentPropertyKeys, "priority") ? incoming.Priority : existing.Priority,
            ClientType = Present(presentPropertyKeys, "clientType") ? incoming.ClientType : existing.ClientType,
            Host = Present(presentPropertyKeys, "host") ? incoming.Host : existing.Host,
            Port = Present(presentPropertyKeys, "port") ? incoming.Port : existing.Port,
            UseSsl = Present(presentPropertyKeys, "useSsl") ? incoming.UseSsl : existing.UseSsl,
            UrlBase = Present(presentPropertyKeys, "urlBase") ? incoming.UrlBase : existing.UrlBase,
            Username = Present(presentPropertyKeys, "username") ? incoming.Username : existing.Username,
            Category = Present(presentPropertyKeys, "category") ? incoming.Category : existing.Category,
            Tags = Present(presentPropertyKeys, "tags")
                ? incoming.Tags ?? new List<int>()
                : new List<int>(existing.Tags ?? new List<int>()),
        };

        if (Present(presentPropertyKeys, "password"))
        {
            if (string.IsNullOrWhiteSpace(incoming.Password) || incoming.Password == PasswordMask)
            {
                merged.Password = existing.Password;
            }
            else
            {
                merged.Password = incoming.Password;
            }
        }
        else
        {
            merged.Password = existing.Password;
        }

        return merged;
    }

    private static bool Present(IReadOnlySet<string> presentPropertyKeys, string camelCaseName)
    {
        return presentPropertyKeys.Contains(camelCaseName);
    }
}
