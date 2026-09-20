using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Blocklist;

public interface IPeerBlocklistSyncService
{
    BlocklistSyncMetadata Metadata { get; }

    string LastSyncStatus { get; }

    HttpStatusCode? LastSyncHttpStatus { get; }

    DateTime? NextAllowedSyncUtc { get; }

    IReadOnlyList<string> ActiveRules { get; }

    int RuleCount { get; }

    DateTime? LastCheckedUtc { get; }

    bool IsSyncAllowed(DateTime? now = null);

    Task<BlocklistSyncResult> SyncAsync(string url = null, bool force = false, CancellationToken cancellationToken = default);

    Task<BlocklistSyncResult> SyncBlocklistAsync(string url = null, bool force = false, CancellationToken cancellationToken = default);

    void ResetBackoff();

    void SetActiveRules(IEnumerable<string> rules);

    bool IsBlocked(IPAddress address);

    bool IsBlocked(string ip);
}
