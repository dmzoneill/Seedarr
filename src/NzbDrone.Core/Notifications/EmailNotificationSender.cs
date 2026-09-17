using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Notifications.Email;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Validation;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Notifications;

public static class EmailNotificationSender
{
    private static readonly int[] AllowedSmtpPorts = { 25, 465, 587, 2525 };
    private static readonly TimeSpan[] DefaultRetryDelays =
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static void SendEmailNotification(
        string settings,
        string eventType,
        Torrent torrent,
        dynamic meta,
        object genericPayload,
        Action<SmtpClient, MailMessage> smtpSender = null,
        IReadOnlyList<TimeSpan> retryDelays = null)
    {
        Func<SmtpClient, MailMessage, CancellationToken, Task> asyncSender = smtpSender != null
            ? (client, mail, _) =>
            {
                smtpSender(client, mail);
                return Task.CompletedTask;
            }
            : null;

        SendEmailNotificationAsync(
            settings,
            eventType,
            torrent,
            meta,
            genericPayload,
            asyncSender,
            retryDelays,
            CancellationToken.None).GetAwaiter().GetResult();
    }

    public static async Task SendEmailNotificationAsync(
        string settings,
        string eventType,
        Torrent torrent,
        dynamic meta,
        object genericPayload,
        Func<SmtpClient, MailMessage, CancellationToken, Task> asyncSmtpSender = null,
        IReadOnlyList<TimeSpan> retryDelays = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            throw new ArgumentException("Email settings are required.", nameof(settings));
        }

        string host = null;
        var port = 25;
        var ssl = false;
        string user = null;
        string pass = null;
        var from = "seedarr@localhost";
        string to = null;

        if (settings.TrimStart().StartsWith("{"))
        {
            using var doc = JsonDocument.Parse(settings);
            var root = doc.RootElement;
            if (root.TryGetProperty("server", out var s) || root.TryGetProperty("host", out s))
            {
                host = s.GetString();
            }

            if (root.TryGetProperty("port", out var p))
            {
                if (p.TryGetInt32(out var pInt))
                {
                    port = pInt;
                }
                else if (int.TryParse(p.GetString(), out var pParsed))
                {
                    port = pParsed;
                }
            }

            if (root.TryGetProperty("useSsl", out var sslProp) || root.TryGetProperty("ssl", out sslProp))
            {
                ssl = sslProp.GetBoolean();
            }

            if (root.TryGetProperty("username", out var u) || root.TryGetProperty("user", out u))
            {
                user = u.GetString();
            }

            if (root.TryGetProperty("password", out var pwd) || root.TryGetProperty("pass", out pwd))
            {
                pass = pwd.GetString();
            }

            if (root.TryGetProperty("from", out var f))
            {
                from = f.GetString() ?? from;
            }

            if (root.TryGetProperty("to", out var tProp) || root.TryGetProperty("recipient", out tProp))
            {
                to = tProp.GetString();
            }
        }
        else if (settings.Contains('='))
        {
            var pairs = settings.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(parts[0]).Trim().ToLowerInvariant();
                var val = Uri.UnescapeDataString(parts[1]).Trim();
                switch (key)
                {
                    case "server":
                    case "host":
                        host = val;
                        break;
                    case "port":
                        if (int.TryParse(val, out var p))
                        {
                            port = p;
                        }

                        break;
                    case "ssl":
                    case "usessl":
                        if (bool.TryParse(val, out var s))
                        {
                            ssl = s;
                        }

                        break;
                    case "user":
                    case "username":
                        user = val;
                        break;
                    case "pass":
                    case "password":
                        pass = val;
                        break;
                    case "from":
                    case "sender":
                        from = val;
                        break;
                    case "to":
                    case "recipient":
                        to = val;
                        break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            throw new InvalidOperationException("Recipient email address ('to') is required.");
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("SMTP host is required.", nameof(settings));
        }

        if (!UrlValidator.IsSafeHost(host))
        {
            throw new ArgumentException($"Target SMTP host '{host}' is not permitted.", nameof(settings));
        }

        if (!AllowedSmtpPorts.Contains(port))
        {
            throw new ArgumentException($"SMTP port {port} is not permitted. Allowed ports: {string.Join(", ", AllowedSmtpPorts)}.", nameof(settings));
        }

        var torrentName = torrent?.Name ?? NotificationPayloadBuilder.ExtractMessage(genericPayload, eventType);
        var subject = $"[Seedarr] [{eventType}] {torrentName}";
        var torrentDetails = torrent != null
            ? $"Torrent: {torrent.Name}\nCategory: {torrent.Label ?? "None"}\nProgress: {torrent.Progress * 100:F1}%\nStatus: {torrent.Status}\nSize: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
            : NotificationPayloadBuilder.ExtractMessage(genericPayload, $"Event: {eventType}");
        var err = NotificationPayloadBuilder.ExtractErrorMessage(genericPayload);
        if (!string.IsNullOrWhiteSpace(err))
        {
            torrentDetails += $"\nError: {err}";
        }

        var overview = NotificationPayloadBuilder.ExtractOverview(meta);
        var body = !string.IsNullOrWhiteSpace(overview)
            ? $"{torrentDetails}\n\n{overview}"
            : torrentDetails;

        using var mail = new MailMessage
        {
            From = new MailAddress(string.IsNullOrWhiteSpace(from) ? "seedarr@localhost" : from),
            Subject = subject,
            Body = body,
        };

        mail.Headers.Add("Auto-Submitted", "auto-generated");
        mail.Headers.Add("X-Auto-Response-Suppress", "All");
        mail.Headers.Add("Precedence", "bulk");

        var plainTextView = AlternateView.CreateAlternateViewFromString(body, null, "text/plain");
        mail.AlternateViews.Add(plainTextView);

        var htmlBody = EmailTemplateRenderer.Render(eventType, body, torrent, meta, genericPayload, err);
        var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html");
        mail.AlternateViews.Add(htmlView);

        var recipients = to.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var recipient in recipients)
        {
            var trimmed = recipient.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            try
            {
                mail.To.Add(new MailAddress(trimmed));
            }
            catch (FormatException ex)
            {
                Logger.Warn(ex, "Invalid recipient email address format '{0}' skipped.", trimmed);
            }
            catch (ArgumentException ex)
            {
                Logger.Warn(ex, "Invalid recipient email address '{0}' skipped.", trimmed);
            }
        }

        if (mail.To.Count == 0)
        {
            Logger.Warn("No valid recipient email addresses found in '{0}'.", to);
            throw new FormatException($"No valid recipient email addresses found in '{to}'.");
        }

        var pipeline = CreateSmtpRetryPipeline(retryDelays);

        try
        {
            await pipeline.ExecuteAsync(
                async ct =>
                {
                    using var client = new SmtpClient(host, port)
                    {
                        EnableSsl = ssl,
                        Timeout = 10000,
                    };

                    if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
                    {
                        client.Credentials = new NetworkCredential(user, pass);
                    }

                    if (asyncSmtpSender != null)
                    {
                        await asyncSmtpSender(client, mail, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(TimeSpan.FromSeconds(10));
                        await client.SendMailAsync(mail, cts.Token).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (SmtpException ex) when (IsTransientSmtpError(ex))
        {
            Logger.Error(ex, "Failed to send email notification to '{0}' after transient retries were exhausted.", to);
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to send email notification to '{0}'.", to);
            throw;
        }
    }

    public static ResiliencePipeline CreateSmtpRetryPipeline(
        IReadOnlyList<TimeSpan> retryDelays = null,
        Action<SmtpException, TimeSpan, int> onRetry = null)
    {
        var delays = (retryDelays != null && retryDelays.Count > 0) ? retryDelays : DefaultRetryDelays;
        var maxAttempts = delays.Count;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxAttempts,
                DelayGenerator = args =>
                {
                    var index = Math.Min(args.AttemptNumber, delays.Count - 1);
                    return new ValueTask<TimeSpan?>(delays[index]);
                },
                ShouldHandle = new PredicateBuilder().Handle<SmtpException>(ex => IsTransientSmtpError(ex)),
                OnRetry = args =>
                {
                    var ex = args.Outcome.Exception as SmtpException ?? args.Outcome.Exception?.InnerException as SmtpException;
                    Logger.Warn(
                        "SMTP delivery failed with transient error ({0}): {1}. Retrying in {2} (attempt {3}/{4})...",
                        ex?.StatusCode.ToString() ?? "Unknown",
                        args.Outcome.Exception?.Message,
                        args.RetryDelay,
                        args.AttemptNumber + 1,
                        maxAttempts);

                    if (ex != null && onRetry != null)
                    {
                        onRetry(ex, args.RetryDelay, args.AttemptNumber + 1);
                    }

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public static bool IsTransientSmtpError(SmtpException ex)
    {
        if (ex == null)
        {
            return false;
        }

        return ex.StatusCode switch
        {
            SmtpStatusCode.ServiceNotAvailable => true,    // 421
            SmtpStatusCode.MailboxBusy => true,            // 450
            SmtpStatusCode.LocalErrorInProcessing => true, // 451
            SmtpStatusCode.InsufficientStorage => true,    // 452
            _ => (int)ex.StatusCode is 421 or 450 or 451 or 452
        };
    }
}
