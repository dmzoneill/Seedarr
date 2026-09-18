using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public interface IRssSeenReleaseRepository : IBasicRepository<RssSeenRelease>
{
    bool IsSeen(int indexerId, string guid, string infoHash = null);
    HashSet<string> GetSeenGuids(int indexerId);
    HashSet<string> GetSeenInfoHashes(int indexerId);
    void MarkSeen(int indexerId, ReleaseInfo release, RssSeenStatus status, int? matchedRuleId = null);
    void MarkSeenBatch(int indexerId, IEnumerable<ReleaseInfo> releases, RssSeenStatus status);
    int PurgeOlderThan(TimeSpan age);
    int PurgeOlderThan(DateTime cutoff);
}
