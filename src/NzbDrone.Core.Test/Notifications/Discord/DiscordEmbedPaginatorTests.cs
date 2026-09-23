using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Discord;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Notifications.Discord;

[TestFixture]
public class DiscordEmbedPaginatorTests
{
    private DiscordEmbedPaginator _paginator;

    [SetUp]
    public void SetUp()
    {
        _paginator = new DiscordEmbedPaginator();
    }

    [Test]
    public void GuardEmbed_returns_null_when_embed_is_null()
    {
        Assert.That(_paginator.GuardEmbed(null), Is.Null);
    }

    [Test]
    public void GuardEmbed_truncates_title_exceeding_256_characters()
    {
        var longTitle = new string('A', 300);
        var embed = new DiscordEmbed { Title = longTitle };

        var guarded = _paginator.GuardEmbed(embed);

        Assert.That(guarded.Title.Length, Is.EqualTo(DiscordEmbedPaginator.MaxTitleLength));
        Assert.That(guarded.Title, Does.EndWith("..."));
    }

    [Test]
    public void GuardEmbed_truncates_description_exceeding_4096_characters()
    {
        var longDesc = new string('D', 5000);
        var embed = new DiscordEmbed { Description = longDesc };

        var guarded = _paginator.GuardEmbed(embed);

        Assert.That(guarded.Description.Length, Is.EqualTo(DiscordEmbedPaginator.MaxDescriptionLength));
        Assert.That(guarded.Description, Does.EndWith("..."));
    }

    [Test]
    public void GuardEmbed_truncates_footer_and_author()
    {
        var embed = new DiscordEmbed
        {
            Footer = new DiscordEmbedFooter { Text = new string('F', 3000) },
            Author = new DiscordEmbedAuthor { Name = new string('A', 400) }
        };

        var guarded = _paginator.GuardEmbed(embed);

        Assert.That(guarded.Footer.Text.Length, Is.EqualTo(DiscordEmbedPaginator.MaxFooterTextLength));
        Assert.That(guarded.Footer.Text, Does.EndWith("..."));
        Assert.That(guarded.Author.Name.Length, Is.EqualTo(DiscordEmbedPaginator.MaxAuthorNameLength));
        Assert.That(guarded.Author.Name, Does.EndWith("..."));
    }

    [Test]
    public void GuardEmbed_limits_fields_to_maximum_of_25()
    {
        var fields = new List<DiscordEmbedField>();
        for (var i = 0; i < 30; i++)
        {
            fields.Add(new DiscordEmbedField { Name = $"Field {i}", Value = $"Value {i}" });
        }

        var embed = new DiscordEmbed { Fields = fields };
        var guarded = _paginator.GuardEmbed(embed);

        Assert.That(guarded.Fields.Count, Is.EqualTo(DiscordEmbedPaginator.MaxFields));
    }

    [Test]
    public void GuardEmbed_handles_empty_field_names_and_values()
    {
        var embed = new DiscordEmbed
        {
            Fields = new List<DiscordEmbedField>
            {
                new() { Name = "", Value = null },
                new() { Name = null, Value = "" }
            }
        };

        var guarded = _paginator.GuardEmbed(embed);

        Assert.That(guarded.Fields[0].Name, Is.EqualTo("-"));
        Assert.That(guarded.Fields[0].Value, Is.EqualTo("-"));
        Assert.That(guarded.Fields[1].Name, Is.EqualTo("-"));
        Assert.That(guarded.Fields[1].Value, Is.EqualTo("-"));
    }

    [Test]
    public void GuardEmbed_truncates_field_name_and_value_lengths()
    {
        var embed = new DiscordEmbed
        {
            Fields = new List<DiscordEmbedField>
            {
                new()
                {
                    Name = new string('N', 300),
                    Value = new string('V', 1200)
                }
            }
        };

        var guarded = _paginator.GuardEmbed(embed);

        Assert.That(guarded.Fields[0].Name.Length, Is.EqualTo(DiscordEmbedPaginator.MaxFieldNameLength));
        Assert.That(guarded.Fields[0].Name, Does.EndWith("..."));
        Assert.That(guarded.Fields[0].Value.Length, Is.EqualTo(DiscordEmbedPaginator.MaxFieldValueLength));
        Assert.That(guarded.Fields[0].Value, Does.EndWith("..."));
    }

    [Test]
    public void GuardEmbed_enforces_6000_total_characters_limit()
    {
        var fields = new List<DiscordEmbedField>();
        for (var i = 0; i < 10; i++)
        {
            fields.Add(new DiscordEmbedField
            {
                Name = $"Field {i}",
                Value = new string('X', 800)
            });
        }

        var embed = new DiscordEmbed
        {
            Title = "Large Embed Test",
            Description = new string('D', 1500),
            Fields = fields
        };

        var guarded = _paginator.GuardEmbed(embed);

        var totalChars = (guarded.Title?.Length ?? 0)
            + (guarded.Description?.Length ?? 0)
            + (guarded.Footer?.Text?.Length ?? 0)
            + (guarded.Author?.Name?.Length ?? 0)
            + (guarded.Fields?.Sum(f => (f.Name?.Length ?? 0) + (f.Value?.Length ?? 0)) ?? 0);

        Assert.That(totalChars, Is.LessThanOrEqualTo(DiscordEmbedPaginator.MaxTotalCharacters));
    }

    [Test]
    public void GuardResponse_handles_null_and_truncates_content()
    {
        Assert.That(_paginator.GuardResponse(null), Is.Null);

        var response = new DiscordInteractionResponse
        {
            Data = new DiscordInteractionCallbackData
            {
                Content = new string('C', 2500)
            }
        };

        var guarded = _paginator.GuardResponse(response);

        Assert.That(guarded.Data.Content.Length, Is.EqualTo(DiscordEmbedPaginator.MaxContentLength));
        Assert.That(guarded.Data.Content, Does.EndWith("..."));
    }

    [TestCase(1, 5, true, false)]
    [TestCase(5, 5, false, true)]
    [TestCase(3, 5, false, false)]
    [TestCase(1, 1, true, true)]
    public void CreatePaginationRow_configures_button_states_correctly(
        int page,
        int totalPages,
        bool expectPrevDisabled,
        bool expectNextDisabled)
    {
        var row = _paginator.CreatePaginationRow(page, totalPages, "test_prefix_");

        Assert.That(row.Type, Is.EqualTo(DiscordComponentType.ActionRow));
        Assert.That(row.Components.Count, Is.EqualTo(2));

        var prevBtn = row.Components[0];
        Assert.That(prevBtn.Disabled, Is.EqualTo(expectPrevDisabled));
        Assert.That(prevBtn.CustomId, Is.EqualTo($"test_prefix_{page - 1}"));

        var nextBtn = row.Components[1];
        Assert.That(nextBtn.Disabled, Is.EqualTo(expectNextDisabled));
        Assert.That(nextBtn.CustomId, Is.EqualTo($"test_prefix_{page + 1}"));
    }

    [Test]
    public void CreateTorrentsPage_returns_empty_message_when_torrents_list_is_empty_or_null()
    {
        var responseNull = _paginator.CreateTorrentsPage(null);
        Assert.That(responseNull.Data.Embeds[0].Description, Is.EqualTo("No torrents found."));

        var responseEmpty = _paginator.CreateTorrentsPage(Array.Empty<Torrent>(), filter: "downloading");
        Assert.That(responseEmpty.Data.Embeds[0].Description, Does.Contain("matching filter: 'downloading'"));
    }

    [Test]
    public void CreateTorrentsPage_pages_and_filters_torrents()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Ubuntu Linux 24.04 ISO", Status = TorrentStatus.Downloading, Progress = 0.5, DownloadSpeed = 1048576, UploadSpeed = 524288, Ratio = 0.5 },
            new() { Id = 2, Name = "Debian 12 NetInst", Status = TorrentStatus.Seeding, Progress = 1.0, DownloadSpeed = 0, UploadSpeed = 2097152, Ratio = 2.1 },
            new() { Id = 3, Name = "Arch Linux LiveCD", Status = TorrentStatus.Paused, Progress = 0.8, DownloadSpeed = 0, UploadSpeed = 0, Ratio = 0.1 },
            new() { Id = 4, Name = "Fedora Workstation 40", Status = TorrentStatus.Downloading, Progress = 0.2, DownloadSpeed = 512000, UploadSpeed = 100000, Ratio = 0.2 }
        };

        var responseAll = _paginator.CreateTorrentsPage(torrents, page: 1, pageSize: 2);
        Assert.That(responseAll.Data.Embeds[0].Fields.Count, Is.EqualTo(2));
        Assert.That(responseAll.Data.Embeds[0].Footer.Text, Does.Contain("Page 1 of 2 • Total: 4"));

        var responseSeeding = _paginator.CreateTorrentsPage(torrents, page: 1, pageSize: 10, filter: "seeding");
        Assert.That(responseSeeding.Data.Embeds[0].Fields.Count, Is.EqualTo(1));
        Assert.That(responseSeeding.Data.Embeds[0].Fields[0].Name, Does.Contain("Debian 12 NetInst"));

        var responsePaused = _paginator.CreateTorrentsPage(torrents, page: 1, pageSize: 10, filter: "paused");
        Assert.That(responsePaused.Data.Embeds[0].Fields.Count, Is.EqualTo(1));
        Assert.That(responsePaused.Data.Embeds[0].Fields[0].Name, Does.Contain("Arch Linux LiveCD"));

        var responseActive = _paginator.CreateTorrentsPage(torrents, page: 1, pageSize: 10, filter: "active");
        Assert.That(responseActive.Data.Embeds[0].Fields.Count, Is.EqualTo(3));

        var responseCompleted = _paginator.CreateTorrentsPage(torrents, page: 1, pageSize: 10, filter: "completed");
        Assert.That(responseCompleted.Data.Embeds[0].Fields.Count, Is.EqualTo(1));

        var responseSearch = _paginator.CreateTorrentsPage(torrents, page: 1, pageSize: 10, filter: "fedora");
        Assert.That(responseSearch.Data.Embeds[0].Fields.Count, Is.EqualTo(1));
        Assert.That(responseSearch.Data.Embeds[0].Fields[0].Name, Does.Contain("Fedora Workstation 40"));
    }

    [TestCase(0.0, 10, "[░░░░░░░░░░] 0%")]
    [TestCase(50.0, 10, "[█████░░░░░] 50%")]
    [TestCase(100.0, 10, "[██████████] 100%")]
    [TestCase(-10.0, 10, "[░░░░░░░░░░] 0%")]
    [TestCase(150.0, 10, "[██████████] 100%")]
    [TestCase(33.5, 10, "[███░░░░░░░] 33.5%")]
    public void BuildProgressBar_creates_accurate_bars(double percentage, int length, string expected)
    {
        var bar = _paginator.BuildProgressBar(percentage, length);
        Assert.That(bar, Is.EqualTo(expected));
    }

    [TestCase(-50L, "0 B")]
    [TestCase(500L, "500 B")]
    [TestCase(1024L, "1.00 KB")]
    [TestCase(1048576L, "1.00 MB")]
    [TestCase(1073741824L, "1.00 GB")]
    [TestCase(1099511627776L, "1.00 TB")]
    public void FormatBytes_formats_units_properly(long bytes, string expected)
    {
        Assert.That(_paginator.FormatBytes(bytes), Is.EqualTo(expected));
        Assert.That(_paginator.FormatSpeed(bytes), Is.EqualTo($"{expected}/s"));
    }

    [TestCase(null, "∞")]
    [TestCase(-5L, "∞")]
    [TestCase(0L, "∞")]
    [TestCase(9000000L, "∞")]
    [TestCase(45L, "45s")]
    [TestCase(125L, "2m 5s")]
    [TestCase(3725L, "1h 2m")]
    [TestCase(90065L, "1d 1h")]
    public void FormatEta_formats_duration_properly(long? eta, string expected)
    {
        Assert.That(_paginator.FormatEta(eta), Is.EqualTo(expected));
    }

    [TestCase(TorrentStatus.Downloading, "⬇️ [DL]")]
    [TestCase(TorrentStatus.Seeding, "⬆️ [SEED]")]
    [TestCase(TorrentStatus.Paused, "⏸️ [PAUSED]")]
    [TestCase(TorrentStatus.Queued, "⏳ [QUEUED]")]
    [TestCase(TorrentStatus.Checking, "🔍 [CHECKING]")]
    [TestCase(TorrentStatus.Error, "⚠️ [ERROR]")]
    public void GetStatusBadge_maps_status_to_badge(TorrentStatus status, string expected)
    {
        var torrent = new Torrent { Status = status };
        Assert.That(_paginator.GetStatusBadge(torrent), Is.EqualTo(expected));
    }

    [Test]
    public void GetStatusBadge_returns_unknown_when_torrent_is_null()
    {
        Assert.That(_paginator.GetStatusBadge(null), Is.EqualTo("[UNKNOWN]"));
    }
}
