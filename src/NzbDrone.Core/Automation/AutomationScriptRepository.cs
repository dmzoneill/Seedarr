using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Automation;

public class AutomationScriptRepository : BasicRepository<AutomationScript>, IAutomationScriptRepository
{
    private readonly IDatabase _database;

    public AutomationScriptRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public List<AutomationScript> GetByTrigger(AutomationTrigger trigger)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<AutomationScript>(
            $"SELECT * FROM \"{_table}\" WHERE \"Trigger\" = @Trigger AND \"IsEnabled\" = 1",
            new { Trigger = (int)trigger }).ToList();
    }

    public List<AutomationScript> GetEnabled()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<AutomationScript>(
            $"SELECT * FROM \"{_table}\" WHERE \"IsEnabled\" = 1").ToList();
    }
}
