using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public interface IRssRuleRepository : IBasicRepository<RssRule>
{
    IEnumerable<RssRule> GetEnabled();
}

public class RssRuleRepository : BasicRepository<RssRule>, IRssRuleRepository
{
    public RssRuleRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<RssRule> GetEnabled()
    {
        return QueryWithRetry(connection =>
            connection.Query<RssRule>(
                $"SELECT * FROM \"{_table}\" WHERE \"IsEnabled\" = @IsEnabled",
                new { IsEnabled = true }));
    }
}
