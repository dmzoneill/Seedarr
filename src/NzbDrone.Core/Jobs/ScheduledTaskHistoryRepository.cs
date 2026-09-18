using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Jobs;

public class ScheduledTaskHistoryRepository : BasicRepository<ScheduledTaskHistory>, IScheduledTaskHistoryRepository
{
    public ScheduledTaskHistoryRepository(IDatabase database)
        : base(database)
    {
    }

    public List<ScheduledTaskHistory> GetByTaskId(int taskId, int limit = 50)
    {
        if (limit <= 0)
        {
            limit = 50;
        }

        return QueryWithRetry(connection =>
            connection.Query<ScheduledTaskHistory>(
                $"SELECT * FROM \"{_table}\" WHERE \"TaskId\" = @TaskId ORDER BY \"StartedAt\" DESC, \"Id\" DESC LIMIT @Limit",
                new { TaskId = taskId, Limit = limit }).ToList());
    }

    public List<ScheduledTaskHistory> GetByTypeName(string typeName, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return new List<ScheduledTaskHistory>();
        }

        if (limit <= 0)
        {
            limit = 50;
        }

        return QueryWithRetry(connection =>
        {
            var shortPattern = "%." + typeName.Trim();
            return connection.Query<ScheduledTaskHistory>(
                $"SELECT * FROM \"{_table}\" WHERE \"TypeName\" = @TypeName OR \"TypeName\" LIKE @ShortPattern ORDER BY \"StartedAt\" DESC, \"Id\" DESC LIMIT @Limit",
                new { TypeName = typeName.Trim(), ShortPattern = shortPattern, Limit = limit }).ToList();
        });
    }

    public void PurgeOldHistory(int retainCount = 100)
    {
        PurgeOldHistory(retainCount, DateTime.UtcNow.AddDays(-30));
    }

    public void PurgeOldHistory(int retainCount, DateTime olderThan)
    {
        ExecuteWithRetry(connection =>
        {
            if (olderThan > DateTime.MinValue)
            {
                while (true)
                {
                    var rows = connection.Execute(
                        $@"DELETE FROM ""{_table}""
                        WHERE ""Id"" IN (
                            SELECT ""Id"" FROM ""{_table}""
                            WHERE ""StartedAt"" < @OlderThan
                            LIMIT 500
                        )",
                        new { OlderThan = olderThan });

                    if (rows == 0)
                    {
                        break;
                    }

                    Thread.Sleep(1);
                }
            }

            if (retainCount > 0)
            {
                while (true)
                {
                    var rows = connection.Execute(
                        $@"DELETE FROM ""{_table}""
                        WHERE ""Id"" IN (
                            SELECT ""Id"" FROM (
                                SELECT ""Id"", ROW_NUMBER() OVER (PARTITION BY ""TypeName"" ORDER BY ""StartedAt"" DESC, ""Id"" DESC) AS rn
                                FROM ""{_table}""
                            ) sub
                            WHERE sub.rn > @RetainCount
                            LIMIT 500
                        )",
                        new { RetainCount = retainCount });

                    if (rows == 0)
                    {
                        break;
                    }

                    Thread.Sleep(1);
                }
            }
        });
    }
}
