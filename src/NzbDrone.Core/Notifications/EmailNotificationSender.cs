using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Security;
using MimeKit;
using NLog;
using NzbDrone.Core.Notifications.Email;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Validation;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Notifications;

public class ParsedEmailSettings
{
    public string Host { get; set; }
    public int Port { get; set; } = 25;
    public bool Ssl { get; set; }
    public bool IgnoreSslErrors { get; set; }
    public string TlsMode { get; set; }
    public bool? Auth { get; set; }
    public string User { get; set; }
    public string Pass { get; set; }
    public string From { get; set; } = "seedarr@localhost";
    public string To { get; set; }

    public SecureSocketOptions ResolveSecureSocketOptions() =>
        EmailNotificationSender.ResolveSecureSocketOptions(Port, Ssl, TlsMode);
}

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

    public static SecureSocketOptions ResolveSecureSocketOptions(int port, bool ssl, string explicitMode = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitMode))
        {
            if (Enum.TryParse<SecureSocketOptions>(explicitMode, true, out var parsedOptions))
            {
                return parsedOptions;
            }

            if (string.Equals(explicitMode, "implicit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(explicitMode, "ssl", StringComparison.OrdinalIgnoreCase))
            {
                return SecureSocketOptions.SslOnConnect;
            }

            if (string.Equals(explicitMode, "explicit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(explicitMode, "tls", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(explicitMode, "starttls", StringComparison.OrdinalIgnoreCase))
            {
                return SecureSocketOptions.StartTls;
            }

            if (string.Equals(explicitMode, "plaintext", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(explicitMode, "plain", StringComparison.OrdinalIgnoreCase))
            {
                return SecureSocketOptions.None;
            }
        }

        if (port == 465)
        {
            return SecureSocketOptions.SslOnConnect;
        }

        return ssl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
    }

    public static ParsedEmailSettings ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            throw new ArgumentException("Email settings are required.", nameof(settings));
        }

        var result = new ParsedEmailSettings();

        if (settings.TrimStart().StartsWith("{"))
        {
            using var doc = JsonDocument.Parse(settings);
            var root = doc.RootElement;
            if (root.TryGetProperty("server", out var s) || root.TryGetProperty("host", out s))
            {
                result.Host = s.GetString();
            }

            if (root.TryGetProperty("port", out var p))
            {
                if (p.TryGetInt32(out var pInt))
                {
                    result.Port = pInt;
                }
                else if (int.TryParse(p.GetString(), out var pParsed))
                {
                    result.Port = pParsed;
                }
            }

            if (root.TryGetProperty("useSsl", out var sslProp) ||
                root.TryGetProperty("ssl", out sslProp) ||
                root.TryGetProperty("useTls", out sslProp) ||
                root.TryGetProperty("tls", out sslProp) ||
                root.TryGetProperty("enableSsl", out sslProp) ||
                root.TryGetProperty("requireTls", out sslProp))
            {
                if (TryParseBoolean(sslProp, out var parsedSsl))
                {
                    result.Ssl = parsedSsl;
                }
            }

            if (root.TryGetProperty("ignoreSslErrors", out var iseProp) ||
                root.TryGetProperty("ignoreSsl", out iseProp) ||
                root.TryGetProperty("allowInvalidCertificates", out iseProp) ||
                root.TryGetProperty("allowInvalidCert", out iseProp) ||
                root.TryGetProperty("ignoreTlsErrors", out iseProp))
            {
                if (TryParseBoolean(iseProp, out var parsedIse))
                {
                    result.IgnoreSslErrors = parsedIse;
                }
            }

            if (root.TryGetProperty("tlsMode", out var tmProp) ||
                root.TryGetProperty("secureSocketOptions", out tmProp))
            {
                result.TlsMode = tmProp.GetString();
            }

            if (root.TryGetProperty("auth", out var authProp) ||
                root.TryGetProperty("useAuth", out authProp))
            {
                if (TryParseBoolean(authProp, out var parsedAuth))
                {
                    result.Auth = parsedAuth;
                }
            }

            if (root.TryGetProperty("username", out var u) || root.TryGetProperty("user", out u))
            {
                result.User = u.GetString();
            }

            if (root.TryGetProperty("password", out var pwd) || root.TryGetProperty("pass", out pwd))
            {
                result.Pass = pwd.GetString();
            }

            if (root.TryGetProperty("from", out var f))
            {
                result.From = f.GetString() ?? result.From;
            }

            if (root.TryGetProperty("to", out var tProp) || root.TryGetProperty("recipient", out tProp))
            {
                result.To = tProp.GetString();
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
                        result.Host = val;
                        break;
                    case "port":
                        if (int.TryParse(val, out var p))
                        {
                            result.Port = p;
                        }

                        break;
                    case "ssl":
                    case "usessl":
                    case "tls":
                    case "usetls":
                    case "enablessl":
                    case "requiretls":
                        if (TryParseBoolean(val, out var s))
                        {
                            result.Ssl = s;
                        }

                        break;
                    case "ignoresslerrors":
                    case "ignoressl":
                    case "allowinvalidcertificates":
                    case "allowinvalidcert":
                    case "ignoretlserrors":
                        if (TryParseBoolean(val, out var ise))
                        {
                            result.IgnoreSslErrors = ise;
                        }

                        break;
                    case "tlsmode":
                    case "securesocketoptions":
                        result.TlsMode = val;
                        break;
                    case "auth":
                    case "useauth":
                        if (TryParseBoolean(val, out var a))
                        {
                            result.Auth = a;
                        }

                        break;
                    case "user":
                    case "username":
                        result.User = val;
                        break;
                    case "pass":
                    case "password":
                        result.Pass = val;
                        break;
                    case "from":
                    case "sender":
                        result.From = val;
                        break;
                    case "to":
                    case "recipient":
                        result.To = val;
                        break;
                }
            }
        }

        return result;
    }

    public static void SendEmailNotification(
        string settings,
        string eventType,
        Torrent torrent,
        object meta,
        object genericPayload,
        Action<SmtpClient, MailMessage> smtpSender = null,
        IReadOnlyList<TimeSpan> retryDelays = null,
        Action<MailKit.Net.Smtp.ISmtpClient, MimeMessage> mailKitSender = null)
    {
        Func<SmtpClient, MailMessage, CancellationToken, Task> asyncSender = smtpSender != null
            ? (client, mail, _) =>
            {
                smtpSender(client, mail);
                return Task.CompletedTask;
            }
        : null;

        Func<MailKit.Net.Smtp.ISmtpClient, MimeMessage, CancellationToken, Task> asyncMailKit = mailKitSender != null
            ? (client, mail, _) =>
            {
                mailKitSender(client, mail);
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
            CancellationToken.None,
            asyncMailKit).GetAwaiter().GetResult();
    }

    public static async Task SendEmailNotificationAsync(
        string settings,
        string eventType,
        Torrent torrent,
        object meta,
        object genericPayload,
        Func<SmtpClient, MailMessage, CancellationToken, Task> asyncSmtpSender = null,
        IReadOnlyList<TimeSpan> retryDelays = null,
        CancellationToken cancellationToken = default,
        Func<MailKit.Net.Smtp.ISmtpClient, MimeMessage, CancellationToken, Task> asyncMailKitSender = null)
    {
        var config = ParseSettings(settings);

        if (string.IsNullOrWhiteSpace(config.To))
        {
            throw new InvalidOperationException("Recipient email address ('to') is required.");
        }

        if (string.IsNullOrWhiteSpace(config.Host))
        {
            throw new ArgumentException("SMTP host is required.", nameof(settings));
        }

        if (!UrlValidator.IsSafeHost(config.Host))
        {
            throw new ArgumentException($"Target SMTP host '{config.Host}' is not permitted.", nameof(settings));
        }

        if (!AllowedSmtpPorts.Contains(config.Port))
        {
            throw new ArgumentException($"SMTP port {config.Port} is not permitted. Allowed ports: {string.Join(", ", AllowedSmtpPorts)}.", nameof(settings));
        }

        var torrentName = torrent?.Name ?? NotificationPayloadBuilder.ExtractMessage(genericPayload, eventType);
        var subject = $"[Seedarr] [{eventType}] {torrentName}";
        var category = torrent != null
            ? (!string.IsNullOrWhiteSpace(torrent.Category) ? torrent.Category : (!string.IsNullOrWhiteSpace(torrent.Label) ? torrent.Label : "None"))
            : "None";
        var torrentDetails = torrent != null
            ? $"Torrent: {torrent.Name}\nCategory: {category}\nProgress: {torrent.Progress * 100:F1}%\nStatus: {torrent.Status}\nSize: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
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

        var htmlBody = EmailTemplateRenderer.Render(eventType, body, torrent, meta, genericPayload, err);

        var recipients = config.To.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var validRecipients = new List<string>();
        foreach (var recipient in recipients)
        {
            var trimmed = recipient.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            try
            {
                var parsed = new MailAddress(trimmed);
                validRecipients.Add(parsed.Address);
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

        if (validRecipients.Count == 0)
        {
            Logger.Warn("No valid recipient email addresses found in '{0}'.", config.To);
            throw new FormatException($"No valid recipient email addresses found in '{config.To}'.");
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(string.IsNullOrWhiteSpace(config.From) ? "seedarr@localhost" : config.From),
            Subject = subject,
            Body = body,
        };

        mail.Headers.Add("Auto-Submitted", "auto-generated");
        mail.Headers.Add("X-Auto-Response-Suppress", "All");
        mail.Headers.Add("Precedence", "bulk");

        var plainTextView = AlternateView.CreateAlternateViewFromString(body, null, "text/plain");
        mail.AlternateViews.Add(plainTextView);

        var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html");
        mail.AlternateViews.Add(htmlView);

        foreach (var r in validRecipients)
        {
            mail.To.Add(new MailAddress(r));
        }

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(MailboxAddress.Parse(string.IsNullOrWhiteSpace(config.From) ? "seedarr@localhost" : config.From));
        foreach (var r in validRecipients)
        {
            mimeMessage.To.Add(MailboxAddress.Parse(r));
        }

        mimeMessage.Subject = subject;
        mimeMessage.Headers.Add("Auto-Submitted", "auto-generated");
        mimeMessage.Headers.Add("X-Auto-Response-Suppress", "All");
        mimeMessage.Headers.Add("Precedence", "bulk");

        var bodyBuilder = new BodyBuilder
        {
            TextBody = body,
            HtmlBody = htmlBody
        };
        mimeMessage.Body = bodyBuilder.ToMessageBody();

        var pipeline = CreateSmtpRetryPipeline(retryDelays);

        try
        {
            await pipeline.ExecuteAsync(
                async ct =>
                {
                    if (asyncSmtpSender != null)
                    {
                        using var client = new SmtpClient(config.Host, config.Port)
                        {
                            EnableSsl = config.Ssl,
                            Timeout = 10000,
                        };

                        if (config.Auth != false && !string.IsNullOrWhiteSpace(config.User) && !string.IsNullOrWhiteSpace(config.Pass))
                        {
                            client.Credentials = new NetworkCredential(config.User, config.Pass);
                        }

                        await asyncSmtpSender(client, mail, ct).ConfigureAwait(false);
                        return;
                    }

                    using var mailKitClient = new MailKit.Net.Smtp.SmtpClient();
                    mailKitClient.Timeout = 10000;

                    if (config.IgnoreSslErrors)
                    {
                        mailKitClient.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                    }

                    if (asyncMailKitSender != null)
                    {
                        await asyncMailKitSender(mailKitClient, mimeMessage, ct).ConfigureAwait(false);
                        return;
                    }

                    var secureSocketOptions = config.ResolveSecureSocketOptions();

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(10));

                    await mailKitClient.ConnectAsync(config.Host, config.Port, secureSocketOptions, cts.Token).ConfigureAwait(false);

                    if (config.Auth != false && !string.IsNullOrWhiteSpace(config.User) && !string.IsNullOrWhiteSpace(config.Pass))
                    {
                        await mailKitClient.AuthenticateAsync(config.User, config.Pass, cts.Token).ConfigureAwait(false);
                    }

                    await mailKitClient.SendAsync(mimeMessage, cts.Token).ConfigureAwait(false);
                    await mailKitClient.DisconnectAsync(true, cts.Token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (SmtpException ex) when (IsTransientSmtpError(ex))
        {
            Logger.Error(ex, "Failed to send email notification to '{0}' after transient retries were exhausted.", config.To);
            throw;
        }
        catch (MailKit.Net.Smtp.SmtpCommandException ex) when (IsTransientMailKitSmtpError(ex))
        {
            Logger.Error(ex, "Failed to send email notification to '{0}' after transient retries were exhausted.", config.To);
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to send email notification to '{0}'.", config.To);
            throw;
        }
    }

    public static ResiliencePipeline CreateSmtpRetryPipeline(
        IReadOnlyList<TimeSpan> retryDelays = null,
        Action<SmtpException, TimeSpan, int> onRetry = null,
        Action<Exception, TimeSpan, int> onAnyRetry = null)
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
                ShouldHandle = new PredicateBuilder()
                    .Handle<SmtpException>(ex => IsTransientSmtpError(ex))
                    .Handle<MailKit.Net.Smtp.SmtpCommandException>(ex => IsTransientMailKitSmtpError(ex)),
                OnRetry = args =>
                {
                    var outcomeEx = args.Outcome.Exception;
                    var smtpEx = outcomeEx as SmtpException ?? outcomeEx?.InnerException as SmtpException;
                    var mailKitEx = outcomeEx as MailKit.Net.Smtp.SmtpCommandException ?? outcomeEx?.InnerException as MailKit.Net.Smtp.SmtpCommandException;
                    var statusCode = smtpEx?.StatusCode.ToString() ?? mailKitEx?.StatusCode.ToString() ?? "Unknown";

                    Logger.Warn(
                        "SMTP delivery failed with transient error ({0}): {1}. Retrying in {2} (attempt {3}/{4})...",
                        statusCode,
                        args.Outcome.Exception?.Message,
                        args.RetryDelay,
                        args.AttemptNumber + 1,
                        maxAttempts);

                    if (smtpEx != null && onRetry != null)
                    {
                        onRetry(smtpEx, args.RetryDelay, args.AttemptNumber + 1);
                    }

                    if (onAnyRetry != null && args.Outcome.Exception != null)
                    {
                        onAnyRetry(args.Outcome.Exception, args.RetryDelay, args.AttemptNumber + 1);
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

    public static bool IsTransientMailKitSmtpError(MailKit.Net.Smtp.SmtpCommandException ex)
    {
        if (ex == null)
        {
            return false;
        }

        return ex.StatusCode switch
        {
            MailKit.Net.Smtp.SmtpStatusCode.ServiceNotAvailable => true,    // 421
            MailKit.Net.Smtp.SmtpStatusCode.MailboxBusy => true,            // 450
            MailKit.Net.Smtp.SmtpStatusCode.TransactionFailed => true, // 451
            MailKit.Net.Smtp.SmtpStatusCode.InsufficientStorage => true,    // 452
            _ => (int)ex.StatusCode is 421 or 450 or 451 or 452
        };
    }

    private static bool TryParseBoolean(JsonElement element, out bool result)
    {
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = element.GetBoolean();
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return TryParseBoolean(element.GetString(), out result);
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var intVal))
        {
            result = intVal != 0;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = false;
            return false;
        }

        if (bool.TryParse(value, out result))
        {
            return true;
        }

        if (int.TryParse(value, out var intVal))
        {
            result = intVal != 0;
            return true;
        }

        result = false;
        return false;
    }
}
