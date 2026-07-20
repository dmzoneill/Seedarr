using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers;

public class IndexerDefinition : ProviderDefinition
{
    public string IndexerType { get; set; }
    public string Url { get; set; }
    public string ApiKey { get; set; }
    public string ApiPath { get; set; } = "/api";
    public bool EnableRss { get; set; } = true;
    public bool EnableSearch { get; set; } = true;
    public string Categories { get; set; }
    public int DownloadClientId { get; set; }

    public IndexerDefinition Clone() => (IndexerDefinition)MemberwiseClone();
}
