using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public class RssGrabHistoryRepository : BasicRepository<RssGrabHistory>, IRssGrabHistoryRepository
{
    public RssGrabHistoryRepository(IDatabase database)
        : base(database)
    {
    }

    public List<RssGrabHistory> GetHistory(int? ruleId = null, string status = null, int limit = 50, int offset = 0)
    {
        return QueryWithRetry(connection =>
        {
            var sql = new StringBuilder($"SELECT * FROM \"{_table}\" WHERE 1=1");
            var parameters = new DynamicParameters();

            if (ruleId.HasValue && ruleId.Value > 0)
            {
                sql.Append(" AND \"RuleId\" = @RuleId");
                parameters.Add("RuleId", ruleId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                sql.Append(" AND LOWER(\"Status\") = @Status");
                parameters.Add("Status", status.Trim().ToLowerInvariant());
            }

            sql.Append(" ORDER BY \"GrabTimestamp\" DESC, \"Id\" DESC");

            if (limit > 0)
            {
                sql.Append(" LIMIT @Limit");
                parameters.Add("Limit", limit);
            }

            if (offset > 0)
            {
                sql.Append(" OFFSET @Offset");
                parameters.Add("Offset", offset);
            }

            return connection.Query<RssGrabHistory>(sql.ToString(), parameters).ToList();
        });
    }

    public int GetCount(int? ruleId = null, string status = null)
    {
        return QueryWithRetry(connection =>
        {
            var sql = new StringBuilder($"SELECT COUNT(*) FROM \"{_table}\" WHERE 1=1");
            var parameters = new DynamicParameters();

            if (ruleId.HasValue && ruleId.Value > 0)
            {
                sql.Append(" AND \"RuleId\" = @RuleId");
                parameters.Add("RuleId", ruleId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                sql.Append(" AND LOWER(\"Status\") = @Status");
                parameters.Add("Status", status.Trim().ToLowerInvariant());
            }

            return connection.ExecuteScalar<int>(sql.ToString(), parameters);
        });
    }

    public void ClearHistory()
    {
        ExecuteWithRetry(connection =>
        {
            connection.Execute($"DELETE FROM \"{_table}\"");
        });
    }
}
