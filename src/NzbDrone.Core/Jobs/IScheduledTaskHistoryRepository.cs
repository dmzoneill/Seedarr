using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Jobs;

public interface IScheduledTaskHistoryRepository : IBasicRepository<ScheduledTaskHistory>
{
    List<ScheduledTaskHistory> GetByTaskId(int taskId, int limit = 50);
    List<ScheduledTaskHistory> GetByTypeName(string typeName, int limit = 50);
    void PurgeOldHistory(int retainCount = 100);
    void PurgeOldHistory(int retainCount, DateTime olderThan);
}
