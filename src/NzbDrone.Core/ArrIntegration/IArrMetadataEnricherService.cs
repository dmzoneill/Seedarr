using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.ArrIntegration
{
    public interface IArrMetadataEnricherService
    {
        MediaMetadata EnrichHistoryEntry(int historyId);
        MediaMetadata FetchMetadataForRecord(ArrDownloadRecord record, ArrConnectionDefinition definition);
        MediaMetadata LookupAndEnrichByTitle(DownloadHistory history);
        void EnrichAll();
        int ReconcileAndEnrichAll();
    }
}
