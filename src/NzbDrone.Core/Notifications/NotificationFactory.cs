using NzbDrone.Common;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications;

public interface INotificationFactory : IProviderFactory<INotificationService, NotificationDefinition>
{
}

public class NotificationFactory : ProviderFactory<INotificationService, NotificationDefinition>, INotificationFactory
{
    public NotificationFactory(
        INotificationRepository providerRepository,
        IServiceFactory serviceFactory)
        : base(providerRepository, serviceFactory)
    {
    }
}
