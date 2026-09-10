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
    private readonly IDatabase _database;

    public NotificationRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public IEnumerable<NotificationDefinition> GetEnabled()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<NotificationDefinition>(
            $"SELECT * FROM \"{_table}\" WHERE \"Enable\" = @Enable",
            new { Enable = true });
    }
}
