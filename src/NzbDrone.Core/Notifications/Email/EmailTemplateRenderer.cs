using System;
using System.Globalization;
using System.Net;
using System.Text;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications.Email;

public static class EmailTemplateRenderer
{
    public static string Render(
        string eventType,
        string messageOrBody,
        Torrent torrent = null,
        object meta = null,
        object genericPayload = null,
        string errorMessage = null)
    {
        var cleanEventType = string.IsNullOrWhiteSpace(eventType) ? "Notification" : eventType.Trim();
        var badgeColor = GetBadgeColor(cleanEventType);
        var encodedEventType = WebUtility.HtmlEncode(cleanEventType);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"utf-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("    <meta name=\"color-scheme\" content=\"light dark\">");
        sb.AppendLine("    <meta name=\"supported-color-schemes\" content=\"light dark\">");
        sb.AppendLine($"    <title>{encodedEventType}</title>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body style=\"margin:0;padding:0;background-color:#1a1d21;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:#ffffff;\">");
        sb.AppendLine("    <table role=\"presentation\" width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\" style=\"background-color:#1a1d21;width:100%;padding:32px 16px;\">");
        sb.AppendLine("        <tr>");
        sb.AppendLine("            <td align=\"center\">");
        sb.AppendLine("                <table role=\"presentation\" width=\"600\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\" style=\"background-color:#22272b;border-radius:8px;max-width:600px;width:100%;border:1px solid #2d333b;overflow:hidden;\">");
        sb.AppendLine("                    <!-- Header -->");
        sb.AppendLine("                    <tr>");
        sb.AppendLine("                        <td style=\"padding:20px 24px;border-bottom:1px solid #2d333b;\">");
        sb.AppendLine("                            <table role=\"presentation\" width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\">");
        sb.AppendLine("                                <tr>");
        sb.AppendLine("                                    <td valign=\"middle\">");
        sb.AppendLine("                                        <span style=\"font-size:20px;font-weight:700;color:#ffffff;letter-spacing:-0.5px;\">Seedarr</span>");
        sb.AppendLine("                                    </td>");
        sb.AppendLine("                                    <td align=\"right\" valign=\"middle\">");
        sb.AppendLine($"                                        <span style=\"display:inline-block;padding:4px 12px;border-radius:12px;font-size:12px;font-weight:600;color:#ffffff;background-color:{badgeColor};\">{encodedEventType}</span>");
        sb.AppendLine("                                    </td>");
        sb.AppendLine("                                </tr>");
        sb.AppendLine("                            </table>");
        sb.AppendLine("                        </td>");
        sb.AppendLine("                    </tr>");
        sb.AppendLine("                    <!-- Content -->");
        sb.AppendLine("                    <tr>");
        sb.AppendLine("                        <td style=\"padding:24px;\">");

        if (torrent != null)
        {
            var torrentName = WebUtility.HtmlEncode(torrent.Name ?? "Unknown");
            var category = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(torrent.Category) ? (torrent.Label ?? "None") : torrent.Category);
            var sizeStr = FormatBytes(torrent.TotalSize);
            var progressPct = torrent.Progress switch
            {
                <= 1.0 and >= 0.0 => torrent.Progress * 100.0,
                > 1.0 and <= 100.0 => torrent.Progress,
                _ => Math.Clamp(torrent.Progress, 0.0, 100.0)
            };
            var progressPctStr = progressPct.ToString("F1", CultureInfo.InvariantCulture);
            var ratioStr = torrent.Ratio.ToString("F2", CultureInfo.InvariantCulture);
            var stateStr = WebUtility.HtmlEncode(torrent.Status.ToString());

            sb.AppendLine("                            <div style=\"font-size:18px;font-weight:600;color:#ffffff;margin-bottom:16px;\">");
            sb.AppendLine($"                                {torrentName}");
            sb.AppendLine("                            </div>");

            sb.AppendLine("                            <table role=\"presentation\" width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;\">");

            sb.AppendLine("                                <tr>");
            sb.AppendLine("                                    <td style=\"padding:8px 0;color:#c7d1db;font-size:14px;width:120px;\">Torrent Name</td>");
            sb.AppendLine($"                                    <td style=\"padding:8px 0;color:#ffffff;font-size:14px;font-weight:500;\">{torrentName}</td>");
            sb.AppendLine("                                </tr>");

            sb.AppendLine("                                <tr>");
            sb.AppendLine("                                    <td style=\"padding:8px 0;color:#c7d1db;font-size:14px;\">Category</td>");
            sb.AppendLine($"                                    <td style=\"padding:8px 0;color:#ffffff;font-size:14px;\">{category}</td>");
            sb.AppendLine("                                </tr>");

            sb.AppendLine("                                <tr>");
            sb.AppendLine("                                    <td style=\"padding:8px 0;color:#c7d1db;font-size:14px;\">Total Size</td>");
            sb.AppendLine($"                                    <td style=\"padding:8px 0;color:#ffffff;font-size:14px;\">{sizeStr}</td>");
            sb.AppendLine("                                </tr>");

            sb.AppendLine("                                <tr>");
            sb.AppendLine("                                    <td style=\"padding:8px 0;color:#c7d1db;font-size:14px;\">Progress</td>");
            sb.AppendLine("                                    <td style=\"padding:8px 0;color:#ffffff;font-size:14px;\">");
            sb.AppendLine($"                                        <div style=\"margin-bottom:4px;font-size:12px;color:#c7d1db;\">{progressPctStr}%</div>");
            sb.AppendLine("                                        <div style=\"background-color:#2d333b;width:100%;height:8px;border-radius:4px;overflow:hidden;\">");
            sb.AppendLine($"                                            <div style=\"background-color:#22c55e;width:{progressPctStr}%;height:8px;border-radius:4px;\"></div>");
            sb.AppendLine("                                        </div>");
            sb.AppendLine("                                    </td>");
            sb.AppendLine("                                </tr>");

            sb.AppendLine("                                <tr>");
            sb.AppendLine("                                    <td style=\"padding:8px 0;color:#c7d1db;font-size:14px;\">Ratio</td>");
            sb.AppendLine($"                                    <td style=\"padding:8px 0;color:#ffffff;font-size:14px;\">{ratioStr}</td>");
            sb.AppendLine("                                </tr>");

            sb.AppendLine("                                <tr>");
            sb.AppendLine("                                    <td style=\"padding:8px 0;color:#c7d1db;font-size:14px;\">State</td>");
            sb.AppendLine($"                                    <td style=\"padding:8px 0;color:#ffffff;font-size:14px;\">{stateStr}</td>");
            sb.AppendLine("                                </tr>");

            sb.AppendLine("                            </table>");
        }
        else
        {
            var fallbackText = !string.IsNullOrWhiteSpace(messageOrBody)
                ? messageOrBody
                : $"Event: {cleanEventType}";

            var encodedFallback = WebUtility.HtmlEncode(fallbackText).Replace("\n", "<br>");

            sb.AppendLine("                            <div style=\"color:#ffffff;font-size:15px;line-height:1.6;\">");
            sb.AppendLine($"                                {encodedFallback}");
            sb.AppendLine("                            </div>");
        }

        // Diagnostic details callout box for errors or health issues
        var isErrorOrHealth = cleanEventType.Contains("health", StringComparison.OrdinalIgnoreCase) ||
            cleanEventType.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            cleanEventType.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            cleanEventType.Contains("issue", StringComparison.OrdinalIgnoreCase);

        var diagnosticMessage = !string.IsNullOrWhiteSpace(errorMessage)
            ? errorMessage
            : (isErrorOrHealth && torrent != null ? messageOrBody : null);

        if (!string.IsNullOrWhiteSpace(diagnosticMessage) || isErrorOrHealth)
        {
            var detailText = !string.IsNullOrWhiteSpace(diagnosticMessage)
                ? diagnosticMessage
                : (!string.IsNullOrWhiteSpace(messageOrBody) ? messageOrBody : "An unexpected issue was encountered.");

            var encodedDetails = WebUtility.HtmlEncode(detailText).Replace("\n", "<br>");

            sb.AppendLine("                            <table role=\"presentation\" width=\"100%\" border=\"0\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:20px;background-color:#2d1b20;border-left:4px solid #ef4444;border-radius:4px;\">");
            sb.AppendLine("                                <tr>");
            sb.AppendLine("                                    <td style=\"padding:16px;\">");
            sb.AppendLine("                                        <div style=\"color:#ef4444;font-weight:600;font-size:14px;margin-bottom:6px;\">Diagnostic Details</div>");
            sb.AppendLine($"                                        <div style=\"color:#c7d1db;font-size:13px;line-height:1.5;font-family:monospace;white-space:pre-wrap;word-break:break-word;\">{encodedDetails}</div>");
            sb.AppendLine("                                    </td>");
            sb.AppendLine("                                </tr>");
            sb.AppendLine("                            </table>");
        }

        sb.AppendLine("                        </td>");
        sb.AppendLine("                    </tr>");
        sb.AppendLine("                    <!-- Footer -->");
        sb.AppendLine("                    <tr>");
        sb.AppendLine("                        <td style=\"padding:16px 24px;border-top:1px solid #2d333b;text-align:center;font-size:12px;color:#c7d1db;\">");
        sb.AppendLine("                            Sent automatically by Seedarr");
        sb.AppendLine("                        </td>");
        sb.AppendLine("                    </tr>");
        sb.AppendLine("                </table>");
        sb.AppendLine("            </td>");
        sb.AppendLine("        </tr>");
        sb.AppendLine("    </table>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    public static string GetBadgeColor(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "#3b82f6";
        }

        var normalized = eventType.Trim().ToLowerInvariant();

        if (normalized.Contains("complete"))
        {
            return "#22c55e";
        }

        if (normalized.Contains("added") || normalized.Contains("started"))
        {
            return "#3b82f6";
        }

        if (normalized.Contains("goal") || normalized.Contains("paused") || normalized.Contains("warning") || normalized.Contains("ratio"))
        {
            return "#eab308";
        }

        if (normalized.Contains("health") || normalized.Contains("failed") || normalized.Contains("error") || normalized.Contains("issue"))
        {
            return "#ef4444";
        }

        return "#3b82f6";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        return bytes switch
        {
            >= 1024L * 1024L * 1024L * 1024L => $"{(double)bytes / (1024L * 1024L * 1024L * 1024L):F2} TB",
            >= 1024L * 1024L * 1024L => $"{(double)bytes / (1024L * 1024L * 1024L):F2} GB",
            >= 1024L * 1024L => $"{(double)bytes / (1024L * 1024L):F2} MB",
            >= 1024L => $"{(double)bytes / 1024L:F2} KB",
            _ => $"{bytes} B"
        };
    }
}
