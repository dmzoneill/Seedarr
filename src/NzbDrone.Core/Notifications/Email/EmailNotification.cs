using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using NLog;

namespace NzbDrone.Core.Notifications.Email;

public class EmailSettings
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";

    /// <summary>
    /// Comma-separated list of recipient email addresses.
    /// </summary>
    public string ToAddresses { get; set; } = "";
}

public class EmailNotification : INotificationService
{
    private const string SubjectPrefix = "[Seedarr]";

    private readonly Logger _logger;

    public string Name => "Email";
    public EmailSettings Settings { get; set; } = new();

    public EmailNotification()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void OnTorrentAdded(string torrentName)
    {
        var subject = $"{SubjectPrefix} Torrent Added";
        var body = $"A new torrent has been added.\n\nTorrent: {torrentName}\nTime: {DateTime.UtcNow:u}";
        SendEmail(subject, body);
    }

    public void OnSeedingStarted(string torrentName)
    {
        var subject = $"{SubjectPrefix} Seeding Started";
        var body = $"Seeding has started for a torrent.\n\nTorrent: {torrentName}\nTime: {DateTime.UtcNow:u}";
        SendEmail(subject, body);
    }

    public void OnSeedingStopped(string torrentName)
    {
        var subject = $"{SubjectPrefix} Seeding Stopped";
        var body = $"Seeding has stopped for a torrent.\n\nTorrent: {torrentName}\nTime: {DateTime.UtcNow:u}";
        SendEmail(subject, body);
    }

    public void OnHealthIssue(string source, string message)
    {
        var subject = $"{SubjectPrefix} Health Issue: {source}";
        var body = $"A health issue has been detected.\n\nSource: {source}\nMessage: {message}\nTime: {DateTime.UtcNow:u}";
        SendEmail(subject, body);
    }

    private void SendEmail(string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(Settings.SmtpHost))
        {
            _logger.Warn("Email SMTP host is not configured");
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.FromAddress) || string.IsNullOrWhiteSpace(Settings.ToAddresses))
        {
            _logger.Warn("Email from/to addresses are not configured");
            return;
        }

        try
        {
            using var message = new MailMessage();
            message.From = new MailAddress(Settings.FromAddress);
            message.Subject = subject;
            message.Body = body;
            message.IsBodyHtml = false;

            var recipients = Settings.ToAddresses
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => !string.IsNullOrWhiteSpace(a));

            foreach (var recipient in recipients)
            {
                message.To.Add(new MailAddress(recipient));
            }

            if (message.To.Count == 0)
            {
                _logger.Warn("No valid recipient addresses configured for email notification");
                return;
            }

            using var client = new SmtpClient(Settings.SmtpHost, Settings.SmtpPort);
            client.EnableSsl = Settings.UseTls;

            if (!string.IsNullOrWhiteSpace(Settings.Username))
            {
                client.Credentials = new NetworkCredential(Settings.Username, Settings.Password);
            }

            client.SendMailAsync(message).GetAwaiter().GetResult();
            _logger.Debug("Email notification sent to {0}", Settings.ToAddresses);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send email notification to {0}", Settings.ToAddresses);
        }
    }
}
