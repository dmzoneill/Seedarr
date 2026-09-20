using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Indexers.Prowlarr;

public class ProwlarrSyncResult
{
    public bool Success { get; set; } = true;
    public string Message { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public int TotalFound { get; set; }
    public List<string> SyncedIndexers { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class ProwlarrIndexerSettings
{
    public int ProwlarrIndexerId { get; set; }
    public int? ProwlarrInstanceId { get; set; }
}

public interface IProwlarrIndexerSyncService
{
    ProwlarrSyncResult Sync(int? prowlarrIndexerDefinitionId = null, string baseUrl = null, string apiKey = null);
    Task<ProwlarrSyncResult> SyncAsync(int? prowlarrIndexerDefinitionId = null, string baseUrl = null, string apiKey = null, CancellationToken cancellationToken = default);
}
