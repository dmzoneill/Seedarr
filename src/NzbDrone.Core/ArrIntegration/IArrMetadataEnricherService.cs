using System.Collections.Generic;
using System.Threading;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.ArrIntegration
{
    public interface IArrMetadataEnricherService
    {
        MediaMetadata EnrichHistoryEntry(int historyId);
        MediaMetadata EnrichHistoryEntry(int historyId, IReadOnlyDictionary<string, ArrHistoryRecord> cachedHistories, CancellationToken cancellationToken = default);
        Dictionary<string, ArrHistoryRecord> PreFetchDownloadHistories(CancellationToken cancellationToken = default);
        MediaMetadata FetchMetadataForRecord(ArrDownloadRecord record, ArrConnectionDefinition definition, CancellationToken cancellationToken = default);
        MediaMetadata LookupAndEnrichByTitle(DownloadHistory history, CancellationToken cancellationToken = default);
        void EnrichAll();
        int ReconcileAndEnrichAll();
    }
}
