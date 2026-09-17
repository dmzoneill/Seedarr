using System.Collections.Generic;
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
}
