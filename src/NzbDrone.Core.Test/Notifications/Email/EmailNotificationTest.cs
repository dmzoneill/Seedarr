using System;
using System.Net.Mail;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Email;

namespace NzbDrone.Core.Test.Notifications.Email;

/// <summary>
/// Test double that intercepts SmtpSend so no real SMTP connection is made.
/// </summary>
internal class TestEmailNotification : EmailNotification
{
    public MailMessage LastSentMessage { get; private set; }
    public bool SmtpSendCalled { get; private set; }
    public Exception ExceptionToThrow { get; set; }

    protected override void SmtpSend(MailMessage message)
    {
        SmtpSendCalled = true;
        LastSentMessage = message;

        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }
    }
}

[TestFixture]
public class EmailNotificationTest
{
    private EmailNotification _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new EmailNotification();
    }

    [Test]
    public void Name_should_return_email()
    {
        Assert.That(_subject.Name, Is.EqualTo("Email"));
    }

    [Test]
    public void Settings_should_default_smtp_port_to_587()
    {
        Assert.That(_subject.Settings.SmtpPort, Is.EqualTo(587));
    }

    [Test]
    public void Settings_should_default_use_tls_to_true()
    {
        Assert.That(_subject.Settings.UseTls, Is.True);
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_smtp_host_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_from_address_is_empty()
    {
        _subject.Settings.SmtpHost = "smtp.example.com";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_to_addresses_is_empty()
    {
        _subject.Settings.SmtpHost = "smtp.example.com";
        _subject.Settings.FromAddress = "from@example.com";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_smtp_host_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_smtp_host_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_smtp_host_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnHealthIssue("Disk", "Low space"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_smtp_host_set_but_to_addresses_is_empty()
    {
        _subject.Settings.SmtpHost = "smtp.example.com";
        _subject.Settings.FromAddress = "from@example.com";
        _subject.Settings.ToAddresses = "";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_to_addresses_is_whitespace_commas()
    {
        _subject.Settings.SmtpHost = "smtp.example.com";
        _subject.Settings.FromAddress = "from@example.com";
        _subject.Settings.ToAddresses = "  ,  ";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    // --- Tests using TestEmailNotification to cover SendEmail send path ---

    private TestEmailNotification CreateConfiguredSubject(string toAddresses = "to@example.com")
    {
        var subject = new TestEmailNotification();
        subject.Settings.SmtpHost = "smtp.example.com";
        subject.Settings.FromAddress = "from@example.com";
        subject.Settings.ToAddresses = toAddresses;
        return subject;
    }

    [Test]
    public void OnTorrentAdded_should_call_smtp_send_when_fully_configured()
    {
        var subject = CreateConfiguredSubject();

        subject.OnTorrentAdded("my.torrent");

        Assert.That(subject.SmtpSendCalled, Is.True);
    }

    [Test]
    public void OnTorrentAdded_should_send_with_seedarr_prefix_in_subject()
    {
        var subject = CreateConfiguredSubject();

        subject.OnTorrentAdded("my.torrent");

        Assert.That(subject.LastSentMessage.Subject, Is.EqualTo("[Seedarr] Torrent Added"));
    }

    [Test]
    public void OnTorrentAdded_should_include_torrent_name_in_body()
    {
        var subject = CreateConfiguredSubject();

        subject.OnTorrentAdded("ubuntu.torrent");

        Assert.That(subject.LastSentMessage.Body, Does.Contain("ubuntu.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_set_from_address_on_message()
    {
        var subject = CreateConfiguredSubject();

        subject.OnTorrentAdded("test.torrent");

        Assert.That(subject.LastSentMessage.From.Address, Is.EqualTo("from@example.com"));
    }

    [Test]
    public void OnTorrentAdded_should_not_send_html()
    {
        var subject = CreateConfiguredSubject();

        subject.OnTorrentAdded("test.torrent");

        Assert.That(subject.LastSentMessage.IsBodyHtml, Is.False);
    }

    [Test]
    public void OnSeedingStarted_should_send_with_correct_subject()
    {
        var subject = CreateConfiguredSubject();

        subject.OnSeedingStarted("seed.torrent");

        Assert.That(subject.SmtpSendCalled, Is.True);
        Assert.That(subject.LastSentMessage.Subject, Is.EqualTo("[Seedarr] Seeding Started"));
    }

    [Test]
    public void OnSeedingStarted_should_include_torrent_name_in_body()
    {
        var subject = CreateConfiguredSubject();

        subject.OnSeedingStarted("seed.torrent");

        Assert.That(subject.LastSentMessage.Body, Does.Contain("seed.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_send_with_correct_subject()
    {
        var subject = CreateConfiguredSubject();

        subject.OnSeedingStopped("done.torrent");

        Assert.That(subject.SmtpSendCalled, Is.True);
        Assert.That(subject.LastSentMessage.Subject, Is.EqualTo("[Seedarr] Seeding Stopped"));
    }

    [Test]
    public void OnSeedingStopped_should_include_torrent_name_in_body()
    {
        var subject = CreateConfiguredSubject();

        subject.OnSeedingStopped("done.torrent");

        Assert.That(subject.LastSentMessage.Body, Does.Contain("done.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_send_with_source_in_subject()
    {
        var subject = CreateConfiguredSubject();

        subject.OnHealthIssue("DiskSpace", "Only 1 GB remaining");

        Assert.That(subject.SmtpSendCalled, Is.True);
        Assert.That(subject.LastSentMessage.Subject, Is.EqualTo("[Seedarr] Health Issue: DiskSpace"));
    }

    [Test]
    public void OnHealthIssue_should_include_source_and_message_in_body()
    {
        var subject = CreateConfiguredSubject();

        subject.OnHealthIssue("DiskSpace", "Only 1 GB remaining");

        Assert.That(subject.LastSentMessage.Body, Does.Contain("DiskSpace"));
        Assert.That(subject.LastSentMessage.Body, Does.Contain("Only 1 GB remaining"));
    }

    [Test]
    public void SendEmail_should_add_single_recipient_to_message()
    {
        var subject = CreateConfiguredSubject("single@example.com");

        subject.OnTorrentAdded("test.torrent");

        Assert.That(subject.LastSentMessage.To.Count, Is.EqualTo(1));
        Assert.That(subject.LastSentMessage.To[0].Address, Is.EqualTo("single@example.com"));
    }

    [Test]
    public void SendEmail_should_add_multiple_recipients_when_comma_separated()
    {
        var subject = CreateConfiguredSubject("a@example.com, b@example.com, c@example.com");

        subject.OnTorrentAdded("test.torrent");

        Assert.That(subject.LastSentMessage.To.Count, Is.EqualTo(3));
    }

    [Test]
    public void SendEmail_should_trim_spaces_around_recipient_addresses()
    {
        var subject = CreateConfiguredSubject("  first@example.com  ,  second@example.com  ");

        subject.OnTorrentAdded("test.torrent");

        Assert.That(subject.LastSentMessage.To.Count, Is.EqualTo(2));
        Assert.That(subject.LastSentMessage.To[0].Address, Is.EqualTo("first@example.com"));
        Assert.That(subject.LastSentMessage.To[1].Address, Is.EqualTo("second@example.com"));
    }

    [Test]
    public void SendEmail_should_not_throw_when_smtp_send_throws()
    {
        var subject = CreateConfiguredSubject();
        subject.ExceptionToThrow = new Exception("SMTP server refused connection");

        Assert.DoesNotThrow(() => subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void SendEmail_should_not_call_smtp_when_no_valid_recipients_after_filtering()
    {
        // "  ,  " splits into all-whitespace entries which are removed → 0 recipients
        var subject = CreateConfiguredSubject("  ,  ");

        subject.OnTorrentAdded("test.torrent");

        Assert.That(subject.SmtpSendCalled, Is.False);
    }

    [Test]
    public void SendEmail_should_not_throw_when_all_recipient_addresses_are_blank()
    {
        var subject = CreateConfiguredSubject("  ,  ,  ");

        Assert.DoesNotThrow(() => subject.OnTorrentAdded("test.torrent"));
    }
}
