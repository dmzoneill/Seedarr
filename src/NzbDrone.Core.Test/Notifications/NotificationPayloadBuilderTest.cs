using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Notifications;

[TestFixture]
public class NotificationPayloadBuilderTest
{
    [Test]
    public void BuildProviderPayload_slack_generates_valid_block_kit_layout_for_torrent()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Big.Buck.Bunny.1080p",
            Category = "Movies",
            Status = TorrentStatus.Downloading,
            TotalSize = 1024 * 1024 * 500, // 500 MB
            Ratio = 1.25,
            Eta = 3600
        };

        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Slack",
            "Grab",
            torrent,
            null,
            null) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ContainsKey("text"), Is.True);
        Assert.That(result["text"].ToString(), Does.Contain("Big.Buck.Bunny"));
        Assert.That(result["username"], Is.EqualTo("Seedarr"));

        var blocks = result["blocks"] as List<object>;
        Assert.That(blocks, Is.Not.Null);
        Assert.That(blocks.Count, Is.EqualTo(3)); // header, section, context

        var header = blocks[0] as Dictionary<string, object>;
        Assert.That(header["type"], Is.EqualTo("header"));
        var headerText = header["text"] as Dictionary<string, object>;
        Assert.That(headerText["type"], Is.EqualTo("plain_text"));
        Assert.That(headerText["text"], Does.Contain("Seedarr [Grab]"));

        var section = blocks[1] as Dictionary<string, object>;
        Assert.That(section["type"], Is.EqualTo("section"));
        var sectionText = section["text"] as Dictionary<string, object>;
        Assert.That(sectionText["type"], Is.EqualTo("mrkdwn"));
        Assert.That(sectionText["text"].ToString(), Does.Contain("Big.Buck.Bunny"));

        var fields = section["fields"] as object[];
        Assert.That(fields, Is.Not.Null);
        Assert.That(fields.Length, Is.EqualTo(4)); // Category, Status, Size, Ratio/ETA

        var context = blocks[2] as Dictionary<string, object>;
        Assert.That(context["type"], Is.EqualTo("context"));

        var attachments = result["attachments"] as object[];
        Assert.That(attachments, Is.Not.Null);
        Assert.That(attachments.Length, Is.EqualTo(1));
        var attachment = attachments[0] as Dictionary<string, object>;
        Assert.That(attachment["color"], Is.EqualTo("#2eb886")); // Grab event -> green
    }

    [Test]
    public void BuildProviderPayload_slack_generates_generic_block_kit_for_non_torrent_event()
    {
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Slack",
            "OnApplicationUpdate",
            null,
            null,
            new { Message = "Seedarr updated to v2.0" }) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        var blocks = result["blocks"] as List<object>;
        Assert.That(blocks, Is.Not.Null);
        Assert.That(blocks.Count, Is.EqualTo(3));

        var section = blocks[1] as Dictionary<string, object>;
        var sectionText = section["text"] as Dictionary<string, object>;
        Assert.That(sectionText["text"], Does.Contain("Seedarr updated to v2.0"));
    }

    [TestCase("Grab", "#2eb886")]
    [TestCase("Complete", "#2eb886")]
    [TestCase("Success", "#2eb886")]
    [TestCase("Restored", "#2eb886")]
    [TestCase("GoalReached", "#2eb886")]
    [TestCase("Error", "#a30200")]
    [TestCase("Failed", "#a30200")]
    [TestCase("HealthIssue", "#a30200")]
    [TestCase("Info", "#3AA3E3")]
    [TestCase("RandomEvent", "#3AA3E3")]
    [TestCase(null, "#3AA3E3")]
    public void GetSlackColor_maps_events_to_correct_color_bars(string eventType, string expectedColor)
    {
        Assert.That(NotificationPayloadBuilder.GetSlackColor(eventType), Is.EqualTo(expectedColor));
    }

    [Test]
    public void BuildProviderPayload_gotify_includes_markdown_extras_and_extracted_priority()
    {
        var settings = "{\"url\":\"http://gotify.local/message\",\"priority\":8}";
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Gotify",
            "HealthIssue",
            null,
            null,
            new { Message = "Disk space low" },
            settings) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result["priority"], Is.EqualTo(8));
        Assert.That(result["title"], Is.EqualTo("Seedarr: HealthIssue"));
        Assert.That(result["message"], Does.Contain("Disk space low"));

        var extras = result["extras"] as Dictionary<string, object>;
        Assert.That(extras, Is.Not.Null);
        var display = extras["client::display"] as Dictionary<string, object>;
        Assert.That(display, Is.Not.Null);
        Assert.That(display["contentType"], Is.EqualTo("text/markdown"));
    }

    [TestCase(null, 5)]
    [TestCase("", 5)]
    [TestCase("{\"priority\": 7}", 7)]
    [TestCase("{\"Priority\": 3}", 3)]
    [TestCase("{\"priority\": \"9\"}", 9)]
    [TestCase("priority=4", 4)]
    [TestCase("Priority=6", 6)]
    [TestCase("{\"priority\": 15}", 10)] // clamped to 10
    [TestCase("{\"priority\": -2}", 1)]  // clamped to 1
    [TestCase("priority=99", 10)]        // clamped to 10
    [TestCase("priority=0", 1)]          // clamped to 1
    public void ExtractPriority_correctly_parses_and_clamps_priority(string settings, int expectedPriority)
    {
        var priority = NotificationPayloadBuilder.ExtractPriority(settings, 5);
        Assert.That(priority, Is.EqualTo(expectedPriority));
    }

    [Test]
    public void BuildProviderPayload_pushover_maps_priority_device_and_sound()
    {
        var settings = "{\"apiKey\":\"my-token\",\"userKey\":\"my-user\",\"priority\":1,\"device\":\"pixel8\",\"sound\":\"cosmic\"}";
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Pushover",
            "Grab",
            null,
            null,
            new { Message = "Torrent added" },
            settings) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result["token"], Is.EqualTo("my-token"));
        Assert.That(result["user"], Is.EqualTo("my-user"));
        Assert.That(result["priority"], Is.EqualTo(1));
        Assert.That(result["device"], Is.EqualTo("pixel8"));
        Assert.That(result["sound"], Is.EqualTo("cosmic"));
        Assert.That(result.ContainsKey("retry"), Is.False);
        Assert.That(result.ContainsKey("expire"), Is.False);
    }

    [Test]
    public void BuildProviderPayload_pushover_clamps_priority_to_minus_two_and_two()
    {
        var lowSettings = "{\"priority\": -5}";
        var lowResult = NotificationPayloadBuilder.BuildProviderPayload(
            "Pushover",
            "Grab",
            null,
            null,
            new { Message = "Low priority" },
            lowSettings) as Dictionary<string, object>;

        Assert.That(lowResult["priority"], Is.EqualTo(-2));

        var highSettings = "{\"priority\": 5}";
        var highResult = NotificationPayloadBuilder.BuildProviderPayload(
            "Pushover",
            "Grab",
            null,
            null,
            new { Message = "High priority" },
            highSettings) as Dictionary<string, object>;

        Assert.That(highResult["priority"], Is.EqualTo(2));
    }

    [Test]
    public void BuildProviderPayload_pushover_emergency_priority_includes_clamped_retry_and_expire()
    {
        var settings = "{\"priority\": 2, \"retry\": 15, \"expire\": 20000}";
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Pushover",
            "HealthIssue",
            null,
            null,
            new { Message = "Emergency alert" },
            settings) as Dictionary<string, object>;

        Assert.That(result["priority"], Is.EqualTo(2));
        Assert.That(result["retry"], Is.EqualTo(30)); // clamped to minimum 30
        Assert.That(result["expire"], Is.EqualTo(10800)); // clamped to maximum 10800
    }

    [Test]
    public void BuildProviderPayload_pushover_emergency_priority_uses_defaults_when_not_specified()
    {
        var settings = "{\"priority\": 2}";
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Pushover",
            "HealthIssue",
            null,
            null,
            new { Message = "Emergency alert" },
            settings) as Dictionary<string, object>;

        Assert.That(result["priority"], Is.EqualTo(2));
        Assert.That(result["retry"], Is.EqualTo(60));
        Assert.That(result["expire"], Is.EqualTo(3600));
    }

    [Test]
    public void BuildProviderPayload_pushover_truncates_title_to_250_and_message_to_1024()
    {
        var longEventType = new string('A', 300);
        var longMessage = new string('B', 2000);

        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Pushover",
            longEventType,
            null,
            null,
            new { Message = longMessage },
            null) as Dictionary<string, object>;

        var title = result["title"].ToString();
        var message = result["message"].ToString();

        Assert.That(title.Length, Is.LessThanOrEqualTo(250));
        Assert.That(message.Length, Is.LessThanOrEqualTo(1024));
        Assert.That(title.EndsWith("..."), Is.True);
        Assert.That(message.EndsWith("..."), Is.True);
    }

    [Test]
    public void BuildProviderPayload_discord_uses_default_branding_when_settings_empty()
    {
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Discord",
            "OnGrab",
            null,
            null,
            new { Message = "Grabbed torrent" },
            "{}") as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result["username"], Is.EqualTo("Seedarr"));
        Assert.That(result.ContainsKey("avatar_url"), Is.False);

        var embeds = result["embeds"] as object[];
        Assert.That(embeds, Is.Not.Null);
        Assert.That(embeds.Length, Is.EqualTo(1));
    }

    [Test]
    public void BuildProviderPayload_discord_extracts_custom_username_and_avatar_from_json()
    {
        var settings = "{\"username\":\"MyBot\",\"avatarUrl\":\"https://example.com/avatar.png\"}";
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Discord",
            "OnGrab",
            null,
            null,
            new { Message = "Grabbed torrent" },
            settings) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result["username"], Is.EqualTo("MyBot"));
        Assert.That(result["avatar_url"], Is.EqualTo("https://example.com/avatar.png"));
    }

    [Test]
    public void BuildProviderPayload_discord_extracts_avatar_with_snake_case()
    {
        var settings = "{\"username\":\"CustomBot\",\"avatar_url\":\"https://example.com/bot.png\"}";
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Discord",
            "OnGrab",
            null,
            null,
            new { Message = "Grabbed torrent" },
            settings) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result["username"], Is.EqualTo("CustomBot"));
        Assert.That(result["avatar_url"], Is.EqualTo("https://example.com/bot.png"));
    }

    [Test]
    public void BuildProviderPayload_discord_enforces_character_constraints_on_long_fields()
    {
        var longTorrentName = new string('A', 500);
        var longOverview = new string('B', 7000);
        var torrent = new Torrent
        {
            Id = 1,
            Name = longTorrentName,
            Category = "Movies",
            Status = TorrentStatus.Downloading,
            TotalSize = 1024 * 1024 * 100,
        };

        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Discord",
            "OnGrab",
            torrent,
            new { Overview = longOverview },
            null,
            null) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        var embeds = result["embeds"] as object[];
        Assert.That(embeds, Is.Not.Null);
        Assert.That(embeds.Length, Is.EqualTo(1));

        var embedJson = JsonSerializer.Serialize(embeds[0]);
        using var doc = JsonDocument.Parse(embedJson);
        var root = doc.RootElement;
        var title = root.GetProperty("title").GetString();
        var description = root.GetProperty("description").GetString();

        Assert.That(title.Length, Is.LessThanOrEqualTo(256));
        Assert.That(title.EndsWith("..."), Is.True);
        Assert.That(description.Length, Is.LessThanOrEqualTo(4096));
        Assert.That(title.Length + description.Length, Is.LessThanOrEqualTo(6000));
    }

    [TestCase("OnGrab", 3066993)]
    [TestCase("Grab", 3066993)]
    [TestCase("OnDownloadComplete", 3066993)]
    [TestCase("DownloadComplete", 3066993)]
    [TestCase("OnSeedGoalReached", 3066993)]
    [TestCase("SeedGoalReached", 3066993)]
    [TestCase("OnHealthRestored", 3066993)]
    [TestCase("HealthRestored", 3066993)]
    [TestCase("OnHealthIssue", 15158332)]
    [TestCase("HealthIssue", 15158332)]
    [TestCase("ArchiveExtractionFailed", 15158332)]
    [TestCase("OnManualInteractionRequired", 15105570)]
    [TestCase("ManualInteractionRequired", 15105570)]
    [TestCase("OnApplicationUpdate", 3447003)]
    [TestCase("ApplicationUpdate", 3447003)]
    [TestCase("OnMediaInspected", 3447003)]
    [TestCase("MediaInspected", 3447003)]
    [TestCase("OnTorrentDeleted", 9807270)]
    [TestCase("TorrentDeleted", 9807270)]
    [TestCase("UnknownEvent", 16765286)]
    [TestCase(null, 16765286)]
    [TestCase("", 16765286)]
    public void GetDiscordColor_maps_events_to_correct_semantic_colors(string eventType, int expectedColor)
    {
        Assert.That(NotificationPayloadBuilder.GetDiscordColor(eventType), Is.EqualTo(expectedColor));
    }

    [Test]
    public void BuildProviderPayload_gotify_elevates_priority_for_health_issues()
    {
        var settingsWithoutPriority = "{\"url\":\"http://gotify.local/message\"}";
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Gotify",
            "OnHealthIssue",
            null,
            null,
            new { Message = "Disk error" },
            settingsWithoutPriority) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result["priority"], Is.EqualTo(8));
    }

    [Test]
    public void BuildProviderPayload_gotify_elevates_low_configured_priority_for_health_issues()
    {
        var settingsWithLowPriority = "{\"url\":\"http://gotify.local/message\",\"priority\":3}";
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Gotify",
            "OnHealthIssue",
            null,
            null,
            new { Message = "Disk error" },
            settingsWithLowPriority) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result["priority"], Is.EqualTo(8));
    }

    [Test]
    public void BuildProviderPayload_gotify_preserves_higher_priority_for_health_issues()
    {
        var settingsWithHighPriority = "{\"url\":\"http://gotify.local/message\",\"priority\":9}";
        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Gotify",
            "OnHealthIssue",
            null,
            null,
            new { Message = "Disk error" },
            settingsWithHighPriority) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result["priority"], Is.EqualTo(9));
    }

    [Test]
    public void BuildProviderPayload_slack_escapes_special_characters_in_text_fields()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "<Anime> Release & Specials > Final",
            Category = "TV & Anime",
            Status = TorrentStatus.Downloading,
            TotalSize = 1024 * 1024 * 100,
        };

        var genericPayload = new { ErrorMessage = "Index < 0 & Length > 10" };

        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Slack",
            "OnGrab",
            torrent,
            null,
            genericPayload) as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        var fallbackText = result["text"].ToString();
        Assert.That(fallbackText, Does.Contain("&lt;Anime&gt; Release &amp; Specials &gt; Final"));
        Assert.That(fallbackText, Does.Contain("TV &amp; Anime"));
        Assert.That(fallbackText, Does.Contain("Index &lt; 0 &amp; Length &gt; 10"));

        var blocks = result["blocks"] as List<object>;
        Assert.That(blocks, Is.Not.Null);
        var section = blocks[1] as Dictionary<string, object>;
        var sectionText = section["text"] as Dictionary<string, object>;
        Assert.That(sectionText["text"].ToString(), Does.Contain("&lt;Anime&gt; Release &amp; Specials &gt; Final"));
        Assert.That(sectionText["text"].ToString(), Does.Contain("Index &lt; 0 &amp; Length &gt; 10"));

        var fields = section["fields"] as object[];
        var categoryField = fields[0] as Dictionary<string, object>;
        Assert.That(categoryField["text"].ToString(), Does.Contain("TV &amp; Anime"));
    }

    [Test]
    public void BuildProviderPayload_telegram_escapes_dotted_version_numbers_and_progress_percentages()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Ubuntu.24.04-LTS-Desktop.amd64-(Seedarr)!",
            Category = "Linux-ISOs.v2",
            Status = TorrentStatus.Downloading,
            Progress = 0.452, // 45.2%
        };

        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Telegram",
            "OnGrab",
            torrent,
            null,
            null,
            """{"chat_id":"987654321"}""") as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        Assert.That(result["chat_id"], Is.EqualTo("987654321"));
        Assert.That(result["parse_mode"], Is.EqualTo("Markdown"));

        var text = result["text"].ToString();
        Assert.That(text, Does.Contain(@"Ubuntu\.24\.04\-LTS\-Desktop\.amd64\-\(Seedarr\)\!"));
        Assert.That(text, Does.Contain(@"Category: Linux\-ISOs\.v2"));
        Assert.That(text, Does.Contain(@"Progress: 45\.2%"));
        Assert.That(text, Does.Contain("Status: Downloading"));
        Assert.That(result.ContainsKey("reply_markup"), Is.True);
    }

    [Test]
    public void BuildProviderPayload_telegram_escapes_special_characters_in_error_message()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "My.Release.2024",
            Status = TorrentStatus.Error,
            Progress = 1.0,
        };

        var genericPayload = new { ErrorMessage = "Error: File.part-01! (corrupt) #crc-fail" };

        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Telegram",
            "OnError",
            torrent,
            null,
            genericPayload,
            """{"chat_id":"12345"}""") as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        var text = result["text"].ToString();
        Assert.That(text, Does.Contain(@"Error: Error: File\.part\-01\! \(corrupt\) \#crc\-fail"));
        Assert.That(text, Does.Contain(@"Progress: 100\.0%"));
    }

    [Test]
    public void BuildProviderPayload_telegram_escapes_non_torrent_event_message()
    {
        var genericPayload = new { Message = "Version 2.0.1-rc1 installed! (See release-notes) #upgrade" };

        var result = NotificationPayloadBuilder.BuildProviderPayload(
            "Telegram",
            "App.Update",
            null,
            null,
            genericPayload,
            """{"chat_id":"12345"}""") as Dictionary<string, object>;

        Assert.That(result, Is.Not.Null);
        var text = result["text"].ToString();
        Assert.That(text, Does.Contain(@"*Seedarr [App\.Update]*"));
        Assert.That(text, Does.Contain(@"Version 2\.0\.1\-rc1 installed\! \(See release\-notes\) \#upgrade"));
    }

    [Test]
    public void InterpolateTemplate_supports_all_required_tokens()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Inception.2010.1080p",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Category = "Sci-Fi",
            Status = TorrentStatus.Downloading,
            TotalSize = 1024L * 1024L * 1536L,
            Downloaded = 1024L * 1024L * 768L,
            Uploaded = 1024L * 1024L * 256L,
            Ratio = 0.33,
            Progress = 0.50,
            DownloadSpeed = 1024L * 1024L * 5,
            UploadSpeed = 1024L * 1024L * 1,
            Eta = 3600,
            SavePath = "/downloads/complete/Inception",
        };

        var meta = new
        {
            Title = "Inception",
            Year = 2010,
            Overview = "A thief who steals corporate secrets through the use of dream-sharing technology.",
        };

        var genericPayload = new
        {
            Message = "Download started successfully",
            InstanceName = "Seedarr-Custom",
            Timestamp = "2026-09-18T19:00:00Z",
        };

        var template = "Event:{EventType}|Instance:{InstanceName}|Time:{Timestamp}|Id:{Torrent.Id}|Name:{Torrent.Name}|Hash:{Torrent.InfoHash}|Cat:{Torrent.Category}|Status:{Torrent.Status}|SizeFormatted:{Torrent.SizeFormatted}|Size:{Torrent.TotalSize}|DL:{Torrent.Downloaded}|UL:{Torrent.Uploaded}|Ratio:{Torrent.Ratio}|Prog:{Torrent.Progress}|ProgPct:{Torrent.ProgressPercent}|DLSpeed:{Torrent.DownloadSpeedFormatted}|ULSpeed:{Torrent.UploadSpeedFormatted}|Eta:{Torrent.EtaString}|Path:{Torrent.SavePath}|Title:{Media.Title}|Year:{Media.Year}|Overview:{Media.Overview}|Msg:{Message}";

        var result = NotificationPayloadBuilder.InterpolateTemplate(
            template,
            "OnGrab",
            torrent,
            meta,
            genericPayload);

        Assert.That(result, Does.Contain("Event:OnGrab"));
        Assert.That(result, Does.Contain("Instance:Seedarr-Custom"));
        Assert.That(result, Does.Contain("Time:2026-09-18T19:00:00Z"));
        Assert.That(result, Does.Contain("Id:42"));
        Assert.That(result, Does.Contain("Name:Inception.2010.1080p"));
        Assert.That(result, Does.Contain("Hash:0123456789abcdef0123456789abcdef01234567"));
        Assert.That(result, Does.Contain("Cat:Sci-Fi"));
        Assert.That(result, Does.Contain("Status:Downloading"));
        Assert.That(result, Does.Contain("SizeFormatted:1.50 GB"));
        Assert.That(result, Does.Contain("Size:1610612736"));
        Assert.That(result, Does.Contain("DL:805306368"));
        Assert.That(result, Does.Contain("UL:268435456"));
        Assert.That(result, Does.Contain("Ratio:0.33"));
        Assert.That(result, Does.Contain("Prog:0.5"));
        Assert.That(result, Does.Contain("ProgPct:50"));
        Assert.That(result, Does.Contain("DLSpeed:5.00 MB/s"));
        Assert.That(result, Does.Contain("ULSpeed:1.00 MB/s"));
        Assert.That(result, Does.Contain("Eta:01:00:00"));
        Assert.That(result, Does.Contain("Path:/downloads/complete/Inception"));
        Assert.That(result, Does.Contain("Title:Inception"));
        Assert.That(result, Does.Contain("Year:2010"));
        Assert.That(result, Does.Contain("Overview:A thief who steals corporate secrets"));
        Assert.That(result, Does.Contain("Msg:Download started successfully"));
    }

    [Test]
    public void InterpolateTemplate_safely_escapes_quotes_and_backslashes_in_json_templates()
    {
        var torrent = new Torrent
        {
            Id = 99,
            Name = "Show.S01E02.\"Special.Cut\".1080p.mkv",
            SavePath = @"D:\Torrents\Complete\Show.S01E02",
            TotalSize = 1024L * 1024L * 500L,
            Ratio = 1.25,
            Progress = 0.75,
        };

        var template = "{\"event\": \"{EventType}\", \"name\": \"{Torrent.Name}\", \"path\": \"{Torrent.SavePath}\", \"size\": \"{Torrent.SizeFormatted}\", \"ratio\": {Torrent.Ratio}, \"progress\": {Torrent.Progress}}";

        var result = NotificationPayloadBuilder.InterpolateTemplate(
            template,
            "OnDownloadComplete",
            torrent);

        Assert.That(result, Is.Not.Null);

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("event").GetString(), Is.EqualTo("OnDownloadComplete"));
        Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("Show.S01E02.\"Special.Cut\".1080p.mkv"));
        Assert.That(root.GetProperty("path").GetString(), Is.EqualTo(@"D:\Torrents\Complete\Show.S01E02"));
        Assert.That(root.GetProperty("size").GetString(), Is.EqualTo("500.00 MB"));
        Assert.That(root.GetProperty("ratio").GetDouble(), Is.EqualTo(1.25));
        Assert.That(root.GetProperty("progress").GetDouble(), Is.EqualTo(0.75));
    }

    [Test]
    public void InterpolateTemplate_does_not_escape_strings_for_plain_text_templates()
    {
        var torrent = new Torrent
        {
            Name = "Movie.\"Director's.Cut\".2024",
            SavePath = @"D:\Torrents\Movies",
        };

        var template = "Torrent: {Torrent.Name} stored at {Torrent.SavePath}";
        var result = NotificationPayloadBuilder.InterpolateTemplate(template, "OnGrab", torrent);

        Assert.That(result, Is.EqualTo(@"Torrent: Movie.""Director's.Cut"".2024 stored at D:\Torrents\Movies"));
    }

    [Test]
    public void ResolveCustomHeaders_injects_basic_auth_when_username_and_password_present()
    {
        var settings = "{\"url\":\"https://example.com/webhook\",\"username\":\"admin\",\"password\":\"password123\"}";
        var headers = NotificationPayloadBuilder.ResolveCustomHeaders("Webhook", settings);

        Assert.That(headers, Is.Not.Null);
        using var doc = JsonDocument.Parse(headers);
        var root = doc.RootElement;
        Assert.That(root.TryGetProperty("Authorization", out var authProp), Is.True);
        Assert.That(authProp.GetString(), Is.EqualTo("Basic YWRtaW46cGFzc3dvcmQxMjM="));
    }

    [Test]
    public void ResolveCustomHeaders_preserves_existing_authorization_header()
    {
        var settings = "{\"url\":\"https://example.com/webhook\",\"headers\":{\"Authorization\":\"Bearer secret-jwt-token\"},\"username\":\"admin\",\"password\":\"password123\"}";
        var headers = NotificationPayloadBuilder.ResolveCustomHeaders("Webhook", settings);

        Assert.That(headers, Is.Not.Null);
        using var doc = JsonDocument.Parse(headers);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("Authorization").GetString(), Is.EqualTo("Bearer secret-jwt-token"));
    }

    [Test]
    public void ResolveCustomHeaders_injects_basic_auth_alongside_custom_headers()
    {
        var settings = "{\"url\":\"https://example.com/webhook\",\"headers\":{\"X-App\":\"Seedarr\",\"X-Environment\":\"prod\"},\"username\":\"user\",\"password\":\"pass\"}";
        var headers = NotificationPayloadBuilder.ResolveCustomHeaders("Webhook", settings);

        Assert.That(headers, Is.Not.Null);
        using var doc = JsonDocument.Parse(headers);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("X-App").GetString(), Is.EqualTo("Seedarr"));
        Assert.That(root.GetProperty("X-Environment").GetString(), Is.EqualTo("prod"));
        Assert.That(root.GetProperty("Authorization").GetString(), Is.EqualTo("Basic dXNlcjpwYXNz"));
    }

    [Test]
    public void BuildProviderPayload_webhook_returns_interpolated_template_when_configured()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test.Movie.2024",
            TotalSize = 1024L * 1024L * 100L,
            Ratio = 1.0,
        };

        var settings = "{\"url\": \"https://example.com/webhook\", \"payloadTemplate\": \"{\\\"event\\\": \\\"{EventType}\\\", \\\"title\\\": \\\"{Torrent.Name}\\\"}\"}";

        var payload = NotificationPayloadBuilder.BuildProviderPayload(
            "Webhook",
            "OnGrab",
            torrent,
            null,
            new { generic = true },
            settings);

        Assert.That(payload, Is.InstanceOf<string>());
        var jsonStr = (string)payload;
        using var doc = JsonDocument.Parse(jsonStr);
        Assert.That(doc.RootElement.GetProperty("event").GetString(), Is.EqualTo("OnGrab"));
        Assert.That(doc.RootElement.GetProperty("title").GetString(), Is.EqualTo("Test.Movie.2024"));
    }

    [Test]
    public void BuildProviderPayload_webhook_falls_back_to_genericPayload_when_no_template()
    {
        var genericPayload = new { eventType = "OnGrab", name = "Test" };
        var settings = "{\"url\": \"https://example.com/webhook\"}";

        var payload = NotificationPayloadBuilder.BuildProviderPayload(
            "Webhook",
            "OnGrab",
            null,
            null,
            genericPayload,
            settings);

        Assert.That(payload, Is.SameAs(genericPayload));
    }

    [TestCase("{\"method\":\"PUT\"}", "PUT")]
    [TestCase("{\"method\":\"put\"}", "PUT")]
    [TestCase("{\"httpMethod\":\"PUT\"}", "PUT")]
    [TestCase("method=PUT", "PUT")]
    [TestCase("{\"method\":\"POST\"}", "POST")]
    [TestCase("{\"method\":\"post\"}", "POST")]
    [TestCase("{\"url\":\"http://test\"}", "POST")]
    [TestCase(null, "POST")]
    [TestCase("", "POST")]
    public void ResolveHttpMethod_resolves_put_vs_post(string settings, string expectedMethod)
    {
        var method = NotificationPayloadBuilder.ResolveHttpMethod("Webhook", settings);
        Assert.That(method.Method, Is.EqualTo(expectedMethod));
    }
}
