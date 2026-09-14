#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Automation;

public static class TriggerEvaluator
{
    private static readonly Regex VariableRegex = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    public static Dictionary<string, object?> BuildEvaluationContext(
        Torrent? torrent,
        List<string>? torrentTags = null,
        Dictionary<string, object?>? customInputs = null,
        long? diskFreeSpace = null,
        bool vpnActive = true,
        bool isPortForwarded = true)
    {
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Inputs / Secrets
        if (customInputs != null)
        {
            foreach (var kvp in customInputs)
            {
                context[$"inputs.{kvp.Key}"] = kvp.Value;
                context[$"secrets.{kvp.Key}"] = kvp.Value;
                context[kvp.Key] = kvp.Value;
            }
        }

        // Torrent Context
        if (torrent != null)
        {
            context["torrent.id"] = torrent.Id;
            context["torrent.name"] = torrent.Name ?? string.Empty;
            context["torrent.infoHash"] = torrent.InfoHash ?? string.Empty;
            context["torrent.size"] = torrent.TotalSize;
            context["torrent.totalSize"] = torrent.TotalSize;
            context["torrent.ratio"] = torrent.Ratio;
            context["torrent.uploaded"] = torrent.Uploaded;
            context["torrent.downloaded"] = torrent.Downloaded;
            context["torrent.downloadSpeed"] = torrent.DownloadSpeed;
            context["torrent.uploadSpeed"] = torrent.UploadSpeed;
            context["torrent.seedingTime"] = torrent.SeedingTime;
            context["torrent.seedingTimeMinutes"] = (long)(torrent.SeedingTime / 60);
            context["torrent.category"] = torrent.Category ?? string.Empty;
            context["torrent.savePath"] = torrent.SavePath ?? string.Empty;
            context["torrent.tracker"] = torrent.TrackerUrl ?? string.Empty;
            context["torrent.trackerUrl"] = torrent.TrackerUrl ?? string.Empty;
            context["torrent.status"] = torrent.Status.ToString();
            context["torrent.progress"] = torrent.Progress;
            context["torrent.isPrivate"] = torrent.IsPrivate;
            context["torrent.isComplete"] = torrent.Progress >= 1.0 || torrent.Progress >= 0.999 || torrent.ForceCompleted || torrent.Status == TorrentStatus.Seeding;
            context["torrent.seeders"] = torrent.Seeders;
            context["torrent.leechers"] = torrent.Leechers;

            if (torrentTags != null && torrentTags.Count > 0)
            {
                context["torrent.tags"] = string.Join(",", torrentTags);
            }
        }

        // System Context
        var freeSpace = diskFreeSpace ?? GetAvailableFreeSpace(torrent?.SavePath);
        context["system.diskFreeSpace"] = freeSpace;
        context["system.vpnActive"] = vpnActive;
        context["system.isPortForwarded"] = isPortForwarded;

        return context;
    }

    public static long GetAvailableFreeSpace(string? targetPath = null)
    {
        try
        {
            var path = !string.IsNullOrWhiteSpace(targetPath) && Directory.Exists(targetPath)
                ? targetPath
                : AppContext.BaseDirectory;
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? "/");
            return drive.AvailableFreeSpace;
        }
        catch
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "/");
                return drive.AvailableFreeSpace;
            }
            catch
            {
                return 0;
            }
        }
    }

    public static bool ShouldExecute(AutomationScript script, AutomationTrigger trigger, Torrent? torrent = null)
    {
        if (!script.IsEnabled || script.Trigger != trigger)
        {
            return false;
        }

        if (torrent != null)
        {
            // Category filter
            if (script.TargetCategories != null && script.TargetCategories.Count > 0)
            {
                if (string.IsNullOrEmpty(torrent.Category) || !script.TargetCategories.Any(c => string.Equals(c, torrent.Category, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            // Tag filter
            if (script.TargetTagIds != null && script.TargetTagIds.Count > 0)
            {
                if (torrent.TagIds == null || !script.TargetTagIds.Any(t => torrent.TagIds.Contains(t)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static string SubstituteVariables(string template, Dictionary<string, object?> context)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        return VariableRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (context.TryGetValue(key, out var val) && val != null)
            {
                return val.ToString() ?? string.Empty;
            }

            return match.Value;
        });
    }

    public static bool EvaluateCondition(string condition, Dictionary<string, object?> context)
    {
        var substituted = SubstituteVariables(condition, context);
        if (string.IsNullOrWhiteSpace(substituted))
        {
            return true;
        }

        var trimmed = substituted.Trim();

        if (!trimmed.Contains("==") && !trimmed.Contains("!=") && !trimmed.Contains(">=") && !trimmed.Contains("<=") && !trimmed.Contains('>') && !trimmed.Contains('<'))
        {
            if (trimmed.StartsWith('!'))
            {
                var inner = trimmed[1..].Trim();
                return IsFalsy(inner);
            }

            return !IsFalsy(trimmed);
        }

        if (substituted.Contains("=="))
        {
            var parts = substituted.Split(new[] { "==" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return AreEqual(parts[0], parts[1]);
            }
        }
        else if (substituted.Contains("!="))
        {
            var parts = substituted.Split(new[] { "!=" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return !AreEqual(parts[0], parts[1]);
            }
        }
        else if (substituted.Contains(">="))
        {
            var parts = substituted.Split(new[] { ">=" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && TryParseNumber(parts[0], out var l) && TryParseNumber(parts[1], out var r))
            {
                return l >= r;
            }
        }
        else if (substituted.Contains("<="))
        {
            var parts = substituted.Split(new[] { "<=" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && TryParseNumber(parts[0], out var l) && TryParseNumber(parts[1], out var r))
            {
                return l <= r;
            }
        }
        else if (substituted.Contains('>'))
        {
            var parts = substituted.Split(new[] { '>' }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && TryParseNumber(parts[0], out var l) && TryParseNumber(parts[1], out var r))
            {
                return l > r;
            }
        }
        else if (substituted.Contains('<'))
        {
            var parts = substituted.Split(new[] { '<' }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && TryParseNumber(parts[0], out var l) && TryParseNumber(parts[1], out var r))
            {
                return l < r;
            }
        }

        return !IsFalsy(substituted);
    }

    private static bool TryParseNumber(string raw, out double number)
    {
        var clean = raw.Trim('\'', '"', ' ');
        return double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsFalsy(string val)
    {
        var clean = val.Trim('\'', '"', ' ');
        return string.IsNullOrEmpty(clean)
            || string.Equals(clean, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, "null", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreEqual(string leftRaw, string rightRaw)
    {
        var left = leftRaw.Trim('\'', '"', ' ');
        var right = rightRaw.Trim('\'', '"', ' ');

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TryParseBool(left, out var bLeft) && TryParseBool(right, out var bRight))
        {
            return bLeft == bRight;
        }

        if (TryParseNumber(left, out var nLeft) && TryParseNumber(right, out var nRight))
        {
            return Math.Abs(nLeft - nRight) < 0.000001;
        }

        return false;
    }

    private static bool TryParseBool(string val, out bool result)
    {
        if (bool.TryParse(val, out result))
        {
            return true;
        }

        if (string.Equals(val, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(val, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(val, "on", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(val, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(val, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(val, "off", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }
}
