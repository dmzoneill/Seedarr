using System.Collections.Generic;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.ArrIntegration
{
    public interface IArrMetadataEnricherService
    {
        MediaMetadata EnrichHistoryEntry(int historyId);
        MediaMetadata EnrichHistoryEntry(int historyId, IReadOnlyDictionary<string, ArrHistoryRecord> cachedHistories);
        Dictionary<string, ArrHistoryRecord> PreFetchDownloadHistories();
        MediaMetadata FetchMetadataForRecord(ArrDownloadRecord record, ArrConnectionDefinition definition);
        MediaMetadata LookupAndEnrichByTitle(DownloadHistory history);
        void EnrichAll();
        int ReconcileAndEnrichAll();
    }
}
