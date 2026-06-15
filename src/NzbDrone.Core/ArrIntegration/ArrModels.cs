using System;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ArrIntegration;

public class ArrConnectionDefinition : ProviderDefinition
{
    public string Url { get; set; }
    public string ApiKey { get; set; }
    public string ArrType { get; set; }
    public int SyncIntervalMinutes { get; set; } = 60;
}

public class ArrDownloadRecord
{
    public string Title { get; set; }
    public string DownloadId { get; set; }
    public string InfoHash { get; set; }
    public string Indexer { get; set; }
    public long Size { get; set; }
    public DateTime Date { get; set; }
    public string DownloadClient { get; set; }
    public string OutputPath { get; set; }
}
