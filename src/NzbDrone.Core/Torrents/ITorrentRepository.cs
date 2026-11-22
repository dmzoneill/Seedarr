using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface ITorrentRepository : IBasicRepository<Torrent>
{
    bool ExistsByInfoHash(string infoHash);
    Torrent GetByInfoHash(string infoHash);
    Torrent FindByInfoHash(string infoHash);
    List<Torrent> GetByInfoHashes(IEnumerable<string> infoHashes);
    int GetNextSortOrder();
    void UpdateCategoryName(string oldCategoryName, string newCategoryName);
    void ClearCategory(string categoryName);
    void UpdateTagsAndLabels(IEnumerable<Torrent> torrents);
    void UpdateTagsAndLabel(int id, List<int> tagIds, string label);
    void DeleteMany(List<int> torrentIds, bool deleteFiles = false);
    int UpdateRuntimeStats(IEnumerable<TorrentRuntimeWrite> writes);
    int TryTransitionStatuses(IEnumerable<TorrentStatusTransition> transitions);
    void ClearActivity(IEnumerable<int> torrentIds);
    void UpdateSwarmStats(int torrentId, int seeders, int leechers, double? availability);
    void UpdateExecutionStates(IEnumerable<TorrentExecutionWrite> writes);
    void UpdateSuperSeeding(int torrentId, bool superSeeding);
    void UpdateSortOrders(IEnumerable<Torrent> torrents);
    void UpdateUserColumns(Torrent torrent);
}
