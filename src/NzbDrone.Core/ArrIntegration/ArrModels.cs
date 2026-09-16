using System;
using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ArrIntegration;

public class ArrConnectionDefinition : ProviderDefinition
{
    private int _syncIntervalMinutes = 60;

    public ArrConnectionDefinition()
    {
        Enable = true;
    }

    public string Url { get; set; }
    public string ApiKey { get; set; }
    public string ArrType { get; set; }

    public int SyncIntervalMinutes
    {
        get => _syncIntervalMinutes;
        set => _syncIntervalMinutes = Math.Max(1, value);
    }

    public bool SyncEnabled { get; set; } = true;
    public bool EnableAutomaticAdd { get; set; } = true;
    public bool WebhookEnabled { get; set; } = true;
    public string WebhookHost { get; set; }
    public bool AcceptInvalidCertificates { get; set; }
    public List<int> Tags { get; set; } = new();

    public ArrConnectionDefinition Clone() => (ArrConnectionDefinition)MemberwiseClone();
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
    public string DownloadUrl { get; set; }
    public int? MediaId { get; set; }
    public string MediaType { get; set; }
    public MediaMetadata Metadata { get; set; }
}
