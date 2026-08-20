namespace NzbDrone.Core.ArrIntegration
{
    public interface IArrMetadataEnricherService
    {
        MediaMetadata EnrichHistoryEntry(int historyId);
        MediaMetadata FetchMetadataForRecord(ArrDownloadRecord record, ArrConnectionDefinition definition);
        void EnrichAll();
    }
}
