using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public interface IRssGrabHistoryRepository : IBasicRepository<RssGrabHistory>
{
    List<RssGrabHistory> GetHistory(int? ruleId = null, string status = null, int limit = 50, int offset = 0);
    int GetCount(int? ruleId = null, string status = null);
    void ClearHistory();
}
