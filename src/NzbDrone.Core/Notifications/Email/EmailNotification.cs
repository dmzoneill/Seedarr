using System;
using System.Linq;
using System.Net.Mail;
using NLog;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Email;

public class EmailNotification : INotificationService
{
    private const string SubjectPrefix = "[Seedarr]";
    private static readonly int[] AllowedSmtpPorts = { 25, 465, 587, 2525 };

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

        if (!UrlValidator.IsSafeHost(Settings.SmtpHost))
        {
            _logger.Warn("Email SMTP host '{0}' is not permitted (private or loopback address)", Settings.SmtpHost);
            return;
        }

        if (!AllowedSmtpPorts.Contains(Settings.SmtpPort))
        {
            _logger.Warn("Email SMTP port {0} is not permitted. Allowed ports: {1}", Settings.SmtpPort, string.Join(", ", AllowedSmtpPorts));
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

            message.Headers.Add("Auto-Submitted", "auto-generated");
            message.Headers.Add("X-Auto-Response-Suppress", "All");
            message.Headers.Add("Precedence", "bulk");

            var plainTextView = AlternateView.CreateAlternateViewFromString(body, null, "text/plain");
            message.AlternateViews.Add(plainTextView);

            var htmlBody = EmailTemplateRenderer.Render(subject, body);
            var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html");
            message.AlternateViews.Add(htmlView);

            var recipients = Settings.ToAddresses
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => !string.IsNullOrWhiteSpace(a));

            foreach (var recipient in recipients)
            {
                var trimmed = recipient.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                try
                {
                    message.To.Add(new MailAddress(trimmed));
                }
                catch (FormatException ex)
                {
                    _logger.Warn(ex, "Invalid recipient email address format '{0}' skipped", trimmed);
                }
                catch (ArgumentException ex)
                {
                    _logger.Warn(ex, "Invalid recipient email address '{0}' skipped", trimmed);
                }
            }

            if (message.To.Count == 0)
            {
                _logger.Warn("No valid recipient addresses configured for email notification");
                return;
            }

            SmtpSend(message);
            _logger.Debug("Email notification sent to {0}", Settings.ToAddresses);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send email notification to {0}", Settings.ToAddresses);
        }
    }

    protected virtual void SmtpSend(MailMessage message)
    {
        var retryPipeline = EmailNotificationSender.CreateSmtpRetryPipeline();
        retryPipeline.Execute(() =>
        {
            using var client = new MailKit.Net.Smtp.SmtpClient();
            client.Timeout = 10000;

            if (Settings.IgnoreSslErrors || Settings.AllowInvalidCertificates)
            {
                client.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }

            var secureSocketOptions = EmailNotificationSender.ResolveSecureSocketOptions(Settings.SmtpPort, Settings.UseTls);
            client.Connect(Settings.SmtpHost, Settings.SmtpPort, secureSocketOptions);

            if (!string.IsNullOrWhiteSpace(Settings.Username))
            {
                client.Authenticate(Settings.Username, Settings.Password);
            }

            var mimeMessage = MimeKit.MimeMessage.CreateFromMailMessage(message);
            client.Send(mimeMessage);
            client.Disconnect(true);
        });
    }
}
