using System;
using System.Net.Mail;
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
}
