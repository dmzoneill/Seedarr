using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers;

public class IndexerTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public int? StatusCode { get; set; }
    public System.TimeSpan? RetryAfter { get; set; }
    public Torznab.TorznabCapabilities Capabilities { get; set; }
}

public interface IIndexer : IProvider
{
    string IndexerType { get; }
    bool TestConnection(IndexerDefinition definition);
    IndexerTestResult TestConnectionDetailed(IndexerDefinition definition);
    byte[] FetchTorrentByHash(IndexerDefinition definition, string infoHash);
    System.Collections.Generic.List<ReleaseInfo> Search(IndexerDefinition definition, string query, string category = null, int offset = 0, int limit = 50);
    System.Collections.Generic.List<ReleaseInfo> Search(IndexerDefinition definition, SearchQuery searchQuery);
    System.Collections.Generic.List<ReleaseInfo> Search(IndexerDefinition definition, TorznabSearchCriteria criteria);
}
