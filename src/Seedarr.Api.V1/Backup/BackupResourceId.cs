using System;
using System.Collections.Generic;
using System.IO;
using NzbDrone.Core.Backup;

namespace Seedarr.Api.V1.Backup;

public static class BackupResourceId
{
    /// <summary>
    /// Deterministic id for a backup archive file name. Stable across list mutations and sort order.
    /// </summary>
    public static int FromFileName(string fileName)
    {
        var normalized = Path.GetFileName(fileName) ?? string.Empty;
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            uint hash = offsetBasis;
            foreach (var c in normalized.ToLowerInvariant())
            {
                hash ^= c;
                hash *= prime;
            }

            var id = (int)(hash & 0x7FFFFFFF);
            return id == 0 ? 1 : id;
        }
    }

    public static bool TryResolveName(IReadOnlyList<BackupInfo> backups, int id, out string name)
    {
        name = null;
        if (id < 1)
        {
            return false;
        }

        string match = null;
        foreach (var backup in backups)
        {
            if (FromFileName(backup.Name) != id)
            {
                continue;
            }

            if (match != null && !string.Equals(match, backup.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Multiple backups share the same API id");
            }

            match = backup.Name;
        }

        if (match == null)
        {
            return false;
        }

        name = match;
        return true;
    }
}
