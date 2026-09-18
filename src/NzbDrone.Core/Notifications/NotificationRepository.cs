using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications;

public interface INotificationRepository : IProviderRepository<NotificationDefinition>
{
    IEnumerable<NotificationDefinition> GetEnabled();
}

public class NotificationRepository : ProviderRepository<NotificationDefinition>, INotificationRepository
{
    public NotificationRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<NotificationDefinition> GetEnabled()
    {
        return QueryWithRetry(connection =>
            connection.Query<NotificationDefinition>(
                $"SELECT * FROM \"{_table}\" WHERE \"Enable\" = @Enable",
                new { Enable = true }));
    }
}
