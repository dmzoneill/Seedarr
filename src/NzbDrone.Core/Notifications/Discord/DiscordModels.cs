using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.Notifications.Discord;

public static class DiscordInteractionType
{
    public const int Ping = 1;
    public const int ApplicationCommand = 2;
    public const int MessageComponent = 3;
    public const int ApplicationCommandAutocomplete = 4;
    public const int ModalSubmit = 5;
}

public static class DiscordInteractionResponseType
{
    public const int Pong = 1;
    public const int ChannelMessageWithSource = 4;
    public const int DeferredChannelMessageWithSource = 5;
    public const int DeferredUpdateMessage = 6;
    public const int UpdateMessage = 7;
    public const int Modal = 9;
}

public static class DiscordComponentType
{
    public const int ActionRow = 1;
    public const int Button = 2;
    public const int StringSelect = 3;
    public const int TextInput = 4;
    public const int UserSelect = 5;
    public const int RoleSelect = 6;
    public const int MentionableSelect = 7;
    public const int ChannelSelect = 8;
}

public static class DiscordButtonStyle
{
    public const int Primary = 1;
    public const int Secondary = 2;
    public const int Success = 3;
    public const int Danger = 4;
    public const int Link = 5;
}

public class DiscordInteraction
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("application_id")]
    public string ApplicationId { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("data")]
    public DiscordInteractionData Data { get; set; }

    [JsonPropertyName("guild_id")]
    public string GuildId { get; set; }

    [JsonPropertyName("channel_id")]
    public string ChannelId { get; set; }

    [JsonPropertyName("member")]
    public DiscordGuildMember Member { get; set; }

    [JsonPropertyName("user")]
    public DiscordUser User { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("message")]
    public DiscordMessage Message { get; set; }

    [JsonIgnore]
    public ulong? EffectiveUserId
    {
        get
        {
            if (Member?.User?.Id != null && ulong.TryParse(Member.User.Id, out var mUid))
            {
                return mUid;
            }

            if (User?.Id != null && ulong.TryParse(User.Id, out var uUid))
            {
                return uUid;
            }

            return null;
        }
    }
}

public class DiscordInteractionData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("options")]
    public List<DiscordInteractionDataOption> Options { get; set; } = new();

    [JsonPropertyName("custom_id")]
    public string CustomId { get; set; }

    [JsonPropertyName("component_type")]
    public int ComponentType { get; set; }

    [JsonPropertyName("values")]
    public List<string> Values { get; set; } = new();
}

public class DiscordInteractionDataOption
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("value")]
    public object Value { get; set; }

    [JsonPropertyName("options")]
    public List<DiscordInteractionDataOption> Options { get; set; } = new();

    public string GetStringValue()
    {
        if (Value == null)
        {
            return null;
        }

        if (Value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => element.ToString()
            };
        }

        return Value.ToString();
    }

    public int? GetIntValue()
    {
        if (Value == null)
        {
            return null;
        }

        if (Value is int i)
        {
            return i;
        }

        if (Value is long l && l is >= int.MinValue and <= int.MaxValue)
        {
            return (int)l;
        }

        if (Value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonInt))
            {
                return jsonInt;
            }

            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsedInt))
            {
                return parsedInt;
            }
        }

        if (int.TryParse(Value.ToString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public double? GetDoubleValue()
    {
        if (Value == null)
        {
            return null;
        }

        if (Value is double d)
        {
            return d;
        }

        if (Value is float f)
        {
            return f;
        }

        if (Value is int i)
        {
            return i;
        }

        if (Value is long l)
        {
            return l;
        }

        if (Value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var jsonDouble))
            {
                return jsonDouble;
            }

            if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                return parsedDouble;
            }
        }

        if (double.TryParse(Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public bool? GetBooleanValue()
    {
        if (Value == null)
        {
            return null;
        }

        if (Value is bool b)
        {
            return b;
        }

        if (Value is JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return element.GetBoolean();
            }

            if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var parsedBool))
            {
                return parsedBool;
            }
        }

        if (bool.TryParse(Value.ToString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }
}

public class DiscordInteractionResponse
{
    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiscordInteractionCallbackData Data { get; set; }

    [JsonIgnore]
    public bool Success { get; set; } = true;

    [JsonIgnore]
    public bool Handled { get; set; } = true;

    [JsonIgnore]
    public bool Authorized { get; set; } = true;

    [JsonIgnore]
    public string Command { get; set; }

    [JsonIgnore]
    public string ResponseText => Data?.Content ?? Data?.Embeds?.FirstOrDefault()?.Description;

    public static DiscordInteractionResponse Pong()
    {
        return new DiscordInteractionResponse
        {
            Type = DiscordInteractionResponseType.Pong,
            Success = true,
            Handled = true,
            Authorized = true,
        };
    }

    public static DiscordInteractionResponse ChannelMessage(
        string content = null,
        DiscordEmbed embed = null,
        List<DiscordComponent> components = null,
        bool ephemeral = false)
    {
        var data = new DiscordInteractionCallbackData
        {
            Content = content,
            Flags = ephemeral ? 64 : null,
        };

        if (embed != null)
        {
            data.Embeds = new List<DiscordEmbed> { embed };
        }

        if (components != null && components.Count > 0)
        {
            data.Components = components;
        }

        return new DiscordInteractionResponse
        {
            Type = DiscordInteractionResponseType.ChannelMessageWithSource,
            Data = data,
            Success = true,
            Handled = true,
            Authorized = true,
        };
    }

    public static DiscordInteractionResponse UpdateMessage(
        string content = null,
        DiscordEmbed embed = null,
        List<DiscordComponent> components = null)
    {
        var data = new DiscordInteractionCallbackData
        {
            Content = content,
        };

        if (embed != null)
        {
            data.Embeds = new List<DiscordEmbed> { embed };
        }

        if (components != null && components.Count > 0)
        {
            data.Components = components;
        }

        return new DiscordInteractionResponse
        {
            Type = DiscordInteractionResponseType.UpdateMessage,
            Data = data,
            Success = true,
            Handled = true,
            Authorized = true,
        };
    }

    public static DiscordInteractionResponse DeferredChannelMessage(bool ephemeral = false)
    {
        return new DiscordInteractionResponse
        {
            Type = DiscordInteractionResponseType.DeferredChannelMessageWithSource,
            Data = ephemeral ? new DiscordInteractionCallbackData { Flags = 64 } : null,
            Success = true,
            Handled = true,
            Authorized = true,
        };
    }

    public static DiscordInteractionResponse DeferredUpdateMessage()
    {
        return new DiscordInteractionResponse
        {
            Type = DiscordInteractionResponseType.DeferredUpdateMessage,
            Success = true,
            Handled = true,
            Authorized = true,
        };
    }
}

public class DiscordInteractionCallbackData
{
    [JsonPropertyName("tts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Tts { get; set; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Content { get; set; }

    [JsonPropertyName("embeds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DiscordEmbed> Embeds { get; set; }

    [JsonPropertyName("flags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Flags { get; set; }

    [JsonPropertyName("components")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DiscordComponent> Components { get; set; }
}

public class DiscordEmbed
{
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Title { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Description { get; set; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Url { get; set; }

    [JsonPropertyName("timestamp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Timestamp { get; set; }

    [JsonPropertyName("color")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Color { get; set; }

    [JsonPropertyName("footer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiscordEmbedFooter Footer { get; set; }

    [JsonPropertyName("image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiscordEmbedMedia Image { get; set; }

    [JsonPropertyName("thumbnail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiscordEmbedMedia Thumbnail { get; set; }

    [JsonPropertyName("author")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiscordEmbedAuthor Author { get; set; }

    [JsonPropertyName("fields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DiscordEmbedField> Fields { get; set; }
}

public class DiscordEmbedField
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("value")]
    public string Value { get; set; }

    [JsonPropertyName("inline")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Inline { get; set; }
}

public class DiscordEmbedFooter
{
    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("icon_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string IconUrl { get; set; }
}

public class DiscordEmbedAuthor
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Url { get; set; }

    [JsonPropertyName("icon_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string IconUrl { get; set; }
}

public class DiscordEmbedMedia
{
    [JsonPropertyName("url")]
    public string Url { get; set; }
}

public class DiscordComponent
{
    [JsonPropertyName("type")]
    public int Type { get; set; } = DiscordComponentType.ActionRow;

    [JsonPropertyName("style")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Style { get; set; }

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Label { get; set; }

    [JsonPropertyName("custom_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string CustomId { get; set; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Url { get; set; }

    [JsonPropertyName("disabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Disabled { get; set; }

    [JsonPropertyName("components")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DiscordComponent> Components { get; set; }
}

public class DiscordUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; }

    [JsonPropertyName("discriminator")]
    public string Discriminator { get; set; }

    [JsonPropertyName("global_name")]
    public string GlobalName { get; set; }

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; }

    [JsonPropertyName("bot")]
    public bool? Bot { get; set; }
}

public class DiscordGuildMember
{
    [JsonPropertyName("user")]
    public DiscordUser User { get; set; }

    [JsonPropertyName("nick")]
    public string Nick { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("joined_at")]
    public string JoinedAt { get; set; }
}

public class DiscordMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("channel_id")]
    public string ChannelId { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; }

    [JsonPropertyName("embeds")]
    public List<DiscordEmbed> Embeds { get; set; } = new();

    [JsonPropertyName("components")]
    public List<DiscordComponent> Components { get; set; } = new();
}
