using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Tags;

public class AutoTaggerRuleRepository : BasicRepository<AutoTaggerRule>, IAutoTaggerRuleRepository
{
    public AutoTaggerRuleRepository(IMainDatabase database, IEventAggregator eventAggregator = null)
        : base(database)
    {
    }

    public AutoTaggerRuleRepository(IDatabase database)
        : base(database)
    {
    }
}
