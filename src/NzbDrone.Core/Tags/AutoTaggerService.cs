using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Tags;

public class AutoTaggerService : IAutoTaggerService,
    IHandle<TorrentAddedEvent>,
    IHandle<MediaEnrichedEvent>,
    IHandle<TorrentDownloadCompletedEvent>
{
    private readonly IAutoTaggerRuleRepository _ruleRepository;
    private readonly ITorrentService _torrentService;
    private readonly ITagService _tagService;
    private readonly Logger _logger;

    public AutoTaggerService(
        IAutoTaggerRuleRepository ruleRepository,
        ITorrentService torrentService,
        ITagService tagService = null,
        Logger logger = null)
    {
        _ruleRepository = ruleRepository;
        _torrentService = torrentService;
        _tagService = tagService;
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public List<AutoTaggerRule> GetAllRules()
    {
        return _ruleRepository.All()?.ToList() ?? new List<AutoTaggerRule>();
    }

    public AutoTaggerRule GetRule(int id)
    {
        return _ruleRepository.Get(id);
    }

    public AutoTaggerRule AddRule(AutoTaggerRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            throw new ArgumentException("Rule name cannot be empty.", nameof(rule));
        }

        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            throw new ArgumentException("Rule pattern cannot be empty.", nameof(rule));
        }

        _logger.Info("Adding auto-tagger rule '{0}' (Type: {1}, TagId: {2})", rule.Name, rule.RuleType, rule.TagId);
        return _ruleRepository.Insert(rule);
    }

    public AutoTaggerRule UpdateRule(AutoTaggerRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            throw new ArgumentException("Rule name cannot be empty.", nameof(rule));
        }

        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            throw new ArgumentException("Rule pattern cannot be empty.", nameof(rule));
        }

        _logger.Info("Updating auto-tagger rule '{0}' (Id: {1})", rule.Name, rule.Id);
        return _ruleRepository.Update(rule);
    }

    public void DeleteRule(int id)
    {
        _logger.Info("Deleting auto-tagger rule {0}", id);
        _ruleRepository.Delete(id);
    }

    public void EvaluateTorrent(Torrent torrent)
    {
        if (torrent == null)
        {
            return;
        }

        var rules = GetAllRules()
            .Where(r => r.IsEnabled)
            .OrderByDescending(r => r.Priority)
            .ToList();

        if (rules.Count == 0)
        {
            return;
        }

        torrent.TagIds ??= new List<int>();
        var modified = false;

        foreach (var rule in rules)
        {
            if (!torrent.TagIds.Contains(rule.TagId) && MatchesRule(torrent, rule))
            {
                _logger.Info(
                    "Torrent '{0}' matched auto-tagger rule '{1}' (Type: {2}), adding tag ID {3}",
                    torrent.Name,
                    rule.Name,
                    rule.RuleType,
                    rule.TagId);

                torrent.TagIds.Add(rule.TagId);
                modified = true;
            }
        }

        if (modified)
        {
            _torrentService?.Update(torrent);
        }
    }

    public void EvaluateAll()
    {
        _logger.Info("Evaluating auto-tagger rules across all torrents");
        var torrents = _torrentService?.GetAll();
        if (torrents == null || torrents.Count == 0)
        {
            return;
        }

        foreach (var torrent in torrents)
        {
            EvaluateTorrent(torrent);
        }
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent != null)
        {
            EvaluateTorrent(message.Torrent);
        }
    }

    public void Handle(MediaEnrichedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var torrent = message.Torrent ?? _torrentService?.Get(message.TorrentId);
        if (torrent != null)
        {
            EvaluateTorrent(torrent);
        }
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent != null)
        {
            EvaluateTorrent(message.Torrent);
        }
    }

    private static bool MatchesRule(Torrent torrent, AutoTaggerRule rule)
    {
        if (torrent == null || rule == null || string.IsNullOrWhiteSpace(rule.Pattern))
        {
            return false;
        }

        return rule.RuleType switch
        {
            AutoTaggerRuleType.Regex => MatchesRegex(torrent, rule.Pattern),
            AutoTaggerRuleType.TrackerDomain => MatchesTrackerDomain(torrent, rule.Pattern),
            AutoTaggerRuleType.Quality => MatchesQuality(torrent, rule.Pattern),
            AutoTaggerRuleType.MediaInfo => MatchesMediaInfo(torrent, rule.Pattern),
            AutoTaggerRuleType.Size => MatchesSize(torrent, rule.Pattern),
            _ => false
        };
    }

    private static bool MatchesRegex(Torrent torrent, string pattern)
    {
        var title = torrent.Title ?? torrent.Name;
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesTrackerDomain(Torrent torrent, string pattern)
    {
        var trackerUrl = torrent.TrackerUrl;
        if (string.IsNullOrWhiteSpace(trackerUrl))
        {
            return false;
        }

        var cleanPattern = pattern.Trim();

        if (cleanPattern.Contains('*'))
        {
            var regexPattern = "^" + Regex.Escape(cleanPattern).Replace("\\*", ".*") + "$";
            if (Regex.IsMatch(trackerUrl, regexPattern, RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (Uri.TryCreate(trackerUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                if (Regex.IsMatch(uri.Host, regexPattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            var stripped = cleanPattern.Trim('*');
            if (!string.IsNullOrWhiteSpace(stripped) && trackerUrl.Contains(stripped, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        else
        {
            if (trackerUrl.Contains(cleanPattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Uri.TryCreate(trackerUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                if (uri.Host.Equals(cleanPattern, StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith("." + cleanPattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MatchesQuality(Torrent torrent, string pattern)
    {
        var quality = torrent.Quality;
        var title = torrent.Title ?? torrent.Name;

        var candidates = new[] { quality, title }.Where(s => !string.IsNullOrWhiteSpace(s));

        foreach (var text in candidates)
        {
            try
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                {
                    return true;
                }
            }
            catch
            {
                if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MatchesMediaInfo(Torrent torrent, string pattern)
    {
        var title = torrent.Title ?? torrent.Name;
        var quality = torrent.Quality;

        var candidates = new[] { title, quality }.Where(s => !string.IsNullOrWhiteSpace(s));

        foreach (var text in candidates)
        {
            try
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                {
                    return true;
                }
            }
            catch
            {
                if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MatchesSize(Torrent torrent, string pattern)
    {
        var sizeBytes = torrent.Size > 0 ? torrent.Size : torrent.TotalSize;
        var trimmed = pattern.Trim();

        var op = ">";
        var sizePart = trimmed;

        if (trimmed.StartsWith(">="))
        {
            op = ">=";
            sizePart = trimmed.Substring(2).Trim();
        }
        else if (trimmed.StartsWith("<="))
        {
            op = "<=";
            sizePart = trimmed.Substring(2).Trim();
        }
        else if (trimmed.StartsWith("=="))
        {
            op = "==";
            sizePart = trimmed.Substring(2).Trim();
        }
        else if (trimmed.StartsWith(">"))
        {
            op = ">";
            sizePart = trimmed.Substring(1).Trim();
        }
        else if (trimmed.StartsWith("<"))
        {
            op = "<";
            sizePart = trimmed.Substring(1).Trim();
        }
        else if (trimmed.StartsWith("="))
        {
            op = "==";
            sizePart = trimmed.Substring(1).Trim();
        }

        if (!TryParseSizeBytes(sizePart, out var targetBytes))
        {
            return false;
        }

        return op switch
        {
            ">" => sizeBytes > targetBytes,
            ">=" => sizeBytes >= targetBytes,
            "<" => sizeBytes < targetBytes,
            "<=" => sizeBytes <= targetBytes,
            "==" => sizeBytes == targetBytes,
            _ => false
        };
    }

    private static bool TryParseSizeBytes(string input, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var clean = input.Trim();
        var match = Regex.Match(clean, @"^([\d\.]+)\s*([a-zA-Z]*)$");
        if (!match.Success)
        {
            return false;
        }

        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
        {
            return false;
        }

        var unit = match.Groups[2].Value.ToUpperInvariant();
        double multiplier = unit switch
        {
            "TB" or "TIB" or "T" => 1024L * 1024 * 1024 * 1024,
            "GB" or "GIB" or "G" => 1024L * 1024 * 1024,
            "MB" or "MIB" or "M" => 1024L * 1024,
            "KB" or "KIB" or "K" => 1024L,
            "B" or "" => 1L,
            _ => 1L
        };

        bytes = (long)(num * multiplier);
        return true;
    }
}
