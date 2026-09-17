using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Discord;
using NzbDrone.Core.Test.TestHelpers;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Notifications.Discord;

[TestFixture]
public class DiscordInteractionHandlerTest
{
    private ITorrentService _torrentService;
    private IConfigService _configService;
    private IDiskSpaceService _diskSpaceService;
    private MockHttpMessageHandler _httpHandler;
    private HttpClient _httpClient;
    private DiscordSettings _settings;
    private DiscordEmbedPaginator _paginator;
    private DiscordInteractionHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _configService = Substitute.For<IConfigService>();
        _diskSpaceService = Substitute.For<IDiskSpaceService>();
        _httpHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_httpHandler);
        _paginator = new DiscordEmbedPaginator();

        _settings = new DiscordSettings
        {
            ApplicationId = "123456789",
            BotToken = "test_bot_token",
            AllowedUserIds = new List<ulong> { 1001, 1002 },
            AllowedGuildIds = new List<ulong> { 5001 },
        };

        _handler = new DiscordInteractionHandler(
            _torrentService,
            _configService,
            _diskSpaceService,
            _paginator,
            _settings,
            _httpClient);
    }

    // --- Ping ---

    [Test]
    public async Task HandleInteractionAsync_ping_should_return_pong()
    {
        var interaction = new DiscordInteraction
        {
            Id = "int_1",
            Type = DiscordInteractionType.Ping,
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        Assert.That(response.Type, Is.EqualTo(DiscordInteractionResponseType.Pong));
        Assert.That(response.Success, Is.True);
        Assert.That(response.Handled, Is.True);
    }

    // --- Authorization ---

    [Test]
    public void IsAuthorized_should_return_true_when_no_filters_configured()
    {
        var openSettings = new DiscordSettings();
        var handler = new DiscordInteractionHandler(_torrentService, _configService, settings: openSettings);

        var interaction = new DiscordInteraction
        {
            User = new DiscordUser { Id = "9999" },
        };

        Assert.That(handler.IsAuthorized(interaction), Is.True);
    }

    [Test]
    public void IsAuthorized_should_return_true_when_user_id_allowed()
    {
        var interaction = new DiscordInteraction
        {
            User = new DiscordUser { Id = "1001" },
        };

        Assert.That(_handler.IsAuthorized(interaction), Is.True);
    }

    [Test]
    public void IsAuthorized_should_return_true_when_guild_id_allowed()
    {
        var interaction = new DiscordInteraction
        {
            GuildId = "5001",
            User = new DiscordUser { Id = "9999" },
        };

        Assert.That(_handler.IsAuthorized(interaction), Is.True);
    }

    [Test]
    public void IsAuthorized_should_return_false_when_user_and_guild_not_allowed()
    {
        var interaction = new DiscordInteraction
        {
            GuildId = "9999",
            User = new DiscordUser { Id = "8888" },
        };

        Assert.That(_handler.IsAuthorized(interaction), Is.False);
    }

    [Test]
    public async Task HandleInteractionAsync_unauthorized_should_return_ephemeral_rejection()
    {
        var interaction = new DiscordInteraction
        {
            Id = "int_unauth",
            Type = DiscordInteractionType.ApplicationCommand,
            User = new DiscordUser { Id = "9999" },
            Data = new DiscordInteractionData { Name = "status" },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        Assert.That(response.Authorized, Is.False);
        Assert.That(response.Success, Is.False);
        Assert.That(response.Data.Content, Does.Contain("Unauthorized"));
        Assert.That(response.Data.Flags, Is.EqualTo(64));
    }

    // --- Slash Commands ---

    [Test]
    public async Task HandleInteractionAsync_status_command_should_aggregate_speeds_and_counts()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Ubuntu", Status = TorrentStatus.Downloading, DownloadSpeed = 2 * 1024 * 1024, UploadSpeed = 100 * 1024 },
            new() { Id = 2, Name = "Debian", Status = TorrentStatus.Seeding, DownloadSpeed = 0, UploadSpeed = 500 * 1024 },
            new() { Id = 3, Name = "Arch", Status = TorrentStatus.Paused, DownloadSpeed = 0, UploadSpeed = 0 },
        };

        _torrentService.GetAll().Returns(torrents);
        _configService.AlternativeSpeedEnabled.Returns(true);

        var interaction = new DiscordInteraction
        {
            Id = "int_status",
            Type = DiscordInteractionType.ApplicationCommand,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData { Name = "status" },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        Assert.That(response.Success, Is.True);
        Assert.That(response.Type, Is.EqualTo(DiscordInteractionResponseType.ChannelMessageWithSource));
        Assert.That(response.Data?.Embeds, Is.Not.Null);
        Assert.That(response.Data.Embeds.Count, Is.EqualTo(1));

        var embed = response.Data.Embeds[0];
        Assert.That(embed.Title, Is.EqualTo("Seedarr Status"));
        Assert.That(embed.Description, Does.Contain("Turtle Mode").And.Contain("Active"));
        Assert.That(embed.Description, Does.Contain("3 total"));
        Assert.That(embed.Description, Does.Contain("1 dl"));
        Assert.That(embed.Description, Does.Contain("1 seed"));
        Assert.That(embed.Description, Does.Contain("1 paused"));
    }

    [Test]
    public async Task HandleInteractionAsync_torrents_command_should_return_paginated_list_with_progress_bar()
    {
        var torrents = new List<Torrent>
        {
            new()
            {
                Id = 10,
                Name = "Fedora Linux",
                Status = TorrentStatus.Downloading,
                Progress = 0.80,
                DownloadSpeed = 5 * 1024 * 1024,
                UploadSpeed = 500 * 1024,
                Ratio = 1.25,
                Eta = 600,
            },
        };

        _torrentService.GetAll().Returns(torrents);

        var interaction = new DiscordInteraction
        {
            Id = "int_torrents",
            Type = DiscordInteractionType.ApplicationCommand,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData { Name = "torrents" },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        Assert.That(response.Success, Is.True);
        Assert.That(response.Type, Is.EqualTo(DiscordInteractionResponseType.ChannelMessageWithSource));
        Assert.That(response.Data.Embeds, Is.Not.Null);

        var embed = response.Data.Embeds[0];
        Assert.That(embed.Description, Does.Contain("Fedora Linux"));
        Assert.That(embed.Description, Does.Contain("[████████░░] 80%"));
        Assert.That(embed.Description, Does.Contain("Ratio: 1.25"));

        // Components: Previous & Next buttons
        Assert.That(response.Data.Components, Is.Not.Null);
        var row = response.Data.Components[0];
        Assert.That(row.Components.Count, Is.EqualTo(2));
        Assert.That(row.Components[0].CustomId, Does.StartWith("torrents_page_"));
        Assert.That(row.Components[1].CustomId, Does.StartWith("torrents_page_"));
    }

    [Test]
    public async Task HandleInteractionAsync_torrents_command_with_filter_should_filter_correctly()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Ubuntu Server", Status = TorrentStatus.Downloading },
            new() { Id = 2, Name = "Windows 11", Status = TorrentStatus.Seeding },
        };

        _torrentService.GetAll().Returns(torrents);

        var interaction = new DiscordInteraction
        {
            Id = "int_filter",
            Type = DiscordInteractionType.ApplicationCommand,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData
            {
                Name = "torrents",
                Options = new List<DiscordInteractionDataOption>
                {
                    new() { Name = "filter", Value = "downloading" },
                },
            },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        Assert.That(response.Success, Is.True);
        var embed = response.Data.Embeds[0];
        Assert.That(embed.Description, Does.Contain("Ubuntu Server"));
        Assert.That(embed.Description, Does.Not.Contain("Windows 11"));
    }

    [Test]
    public async Task HandleInteractionAsync_pause_command_should_pause_torrent()
    {
        var torrent = new Torrent { Id = 7, Name = "Ubuntu Desktop" };
        _torrentService.Get(7).Returns(torrent);

        var interaction = new DiscordInteraction
        {
            Id = "int_pause",
            Type = DiscordInteractionType.ApplicationCommand,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData
            {
                Name = "pause",
                Options = new List<DiscordInteractionDataOption>
                {
                    new() { Name = "id", Value = 7 },
                },
            },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        _torrentService.Received(1).Pause(7);
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data.Content, Does.Contain("Paused torrent: **Ubuntu Desktop**"));
        Assert.That(response.Data.Components, Is.Not.Null);
    }

    [Test]
    public async Task HandleInteractionAsync_pause_command_with_missing_id_should_return_usage()
    {
        var interaction = new DiscordInteraction
        {
            Id = "int_pause_err",
            Type = DiscordInteractionType.ApplicationCommand,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData { Name = "pause" },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        _torrentService.DidNotReceive().Pause(Arg.Any<int>());
        Assert.That(response.Success, Is.False);
        Assert.That(response.Data.Content, Does.Contain("Usage: `/pause <id>`"));
    }

    [Test]
    public async Task HandleInteractionAsync_resume_command_should_start_torrent()
    {
        var torrent = new Torrent { Id = 8, Name = "Arch Linux ISO" };
        _torrentService.Get(8).Returns(torrent);

        var interaction = new DiscordInteraction
        {
            Id = "int_resume",
            Type = DiscordInteractionType.ApplicationCommand,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData
            {
                Name = "resume",
                Options = new List<DiscordInteractionDataOption>
                {
                    new() { Name = "id", Value = 8 },
                },
            },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        _torrentService.Received(1).Start(8);
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data.Content, Does.Contain("Resumed torrent: **Arch Linux ISO**"));
    }

    [Test]
    public async Task HandleInteractionAsync_turtle_command_should_toggle_speed_mode()
    {
        _configService.AlternativeSpeedEnabled.Returns(false);

        var interaction = new DiscordInteraction
        {
            Id = "int_turtle",
            Type = DiscordInteractionType.ApplicationCommand,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData { Name = "turtle" },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (bool)d["AlternativeSpeedEnabled"] == true));
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data.Content, Does.Contain("Turtle mode enabled"));
    }

    [Test]
    public async Task HandleInteractionAsync_speed_command_should_update_bandwidth_limits()
    {
        var interaction = new DiscordInteraction
        {
            Id = "int_speed",
            Type = DiscordInteractionType.ApplicationCommand,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData
            {
                Name = "speed",
                Options = new List<DiscordInteractionDataOption>
                {
                    new() { Name = "down", Value = 5000 },
                    new() { Name = "up", Value = 2000 },
                },
            },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (int)d["MaxDownloadSpeedKbps"] == 5000 && (int)d["MaxUploadSpeedKbps"] == 2000));
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data.Content, Does.Contain("Bandwidth limits updated"));
    }

    // --- Message Component Buttons ---

    [Test]
    public async Task HandleInteractionAsync_component_pause_should_pause_and_return_update_message()
    {
        var torrent = new Torrent { Id = 15, Name = "Ubuntu Server" };
        _torrentService.Get(15).Returns(torrent);

        var interaction = new DiscordInteraction
        {
            Id = "btn_pause_1",
            Type = DiscordInteractionType.MessageComponent,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData
            {
                CustomId = "pause:15",
                ComponentType = DiscordComponentType.Button,
            },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        _torrentService.Received(1).Pause(15);
        Assert.That(response.Type, Is.EqualTo(DiscordInteractionResponseType.UpdateMessage));
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data.Content, Does.Contain("Paused torrent: **Ubuntu Server**"));
        Assert.That(response.Data.Components[0].Components[0].CustomId, Is.EqualTo("resume:15"));
    }

    [Test]
    public async Task HandleInteractionAsync_component_resume_should_start_and_return_update_message()
    {
        var torrent = new Torrent { Id = 16, Name = "Ubuntu Desktop" };
        _torrentService.Get(16).Returns(torrent);

        var interaction = new DiscordInteraction
        {
            Id = "btn_resume_1",
            Type = DiscordInteractionType.MessageComponent,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData
            {
                CustomId = "resume:16",
                ComponentType = DiscordComponentType.Button,
            },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        _torrentService.Received(1).Start(16);
        Assert.That(response.Type, Is.EqualTo(DiscordInteractionResponseType.UpdateMessage));
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data.Content, Does.Contain("Resumed torrent: **Ubuntu Desktop**"));
        Assert.That(response.Data.Components[0].Components[0].CustomId, Is.EqualTo("pause:16"));
    }

    [Test]
    public async Task HandleInteractionAsync_component_turtle_toggle_should_toggle_and_return_update_message()
    {
        _configService.AlternativeSpeedEnabled.Returns(false);

        var interaction = new DiscordInteraction
        {
            Id = "btn_turtle_1",
            Type = DiscordInteractionType.MessageComponent,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData
            {
                CustomId = "turtle:toggle",
                ComponentType = DiscordComponentType.Button,
            },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (bool)d["AlternativeSpeedEnabled"] == true));
        Assert.That(response.Type, Is.EqualTo(DiscordInteractionResponseType.UpdateMessage));
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data.Content, Does.Contain("Turtle mode enabled"));
    }

    [Test]
    public async Task HandleInteractionAsync_component_pagination_button_should_update_page()
    {
        var torrents = Enumerable.Range(1, 15).Select(i => new Torrent
        {
            Id = i,
            Name = $"Torrent {i}",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        }).ToList();

        _torrentService.GetAll().Returns(torrents);

        var interaction = new DiscordInteraction
        {
            Id = "btn_page_2",
            Type = DiscordInteractionType.MessageComponent,
            User = new DiscordUser { Id = "1001" },
            Data = new DiscordInteractionData
            {
                CustomId = "torrents_page_2",
                ComponentType = DiscordComponentType.Button,
            },
        };

        var response = await _handler.HandleInteractionAsync(interaction);

        Assert.That(response.Type, Is.EqualTo(DiscordInteractionResponseType.UpdateMessage));
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data.Embeds[0].Footer.Text, Does.Contain("Page 2 of 3"));
    }

    // --- DiscordEmbedPaginator Constraint & Guard Tests ---

    [Test]
    public void DiscordEmbedPaginator_GuardEmbed_should_truncate_description_exceeding_4096_chars()
    {
        var longDesc = new string('A', 5000);
        var embed = new DiscordEmbed { Description = longDesc };

        var guarded = _paginator.GuardEmbed(embed);

        Assert.That(guarded.Description.Length, Is.LessThanOrEqualTo(DiscordEmbedPaginator.MaxDescriptionLength));
        Assert.That(guarded.Description, Does.EndWith("..."));
    }

    [Test]
    public void DiscordEmbedPaginator_GuardEmbed_should_clamp_fields_to_maximum_25()
    {
        var fields = Enumerable.Range(1, 40).Select(i => new DiscordEmbedField
        {
            Name = $"Field {i}",
            Value = $"Value {i}",
        }).ToList();

        var embed = new DiscordEmbed { Fields = fields };
        var guarded = _paginator.GuardEmbed(embed);

        Assert.That(guarded.Fields.Count, Is.EqualTo(DiscordEmbedPaginator.MaxFields));
    }

    [Test]
    public void DiscordEmbedPaginator_GuardResponse_should_truncate_content_exceeding_2000_chars()
    {
        var longContent = new string('X', 3000);
        var response = new DiscordInteractionResponse
        {
            Data = new DiscordInteractionCallbackData { Content = longContent },
        };

        var guarded = _paginator.GuardResponse(response);

        Assert.That(guarded.Data.Content.Length, Is.LessThanOrEqualTo(DiscordEmbedPaginator.MaxContentLength));
        Assert.That(guarded.Data.Content, Does.EndWith("..."));
    }

    [Test]
    public void DiscordEmbedPaginator_CreatePaginationRow_should_set_correct_button_states()
    {
        var rowPage1 = _paginator.CreatePaginationRow(1, 3);
        Assert.That(rowPage1.Components[0].Disabled, Is.True, "Previous button should be disabled on page 1");
        Assert.That(rowPage1.Components[1].Disabled, Is.False, "Next button should be enabled on page 1");

        var rowPage2 = _paginator.CreatePaginationRow(2, 3);
        Assert.That(rowPage2.Components[0].Disabled, Is.False, "Previous button should be enabled on page 2");
        Assert.That(rowPage2.Components[1].Disabled, Is.False, "Next button should be enabled on page 2");

        var rowPage3 = _paginator.CreatePaginationRow(3, 3);
        Assert.That(rowPage3.Components[0].Disabled, Is.False, "Previous button should be enabled on page 3");
        Assert.That(rowPage3.Components[1].Disabled, Is.True, "Next button should be disabled on page 3");
    }

    [Test]
    public void DiscordEmbedPaginator_BuildProgressBar_should_format_correctly()
    {
        var bar0 = _paginator.BuildProgressBar(0.0);
        Assert.That(bar0, Does.Contain("[░░░░░░░░░░] 0%"));

        var bar80 = _paginator.BuildProgressBar(80.0);
        Assert.That(bar80, Does.Contain("[████████░░] 80%"));

        var bar100 = _paginator.BuildProgressBar(100.0);
        Assert.That(bar100, Does.Contain("[██████████] 100%"));
    }

    // --- Deferred Response Helpers ---

    [Test]
    public void DiscordInteractionResponse_DeferredHelpers_should_return_correct_types()
    {
        var deferredSlash = DiscordInteractionResponse.DeferredChannelMessage(ephemeral: true);
        Assert.That(deferredSlash.Type, Is.EqualTo(DiscordInteractionResponseType.DeferredChannelMessageWithSource));
        Assert.That(deferredSlash.Data?.Flags, Is.EqualTo(64));

        var deferredComponent = DiscordInteractionResponse.DeferredUpdateMessage();
        Assert.That(deferredComponent.Type, Is.EqualTo(DiscordInteractionResponseType.DeferredUpdateMessage));
    }

    [Test]
    public async Task EditOriginalResponseAsync_should_send_patch_to_discord_original_url()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");

        var data = new DiscordInteractionCallbackData { Content = "Deferred completed" };
        var success = await _handler.EditOriginalResponseAsync("app123", "token456", data);

        Assert.That(success, Is.True);
        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(1));
        var req = _httpHandler.LastRequest;
        Assert.That(req.Method, Is.EqualTo(HttpMethod.Patch));
        Assert.That(req.RequestUri.ToString(), Does.Contain("/webhooks/app123/token456/messages/@original"));
    }

    // --- JSON string deserialization ---

    [Test]
    public async Task HandleInteractionAsync_json_string_should_deserialize_and_route()
    {
        var json = "{\"type\":2,\"user\":{\"id\":\"1001\"},\"data\":{\"name\":\"turtle\"}}";
        var response = await _handler.HandleInteractionAsync(json);

        Assert.That(response.Success, Is.True);
        Assert.That(response.Handled, Is.True);
    }

    // --- NotificationPayloadBuilder Discord ActionRow Buttons ---

    [Test]
    public void NotificationPayloadBuilder_should_include_discord_action_row_buttons_for_torrent_alerts()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Big Movie Release",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
        };

        var payload = NotificationPayloadBuilder.BuildProviderPayload(
            "Discord",
            "TorrentAdded",
            torrent,
            null,
            null) as Dictionary<string, object>;

        Assert.That(payload, Is.Not.Null);
        Assert.That(payload.ContainsKey("components"), Is.True);

        var json = JsonSerializer.Serialize(payload["components"]);
        Assert.That(json, Does.Contain("pause:42"));
        Assert.That(json, Does.Contain("resume:42"));
    }
}
