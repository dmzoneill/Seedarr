using System;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Email;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Notifications;

[TestFixture]
public class EmailTemplateRendererTest
{
    [Test]
    public void Render_should_render_torrent_complete_event_with_name_formatted_size_progress_bar_and_badge_color()
    {
        var torrent = new Torrent
        {
            Name = "Big Buck Bunny 4K",
            TotalSize = 2_500_000_000L, // ~2.33 GB
            Progress = 1.0,
            Ratio = 1.85,
            Status = TorrentStatus.Seeding,
            Label = "Movies"
        };

        var html = EmailTemplateRenderer.Render("Torrent Complete", "Download finished", torrent);

        Assert.That(html, Does.Contain("Big Buck Bunny 4K"));
        Assert.That(html, Does.Contain("2.33 GB"));
        Assert.That(html, Does.Contain("<div style=\"background-color:#22c55e;width:100.0%;height:8px;border-radius:4px;\">"));
        Assert.That(html, Does.Contain("#22c55e")); // Green badge color
        Assert.That(html, Does.Contain("Seedarr"));
        Assert.That(html, Does.Contain("#1a1d21")); // Dark mode background
        Assert.That(html, Does.Contain("#22272b")); // Card container
    }

    [Test]
    public void Render_should_render_health_issue_event_with_diagnostic_callout_box()
    {
        var html = EmailTemplateRenderer.Render(
            "Health Issue",
            "Disk space low",
            errorMessage: "LowDiskSpaceException: only 500MB free on /data");

        Assert.That(html, Does.Contain("Diagnostic Details"));
        Assert.That(html, Does.Contain("LowDiskSpaceException: only 500MB free on /data"));
        Assert.That(html, Does.Contain("#ef4444")); // Red badge color
    }

    [Test]
    public void Render_should_render_fallback_for_generic_payload_when_torrent_is_null()
    {
        var html = EmailTemplateRenderer.Render(
            "Application Update",
            "Seedarr has updated to version 2.0.0 successfully.",
            torrent: null);

        Assert.That(html, Does.Contain("Seedarr has updated to version 2.0.0 successfully."));
        Assert.That(html, Does.Not.Contain("Torrent Name</td>"));
        Assert.That(html, Does.Contain("Seedarr"));
    }

    [TestCase("Torrent Complete", "#22c55e")]
    [TestCase("Download Completed", "#22c55e")]
    [TestCase("Torrent Added", "#3b82f6")]
    [TestCase("Download Started", "#3b82f6")]
    [TestCase("Seed Goal Reached", "#eab308")]
    [TestCase("Torrent Paused", "#eab308")]
    [TestCase("Torrent Warning", "#eab308")]
    [TestCase("Health Issue", "#ef4444")]
    [TestCase("Torrent Failed", "#ef4444")]
    [TestCase("Torrent Error", "#ef4444")]
    public void GetBadgeColor_should_return_correct_semantic_color(string eventType, string expectedColor)
    {
        var color = EmailTemplateRenderer.GetBadgeColor(eventType);
        Assert.That(color, Is.EqualTo(expectedColor));
    }

    [Test]
    public void Render_should_html_encode_dangerous_strings()
    {
        var torrent = new Torrent
        {
            Name = "<script>alert('xss')</script>",
            Label = "<b>Important</b>",
            TotalSize = 1024L * 1024L * 500L,
            Progress = 0.5,
            Status = TorrentStatus.Downloading
        };

        var html = EmailTemplateRenderer.Render("Torrent Added", "Test", torrent);

        Assert.That(html, Does.Not.Contain("<script>"));
        Assert.That(html, Does.Contain("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;"));
    }
}
