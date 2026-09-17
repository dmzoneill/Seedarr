using System;
using System.Net.Mail;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Notifications;

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
}
