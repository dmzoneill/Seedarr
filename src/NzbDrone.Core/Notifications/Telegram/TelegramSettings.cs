using System.Collections.Generic;

namespace NzbDrone.Core.Notifications.Telegram;

public class TelegramSettings
{
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
    public List<long> AllowedChatIds { get; set; } = new();
}
