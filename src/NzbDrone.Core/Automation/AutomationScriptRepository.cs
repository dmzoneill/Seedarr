using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Automation;

public class AutomationScriptRepository : BasicRepository<AutomationScript>, IAutomationScriptRepository
{
    public AutomationScriptRepository(IDatabase database)
        : base(database)
    {
    }

    public List<AutomationScript> GetByTrigger(AutomationTrigger trigger)
    {
        return QueryWithRetry(connection =>
            connection.Query<AutomationScript>(
                $"SELECT * FROM \"{_table}\" WHERE \"Trigger\" = @Trigger AND \"IsEnabled\" = 1",
                new { Trigger = (int)trigger }).ToList());
    }

    public List<AutomationScript> GetEnabled()
    {
        return QueryWithRetry(connection =>
            connection.Query<AutomationScript>(
                $"SELECT * FROM \"{_table}\" WHERE \"IsEnabled\" = 1").ToList());
    }
}
