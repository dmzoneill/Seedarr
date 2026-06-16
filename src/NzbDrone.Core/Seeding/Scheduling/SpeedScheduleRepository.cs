using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Seeding.Scheduling;

public interface ISpeedScheduleRepository : IBasicRepository<SpeedSchedule>
{
    IEnumerable<SpeedSchedule> GetEnabled();
}

public class SpeedScheduleRepository : BasicRepository<SpeedSchedule>, ISpeedScheduleRepository
{
    public SpeedScheduleRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<SpeedSchedule> GetEnabled()
    {
        return All().Where(s => s.IsEnabled);
    }
}
