using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers;

public interface IIndexer : IProvider
{
    string IndexerType { get; }
    bool TestConnection(IndexerDefinition definition);
    byte[] FetchTorrentByHash(IndexerDefinition definition, string infoHash);
}
