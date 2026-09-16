using System;
using System.Collections.Generic;
using System.Linq;
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

    public override List<NotificationDefinition> GetDefaultDefinitions()
    {
        var providers = base.GetDefaultDefinitions();
        var knownImplementations = new HashSet<string>(providers.Select(p => p.Implementation), StringComparer.OrdinalIgnoreCase);

        var additional = new[]
        {
            new NotificationDefinition { Name = "Discord", Implementation = "Discord", ConfigContract = "DiscordSettings", Enable = true },
            new NotificationDefinition { Name = "Telegram", Implementation = "Telegram", ConfigContract = "TelegramSettings", Enable = true },
            new NotificationDefinition { Name = "Gotify", Implementation = "Gotify", ConfigContract = "GotifySettings", Enable = true },
            new NotificationDefinition { Name = "Pushover", Implementation = "Pushover", ConfigContract = "PushoverSettings", Enable = true },
            new NotificationDefinition { Name = "Apprise", Implementation = "Apprise", ConfigContract = "AppriseSettings", Enable = true },
            new NotificationDefinition { Name = "Slack", Implementation = "Slack", ConfigContract = "SlackSettings", Enable = true },
            new NotificationDefinition { Name = "Email", Implementation = "Email", ConfigContract = "EmailSettings", Enable = true },
            new NotificationDefinition { Name = "CustomScript", Implementation = "CustomScript", ConfigContract = "CustomScriptSettings", Enable = true },
            new NotificationDefinition { Name = "Webhook", Implementation = "Webhook", ConfigContract = "WebhookSettings", Enable = true },
        };

        foreach (var item in additional)
        {
            if (!knownImplementations.Contains(item.Implementation))
            {
                providers.Add(item);
                knownImplementations.Add(item.Implementation);
            }
        }

        return providers;
    }
}
