using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface ITrackerEntryRepository : IBasicRepository<TrackerEntry>
{
    List<TrackerEntry> GetByTorrentId(int torrentId);
    void DeleteByTorrentId(int torrentId);
}
