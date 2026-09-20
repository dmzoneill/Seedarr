using System;
using System.Text.Json;

namespace NzbDrone.Core.Indexers.Prowlarr;

public static class ProwlarrSyncMetadata
{
    public const string ConfigContractName = "ProwlarrTorznabSettings";

    public static int? GetProwlarrIndexerId(IndexerDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(definition.Settings))
        {
            try
            {
                using var doc = JsonDocument.Parse(definition.Settings);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("prowlarrIndexerId", out var idProp) && idProp.TryGetInt32(out var id))
                    {
                        return id;
                    }

                    if (doc.RootElement.TryGetProperty("ProwlarrIndexerId", out var idPropUpper) && idPropUpper.TryGetInt32(out var idUpper))
                    {
                        return idUpper;
                    }
                }
            }
            catch
            {
                // Fallback to URL parsing below
            }
        }

        // Fallback: parse from URL if it matches {host}/{id} or {host}/{id}/api
        if (!string.IsNullOrWhiteSpace(definition.Url) &&
            string.Equals(definition.ConfigContract, ConfigContractName, StringComparison.OrdinalIgnoreCase))
        {
            var cleanUrl = definition.Url.TrimEnd('/');
            if (cleanUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                cleanUrl = cleanUrl[..^4].TrimEnd('/');
            }

            var segments = cleanUrl.Split('/');
            if (segments.Length > 0 && int.TryParse(segments[^1], out var urlId))
            {
                return urlId;
            }
        }

        return null;
    }

    public static int? GetProwlarrInstanceId(IndexerDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Settings))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(definition.Settings);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("prowlarrInstanceId", out var idProp) && idProp.TryGetInt32(out var id))
                {
                    return id;
                }

                if (doc.RootElement.TryGetProperty("ProwlarrInstanceId", out var idPropUpper) && idPropUpper.TryGetInt32(out var idUpper))
                {
                    return idUpper;
                }
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }

    public static bool IsProwlarrSyncedIndexer(IndexerDefinition definition)
    {
        if (definition == null)
        {
            return false;
        }

        if (string.Equals(definition.ConfigContract, ConfigContractName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return GetProwlarrIndexerId(definition).HasValue;
    }

    public static string BuildSettingsJson(int prowlarrIndexerId, int? prowlarrInstanceId)
    {
        return JsonSerializer.Serialize(new ProwlarrIndexerSettings
        {
            ProwlarrIndexerId = prowlarrIndexerId,
            ProwlarrInstanceId = prowlarrInstanceId
        });
    }
}
