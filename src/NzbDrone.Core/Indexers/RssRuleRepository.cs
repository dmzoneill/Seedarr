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
    private readonly IDatabase _database;

    public RssRuleRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public IEnumerable<RssRule> GetEnabled()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<RssRule>(
            $"SELECT * FROM \"{_table}\" WHERE \"IsEnabled\" = @IsEnabled",
            new { IsEnabled = true });
    }
}
