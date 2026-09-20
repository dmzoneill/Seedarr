using System;
using System.Linq;
using System.Net.Mail;
using System.Net.Security;
using System.Threading.Tasks;
using MailKit.Security;
using MimeKit;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;
using SmtpStatusCode = System.Net.Mail.SmtpStatusCode;

namespace NzbDrone.Core.Test.Notifications;

[TestFixture]
public class EmailNotificationSenderTest
{
    private const string ValidPublicHost = "93.184.216.34";

    [Test]
    public void SendEmailNotification_should_throw_when_settings_is_null_or_whitespace()
    {
        Assert.Throws<ArgumentException>(() =>
            EmailNotificationSender.SendEmailNotification("", "Test", null, null, null));
    }

    [Test]
    public void SendEmailNotification_should_throw_when_to_address_is_missing()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"from\":\"test@example.com\"}}";
        Assert.Throws<InvalidOperationException>(() =>
            EmailNotificationSender.SendEmailNotification(settings, "Test", null, null, null));
    }

    [Test]
    public void SendEmailNotification_should_throw_when_host_is_missing()
    {
        var settings = "{\"to\":\"recipient@example.com\",\"port\":587,\"from\":\"test@example.com\"}";
        var ex = Assert.Throws<ArgumentException>(() =>
            EmailNotificationSender.SendEmailNotification(settings, "Test", null, null, null));
        Assert.That(ex.Message, Does.Contain("SMTP host is required"));
    }

    [TestCase("127.0.0.1")]
    [TestCase("localhost")]
    [TestCase("sub.localhost")]
    [TestCase("10.0.0.1")]
    [TestCase("192.168.1.1")]
    [TestCase("172.16.0.1")]
    [TestCase("169.254.169.254")]
    [TestCase("metadata.google.internal")]
    [TestCase("instance-data")]
    public void SendEmailNotification_should_throw_when_host_is_unsafe(string unsafeHost)
    {
        var settings = $"{{\"host\":\"{unsafeHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var ex = Assert.Throws<ArgumentException>(() =>
            EmailNotificationSender.SendEmailNotification(settings, "Test", null, null, null));
        Assert.That(ex.Message, Does.Contain("is not permitted"));
    }

    [TestCase(21)]
    [TestCase(22)]
    [TestCase(80)]
    [TestCase(443)]
    [TestCase(8080)]
    public void SendEmailNotification_should_throw_when_port_is_not_allowed(int prohibitedPort)
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":{prohibitedPort},\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var ex = Assert.Throws<ArgumentException>(() =>
            EmailNotificationSender.SendEmailNotification(settings, "Test", null, null, null));
        Assert.That(ex.Message, Does.Contain("port"));
        Assert.That(ex.Message, Does.Contain("is not permitted"));
    }

    [TestCase(25)]
    [TestCase(465)]
    [TestCase(587)]
    [TestCase(2525)]
    public void SendEmailNotification_should_call_smtpSender_when_valid(int allowedPort)
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":{allowedPort},\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var senderCalled = false;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Test",
            null,
            null,
            "Test message",
            (client, message) =>
            {
                senderCalled = true;
                Assert.That(message.To[0].Address, Is.EqualTo("recipient@example.com"));
                Assert.That(message.From.Address, Is.EqualTo("test@example.com"));
                Assert.That(client.Host, Is.EqualTo(ValidPublicHost));
                Assert.That(client.Port, Is.EqualTo(allowedPort));
            });

        Assert.That(senderCalled, Is.True);
    }

    [Test]
    public void SendEmailNotification_should_add_multiple_recipients_separated_by_commas_and_semicolons()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"user1@example.com, user2@example.com; user3@example.com\",\"from\":\"test@example.com\"}}";
        MailMessage capturedMessage = null;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Test",
            null,
            null,
            "Test message",
            (client, message) =>
            {
                capturedMessage = message;
            });

        Assert.That(capturedMessage, Is.Not.Null);
        Assert.That(capturedMessage.To.Count, Is.EqualTo(3));
        Assert.That(capturedMessage.To[0].Address, Is.EqualTo("user1@example.com"));
        Assert.That(capturedMessage.To[1].Address, Is.EqualTo("user2@example.com"));
        Assert.That(capturedMessage.To[2].Address, Is.EqualTo("user3@example.com"));
    }

    [Test]
    public void SendEmailNotification_should_skip_invalid_recipient_address_and_send_to_valid_recipients()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"valid1@example.com; not-an-email, valid2@example.com\",\"from\":\"test@example.com\"}}";
        MailMessage capturedMessage = null;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Test",
            null,
            null,
            "Test message",
            (client, message) =>
            {
                capturedMessage = message;
            });

        Assert.That(capturedMessage, Is.Not.Null);
        Assert.That(capturedMessage.To.Count, Is.EqualTo(2));
        Assert.That(capturedMessage.To[0].Address, Is.EqualTo("valid1@example.com"));
        Assert.That(capturedMessage.To[1].Address, Is.EqualTo("valid2@example.com"));
    }

    [Test]
    public void SendEmailNotification_should_throw_when_all_recipient_addresses_are_invalid()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"not-an-email; also-not-an-email\",\"from\":\"test@example.com\"}}";
        Assert.Throws<FormatException>(() =>
            EmailNotificationSender.SendEmailNotification(settings, "Test", null, null, "Test message"));
    }

    [TestCase(SmtpStatusCode.MailboxBusy)] // 450
    [TestCase(SmtpStatusCode.ServiceNotAvailable)] // 421
    [TestCase(SmtpStatusCode.LocalErrorInProcessing)] // 451
    [TestCase(SmtpStatusCode.InsufficientStorage)] // 452
    public void SendEmailNotification_should_retry_transient_smtp_error_and_succeed(SmtpStatusCode statusCode)
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var callCount = 0;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Test",
            null,
            null,
            "Test message",
            (client, message) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new SmtpException(statusCode, $"Transient error: {(int)statusCode}");
                }
            },
            retryDelays: new[] { TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1) });

        Assert.That(callCount, Is.EqualTo(2));
    }

    [TestCase(SmtpStatusCode.MailboxUnavailable)] // 550
    [TestCase(SmtpStatusCode.SyntaxError)] // 501
    [TestCase(SmtpStatusCode.CommandUnrecognized)] // 500
    public void SendEmailNotification_should_fail_immediately_on_terminal_smtp_error(SmtpStatusCode statusCode)
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var callCount = 0;

        var ex = Assert.Throws<SmtpException>(() =>
        {
            EmailNotificationSender.SendEmailNotification(
                settings,
                "Test",
                null,
                null,
                "Test message",
                (client, message) =>
                {
                    callCount++;
                    throw new SmtpException(statusCode, $"Terminal error: {(int)statusCode}");
                },
                retryDelays: new[] { TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1) });
        });

        Assert.That(callCount, Is.EqualTo(1));
        Assert.That(ex.StatusCode, Is.EqualTo(statusCode));
    }

    [Test]
    public void SendEmailNotification_should_throw_when_all_retries_exhausted()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var callCount = 0;

        var ex = Assert.Throws<SmtpException>(() =>
        {
            EmailNotificationSender.SendEmailNotification(
                settings,
                "Test",
                null,
                null,
                "Test message",
                (client, message) =>
                {
                    callCount++;
                    throw new SmtpException(SmtpStatusCode.ServiceNotAvailable, "421 Service unavailable");
                },
                retryDelays: new[] { TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1) });
        });

        Assert.That(callCount, Is.EqualTo(4));
        Assert.That(ex.StatusCode, Is.EqualTo(SmtpStatusCode.ServiceNotAvailable));
    }

    [Test]
    public async Task SendEmailNotificationAsync_should_retry_transient_error_and_succeed()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var callCount = 0;

        await EmailNotificationSender.SendEmailNotificationAsync(
            settings,
            "Test",
            null,
            null,
            "Test message",
            (client, message, ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new SmtpException(SmtpStatusCode.MailboxBusy, "450 Greylisted");
                }

                return Task.CompletedTask;
            },
            retryDelays: new[] { TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1) });

        Assert.That(callCount, Is.EqualTo(2));
    }

    [Test]
    public void SendEmailNotification_should_add_plain_text_and_html_alternate_views()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        MailMessage capturedMessage = null;
        var viewCount = 0;
        string plainType = null;
        string htmlType = null;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Torrent Complete",
            null,
            null,
            "Download finished",
            (client, message) =>
            {
                capturedMessage = message;
                viewCount = message.AlternateViews.Count;
                if (viewCount >= 2)
                {
                    plainType = message.AlternateViews[0].ContentType.MediaType;
                    htmlType = message.AlternateViews[1].ContentType.MediaType;
                }
            });

        Assert.That(capturedMessage, Is.Not.Null);
        Assert.That(viewCount, Is.EqualTo(2));
        Assert.That(plainType, Is.EqualTo("text/plain"));
        Assert.That(htmlType, Is.EqualTo("text/html"));
    }

    [Test]
    public void SendEmailNotification_should_add_anti_spam_and_automated_rfc_headers()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        MailMessage capturedMessage = null;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Test",
            null,
            null,
            "Test message",
            (client, message) =>
            {
                capturedMessage = message;
            });

        Assert.That(capturedMessage, Is.Not.Null);
        Assert.That(capturedMessage.Headers["Auto-Submitted"], Is.EqualTo("auto-generated"));
        Assert.That(capturedMessage.Headers["X-Auto-Response-Suppress"], Is.EqualTo("All"));
        Assert.That(capturedMessage.Headers["Precedence"], Is.EqualTo("bulk"));
    }

    [TestCase("{\"useSsl\":\"true\"}", true)]
    [TestCase("{\"useSsl\":\"false\"}", false)]
    [TestCase("{\"ssl\":\"true\"}", true)]
    [TestCase("{\"ssl\":\"false\"}", false)]
    [TestCase("{\"useSsl\":true}", true)]
    [TestCase("{\"useSsl\":false}", false)]
    [TestCase("{\"ssl\":true}", true)]
    [TestCase("{\"ssl\":false}", false)]
    [TestCase("{\"useTls\":\"true\"}", true)]
    [TestCase("{\"useTls\":\"false\"}", false)]
    [TestCase("{\"requireTls\":\"true\"}", true)]
    [TestCase("{\"requireTls\":\"false\"}", false)]
    public void SendEmailNotification_should_parse_ssl_settings_including_string_booleans(string sslJsonFragment, bool expectedSsl)
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\",{sslJsonFragment.TrimStart('{')}";
        var capturedSsl = !expectedSsl;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Test",
            null,
            null,
            "Test message",
            (client, message) =>
            {
                capturedSsl = client.EnableSsl;
            });

        Assert.That(capturedSsl, Is.EqualTo(expectedSsl));
    }

    [TestCase("ssl=true", true)]
    [TestCase("ssl=false", false)]
    [TestCase("usessl=true", true)]
    [TestCase("usessl=false", false)]
    [TestCase("usetls=true", true)]
    [TestCase("usetls=false", false)]
    public void SendEmailNotification_should_parse_query_string_ssl_settings(string qsFragment, bool expectedSsl)
    {
        var settings = $"host={ValidPublicHost}&port=587&to=recipient@example.com&from=test@example.com&{qsFragment}";
        var capturedSsl = !expectedSsl;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Test",
            null,
            null,
            "Test message",
            (client, message) =>
            {
                capturedSsl = client.EnableSsl;
            });

        Assert.That(capturedSsl, Is.EqualTo(expectedSsl));
    }

    [Test]
    public void SendEmailNotification_should_display_category_when_category_is_present_and_label_is_null()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var torrent = new Torrent
        {
            Name = "Ubuntu Linux 24.04 ISO",
            Category = "Linux",
            Label = null,
            Progress = 0.75,
            Status = TorrentStatus.Downloading,
            TotalSize = 1024 * 1024 * 500L,
        };

        MailMessage capturedMessage = null;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Download Progress",
            torrent,
            null,
            null,
            (client, message) =>
            {
                capturedMessage = message;
            });

        Assert.That(capturedMessage, Is.Not.Null);
        Assert.That(capturedMessage.Body, Does.Contain("Category: Linux"));
    }

    [Test]
    public void SendEmailNotification_should_display_category_when_category_is_present_and_label_is_empty()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var torrent = new Torrent
        {
            Name = "Ubuntu Linux 24.04 ISO",
            Category = "Linux",
            Label = "",
            Progress = 0.75,
            Status = TorrentStatus.Downloading,
            TotalSize = 1024 * 1024 * 500L,
        };

        MailMessage capturedMessage = null;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Download Progress",
            torrent,
            null,
            null,
            (client, message) =>
            {
                capturedMessage = message;
            });

        Assert.That(capturedMessage, Is.Not.Null);
        Assert.That(capturedMessage.Body, Does.Contain("Category: Linux"));
    }

    [Test]
    public void SendEmailNotification_should_fallback_to_label_when_category_is_null_or_empty()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var torrent = new Torrent
        {
            Name = "Ubuntu Linux 24.04 ISO",
            Category = null,
            Label = "Operating Systems",
            Progress = 1.0,
            Status = TorrentStatus.Seeding,
            TotalSize = 1024 * 1024 * 500L,
        };

        MailMessage capturedMessage = null;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Download Finished",
            torrent,
            null,
            null,
            (client, message) =>
            {
                capturedMessage = message;
            });

        Assert.That(capturedMessage, Is.Not.Null);
        Assert.That(capturedMessage.Body, Does.Contain("Category: Operating Systems"));
    }

    [Test]
    public void SendEmailNotification_should_fallback_to_none_when_both_category_and_label_are_null_or_empty()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":587,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\"}}";
        var torrent = new Torrent
        {
            Name = "Ubuntu Linux 24.04 ISO",
            Category = "",
            Label = "",
            Progress = 1.0,
            Status = TorrentStatus.Seeding,
            TotalSize = 1024 * 1024 * 500L,
        };

        MailMessage capturedMessage = null;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Download Finished",
            torrent,
            null,
            null,
            (client, message) =>
            {
                capturedMessage = message;
            });

        Assert.That(capturedMessage, Is.Not.Null);
        Assert.That(capturedMessage.Body, Does.Contain("Category: None"));
    }

    [TestCase(465, true, null, SecureSocketOptions.SslOnConnect)]
    [TestCase(465, false, null, SecureSocketOptions.SslOnConnect)]
    [TestCase(587, true, null, SecureSocketOptions.StartTls)]
    [TestCase(587, false, null, SecureSocketOptions.None)]
    [TestCase(25, false, null, SecureSocketOptions.None)]
    [TestCase(25, true, null, SecureSocketOptions.StartTls)]
    [TestCase(2525, true, null, SecureSocketOptions.StartTls)]
    [TestCase(587, true, "Auto", SecureSocketOptions.Auto)]
    [TestCase(587, true, "StartTls", SecureSocketOptions.StartTls)]
    [TestCase(587, true, "SslOnConnect", SecureSocketOptions.SslOnConnect)]
    [TestCase(587, true, "None", SecureSocketOptions.None)]
    [TestCase(587, true, "implicit", SecureSocketOptions.SslOnConnect)]
    [TestCase(587, true, "explicit", SecureSocketOptions.StartTls)]
    [TestCase(587, true, "plaintext", SecureSocketOptions.None)]
    public void ResolveSecureSocketOptions_should_resolve_correct_options(
        int port,
        bool ssl,
        string explicitMode,
        SecureSocketOptions expectedOptions)
    {
        var resolved = EmailNotificationSender.ResolveSecureSocketOptions(port, ssl, explicitMode);
        Assert.That(resolved, Is.EqualTo(expectedOptions));
    }

    [TestCase("{\"ignoreSslErrors\":true}", true)]
    [TestCase("{\"ignoreSslErrors\":false}", false)]
    [TestCase("{\"ignoreSsl\":\"true\"}", true)]
    [TestCase("{\"ignoreSsl\":\"false\"}", false)]
    [TestCase("{\"allowInvalidCertificates\":true}", true)]
    [TestCase("{\"allowInvalidCertificates\":false}", false)]
    [TestCase("{\"allowInvalidCert\":true}", true)]
    [TestCase("{\"ignoreTlsErrors\":true}", true)]
    [TestCase("{\"ignoreSslErrors\":1}", true)]
    [TestCase("{\"ignoreSslErrors\":0}", false)]
    public void ParseSettings_should_parse_ignoreSslErrors_from_json(string jsonFragment, bool expected)
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":465,\"to\":\"to@example.com\",{jsonFragment.TrimStart('{')}";
        var parsed = EmailNotificationSender.ParseSettings(settings);

        Assert.That(parsed.IgnoreSslErrors, Is.EqualTo(expected));
    }

    [TestCase("ignoresslerrors=true", true)]
    [TestCase("ignoresslerrors=false", false)]
    [TestCase("ignoressl=true", true)]
    [TestCase("allowinvalidcertificates=true", true)]
    [TestCase("allowinvalidcert=true", true)]
    [TestCase("ignoretlserrors=true", true)]
    [TestCase("ignoresslerrors=1", true)]
    [TestCase("ignoresslerrors=0", false)]
    public void ParseSettings_should_parse_ignoreSslErrors_from_query_string(string qsFragment, bool expected)
    {
        var settings = $"host={ValidPublicHost}&port=465&to=to@example.com&{qsFragment}";
        var parsed = EmailNotificationSender.ParseSettings(settings);

        Assert.That(parsed.IgnoreSslErrors, Is.EqualTo(expected));
    }

    [Test]
    public void ParseSettings_should_parse_tlsMode_and_resolve_socket_options()
    {
        var jsonSettings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":465,\"to\":\"to@example.com\",\"tlsMode\":\"Auto\"}}";
        var parsed = EmailNotificationSender.ParseSettings(jsonSettings);

        Assert.That(parsed.TlsMode, Is.EqualTo("Auto"));
        Assert.That(parsed.ResolveSecureSocketOptions(), Is.EqualTo(SecureSocketOptions.Auto));

        var qsSettings = $"host={ValidPublicHost}&port=587&to=to@example.com&tlsmode=SslOnConnect";
        var parsedQs = EmailNotificationSender.ParseSettings(qsSettings);

        Assert.That(parsedQs.TlsMode, Is.EqualTo("SslOnConnect"));
        Assert.That(parsedQs.ResolveSecureSocketOptions(), Is.EqualTo(SecureSocketOptions.SslOnConnect));
    }

    [Test]
    public void SendEmailNotification_should_dispatch_via_mailkit_with_ignoreSslErrors_callback()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":465,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\",\"ignoreSslErrors\":true}}";
        var mailKitCalled = false;
        MailKit.Net.Smtp.ISmtpClient capturedClient = null;
        MimeMessage capturedMessage = null;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Test",
            null,
            null,
            "Test message",
            smtpSender: null,
            retryDelays: null,
            mailKitSender: (client, message) =>
            {
                mailKitCalled = true;
                capturedClient = client;
                capturedMessage = message;
            });

        Assert.That(mailKitCalled, Is.True);
        Assert.That(capturedClient, Is.Not.Null);
        Assert.That(capturedClient.ServerCertificateValidationCallback, Is.Not.Null);

        // Verify custom certificate validation callback accepts certificates
        var callbackResult = capturedClient.ServerCertificateValidationCallback(
            null,
            null,
            null,
            SslPolicyErrors.RemoteCertificateChainErrors);
        Assert.That(callbackResult, Is.True);

        Assert.That(capturedMessage, Is.Not.Null);
        Assert.That(capturedMessage.To.Mailboxes.First().Address, Is.EqualTo("recipient@example.com"));
        Assert.That(capturedMessage.From.Mailboxes.First().Address, Is.EqualTo("test@example.com"));
        Assert.That(capturedMessage.Headers["Auto-Submitted"], Is.EqualTo("auto-generated"));
        Assert.That(capturedMessage.Headers["Precedence"], Is.EqualTo("bulk"));
        Assert.That(capturedMessage.TextBody, Does.Contain("Test message"));
        Assert.That(capturedMessage.HtmlBody, Does.Contain("Test message"));
    }

    [Test]
    public void SendEmailNotification_should_not_set_certificate_callback_when_ignoreSslErrors_is_false()
    {
        var settings = $"{{\"host\":\"{ValidPublicHost}\",\"port\":465,\"to\":\"recipient@example.com\",\"from\":\"test@example.com\",\"ignoreSslErrors\":false}}";
        MailKit.Net.Smtp.ISmtpClient capturedClient = null;

        EmailNotificationSender.SendEmailNotification(
            settings,
            "Test",
            null,
            null,
            "Test message",
            smtpSender: null,
            retryDelays: null,
            mailKitSender: (client, message) =>
            {
                capturedClient = client;
            });

        Assert.That(capturedClient, Is.Not.Null);
        Assert.That(capturedClient.ServerCertificateValidationCallback, Is.Null);
    }

    [TestCase(MailKit.Net.Smtp.SmtpStatusCode.ServiceNotAvailable, true)]
    [TestCase(MailKit.Net.Smtp.SmtpStatusCode.MailboxBusy, true)]
    [TestCase(MailKit.Net.Smtp.SmtpStatusCode.TransactionFailed, true)]
    [TestCase(MailKit.Net.Smtp.SmtpStatusCode.InsufficientStorage, true)]
    [TestCase(MailKit.Net.Smtp.SmtpStatusCode.MailboxUnavailable, false)]
    public void IsTransientMailKitSmtpError_should_identify_transient_status_codes(
        MailKit.Net.Smtp.SmtpStatusCode code,
        bool expectedTransient)
    {
        var ex = new MailKit.Net.Smtp.SmtpCommandException(MailKit.Net.Smtp.SmtpErrorCode.UnexpectedStatusCode, code, "Test error");
        var isTransient = EmailNotificationSender.IsTransientMailKitSmtpError(ex);
        Assert.That(isTransient, Is.EqualTo(expectedTransient));
    }
}
