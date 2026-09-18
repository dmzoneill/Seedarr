using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Notifications;

public class EpisodicParser : IEpisodicParser
{
    private static readonly Regex SeasonEpisodeRangeRegex = new(
        @"\bS(?<season>\d{1,2})E(?<ep1>\d{1,3})\s*[-–—_~]\s*(?:E|e)?(?<ep2>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonEpisodeAltRangeRegex = new(
        @"\b(?<season>\d{1,2})x(?<ep1>\d{1,3})\s*[-–—_~]\s*(?:\k<season>x|x)?(?<ep2>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonEpisodeMultiRegex = new(
        @"\bS(?<season>\d{1,2})(?<episodes>(?:\s*[._-]?\s*[eE]\d{1,3}){2,})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EditionRegex = new(
        @"(?i)\b(?<edition>director'?s?[ ._-]?cut|extended(?:[ ._-]?(?:cut|edition))?|theatrical(?:[ ._-]?(?:cut|edition))?|remastered|remaster|unrated|uncut|imax(?:[ ._-]?(?:enhanced|edition))?|special[ ._-]?edition|criterion(?:[ ._-]?collection)?)\b",
        RegexOptions.Compiled);

    private static readonly Regex EpisodeSubRegex = new(
        @"[eE](?<ep>\d{1,3})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonEpisodeRegex = new(
        @"\bS(?<season>\d{1,2})E(?<ep>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonEpisodeAltRegex = new(
        @"\b(?<season>\d{1,2})x(?<ep>\d{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DailyShowRegex = new(
        @"\b(?<year>19\d{2}|20\d{2})[._-](?<month>0[1-9]|1[0-2])[._-](?<day>0[1-9]|[12]\d|3[01])\b",
        RegexOptions.Compiled);

    private static readonly Regex AnimeAbsoluteRegex = new(
        @"(?:\s+-\s+)(?<ep>\d{1,4})(?:v\d+)?\b",
        RegexOptions.Compiled);

    private static readonly Regex ExplicitEpisodeRegex = new(
        @"\b(?:Episode|Ep\.?)[ ._-]?(?<ep>\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonPackNamedRegex = new(
        @"\b(?:Season|Series)[ ._]?(?<season>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonPackRangeRegex = new(
        @"\b(?:Season|Series|S)[ ._]?(?<s1>\d{1,2})[-–_~](?:(?:Season|Series|S)[ ._]?)?(?<s2>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SeasonPackShortRegex = new(
        @"\bS(?<season>\d{1,2})\b(?![eE]\d)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CompleteKeywordRegex = new(
        @"\b(?:Complete|Full[ ._]?Season|Season[ ._]?Pack)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public (int? SeasonNumber, int? EpisodeNumber, string EpisodeTitle) ExtractEpisodicInfo(string name)
    {
        return ExtractEpisodicInfoStatic(name);
    }

    public EpisodicReleaseInfo ExtractEpisodicReleaseInfo(string name)
    {
        return ExtractEpisodicReleaseInfoStatic(name);
    }

    public (string ContainerFormat, string Resolution, string VideoCodec, string HdrFormat, string AudioCodec, string AudioChannels, string AudioLanguage, List<string> SubtitleLanguages) ExtractStreamSpecs(string mediaInfoJson)
    {
        return ExtractStreamSpecsStatic(mediaInfoJson);
    }

    public string EscapeMarkdown(string text)
    {
        return EscapeMarkdownStatic(text);
    }

    public string FormatEta(long seconds)
    {
        return FormatEtaStatic(seconds);
    }

    public string ExtractEdition(string name)
    {
        return ExtractEditionStatic(name);
    }

    public static string ExtractEditionStatic(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var match = EditionRegex.Match(name);
        if (!match.Success)
        {
            return null;
        }

        var val = match.Groups["edition"].Value.ToLowerInvariant().Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        if (val.Contains("director"))
        {
            return "Director's Cut";
        }

        if (val.Contains("extended"))
        {
            if (val.Contains("cut"))
            {
                return "Extended Cut";
            }

            if (val.Contains("edition"))
            {
                return "Extended Edition";
            }

            return "Extended";
        }

        if (val.Contains("theatrical"))
        {
            return val.Contains("cut") ? "Theatrical Cut" : "Theatrical";
        }

        if (val.Contains("remaster"))
        {
            return "Remastered";
        }

        if (val.Contains("unrated") || val.Contains("uncut"))
        {
            return "Unrated";
        }

        if (val.Contains("imax"))
        {
            return "IMAX";
        }

        if (val.Contains("special"))
        {
            return "Special Edition";
        }

        if (val.Contains("criterion"))
        {
            return "Criterion";
        }

        return match.Groups["edition"].Value;
    }

    public static (int? SeasonNumber, int? EpisodeNumber, string EpisodeTitle) ExtractEpisodicInfoStatic(string name)
    {
        var info = ExtractEpisodicReleaseInfoStatic(name);
        return (info.SeasonNumber, info.EpisodeNumber, info.EpisodeTitle);
    }

    public static EpisodicReleaseInfo ExtractEpisodicReleaseInfoStatic(string name)
    {
        var info = new EpisodicReleaseInfo();
        if (string.IsNullOrWhiteSpace(name))
        {
            return info;
        }

        info.Edition = ExtractEditionStatic(name);

        var dailyMatch = DailyShowRegex.Match(name);
        if (dailyMatch.Success &&
            int.TryParse(dailyMatch.Groups["year"].Value, out var year) &&
            int.TryParse(dailyMatch.Groups["month"].Value, out var month) &&
            int.TryParse(dailyMatch.Groups["day"].Value, out var day))
        {
            try
            {
                var dt = new DateTime(year, month, day);
                info.AirDate = dt.ToString("yyyy-MM-dd");
            }
            catch
            {
                // Ignore invalid calendar dates
            }
        }

        var rangeMatch = SeasonEpisodeRangeRegex.Match(name);
        if (rangeMatch.Success &&
            int.TryParse(rangeMatch.Groups["season"].Value, out var sRange) &&
            int.TryParse(rangeMatch.Groups["ep1"].Value, out var ep1) &&
            int.TryParse(rangeMatch.Groups["ep2"].Value, out var ep2) &&
            ep1 <= ep2 && ep2 - ep1 <= 100)
        {
            info.SeasonNumber = sRange;
            info.EpisodeNumber = ep1;
            info.EndingEpisodeNumber = ep2;
            for (var i = ep1; i <= ep2; i++)
            {
                info.EpisodeNumbers.Add(i);
            }

            return info;
        }

        var altRangeMatch = SeasonEpisodeAltRangeRegex.Match(name);
        if (altRangeMatch.Success &&
            int.TryParse(altRangeMatch.Groups["season"].Value, out var sAltRange) &&
            int.TryParse(altRangeMatch.Groups["ep1"].Value, out var epAlt1) &&
            int.TryParse(altRangeMatch.Groups["ep2"].Value, out var epAlt2) &&
            epAlt1 <= epAlt2 && epAlt2 - epAlt1 <= 100)
        {
            info.SeasonNumber = sAltRange;
            info.EpisodeNumber = epAlt1;
            info.EndingEpisodeNumber = epAlt2;
            for (var i = epAlt1; i <= epAlt2; i++)
            {
                info.EpisodeNumbers.Add(i);
            }

            return info;
        }

        var multiMatch = SeasonEpisodeMultiRegex.Match(name);
        if (multiMatch.Success &&
            int.TryParse(multiMatch.Groups["season"].Value, out var sMulti))
        {
            var subMatches = EpisodeSubRegex.Matches(multiMatch.Groups["episodes"].Value);
            if (subMatches.Count >= 2)
            {
                info.SeasonNumber = sMulti;
                foreach (Match sub in subMatches)
                {
                    if (int.TryParse(sub.Groups["ep"].Value, out var epVal))
                    {
                        info.EpisodeNumbers.Add(epVal);
                    }
                }

                if (info.EpisodeNumbers.Count > 0)
                {
                    info.EpisodeNumbers.Sort();
                    info.EpisodeNumber = info.EpisodeNumbers[0];
                    info.EndingEpisodeNumber = info.EpisodeNumbers[^1];
                    return info;
                }
            }
        }

        var match = SeasonEpisodeRegex.Match(name);
        if (match.Success &&
            int.TryParse(match.Groups["season"].Value, out var s) &&
            int.TryParse(match.Groups["ep"].Value, out var e))
        {
            info.SeasonNumber = s;
            info.EpisodeNumber = e;
            info.EpisodeNumbers.Add(e);
            return info;
        }

        var matchAlt = SeasonEpisodeAltRegex.Match(name);
        if (matchAlt.Success &&
            int.TryParse(matchAlt.Groups["season"].Value, out var sAlt) &&
            int.TryParse(matchAlt.Groups["ep"].Value, out var eAlt))
        {
            info.SeasonNumber = sAlt;
            info.EpisodeNumber = eAlt;
            info.EpisodeNumbers.Add(eAlt);
            return info;
        }

        var sShortMatch = SeasonPackShortRegex.Match(name);
        if (sShortMatch.Success && int.TryParse(sShortMatch.Groups["season"].Value, out var animeSeason))
        {
            info.SeasonNumber = animeSeason;
        }

        var animeMatch = AnimeAbsoluteRegex.Match(name);
        if (animeMatch.Success &&
            int.TryParse(animeMatch.Groups["ep"].Value, out var absEp) &&
            !(absEp >= 1900 && absEp <= 2099))
        {
            info.AbsoluteEpisodeNumber = absEp;
            info.EpisodeNumber = absEp;
            info.EpisodeNumbers.Add(absEp);
            return info;
        }

        var explicitEpMatch = ExplicitEpisodeRegex.Match(name);
        if (explicitEpMatch.Success &&
            int.TryParse(explicitEpMatch.Groups["ep"].Value, out var explicitEp))
        {
            info.AbsoluteEpisodeNumber = explicitEp;
            info.EpisodeNumber = explicitEp;
            info.EpisodeNumbers.Add(explicitEp);
            return info;
        }

        var seasonRangeMatch = SeasonPackRangeRegex.Match(name);
        if (seasonRangeMatch.Success &&
            int.TryParse(seasonRangeMatch.Groups["s1"].Value, out var sRangeStart))
        {
            info.SeasonNumber = sRangeStart;
            info.IsSeasonPack = true;
            return info;
        }

        var seasonNamedMatch = SeasonPackNamedRegex.Match(name);
        if (seasonNamedMatch.Success &&
            int.TryParse(seasonNamedMatch.Groups["season"].Value, out var sNamed))
        {
            info.SeasonNumber = sNamed;
            info.IsSeasonPack = true;
            return info;
        }

        if (sShortMatch.Success && int.TryParse(sShortMatch.Groups["season"].Value, out var sShort))
        {
            info.SeasonNumber = sShort;
            info.IsSeasonPack = true;
            return info;
        }

        if (CompleteKeywordRegex.IsMatch(name))
        {
            info.IsSeasonPack = true;
            return info;
        }

        return info;
    }

    public static (string ContainerFormat, string Resolution, string VideoCodec, string HdrFormat, string AudioCodec, string AudioChannels, string AudioLanguage, List<string> SubtitleLanguages) ExtractStreamSpecsStatic(string mediaInfoJson)
    {
        if (string.IsNullOrWhiteSpace(mediaInfoJson))
        {
            return (null, null, null, null, null, null, null, new List<string>());
        }

        try
        {
            using var doc = JsonDocument.Parse(mediaInfoJson);
            var root = doc.RootElement;
            var container = root.TryGetProperty("ContainerFormat", out var c) ? c.GetString() : null;
            var resolution = root.TryGetProperty("Resolution", out var r) ? r.GetString() : null;
            var videoCodec = root.TryGetProperty("VideoCodec", out var v) ? v.GetString() : null;
            var hdr = root.TryGetProperty("HdrFormat", out var h) ? h.GetString() : null;
            var audioCodec = root.TryGetProperty("AudioCodec", out var a) ? a.GetString() : null;
            var audioChannels = root.TryGetProperty("AudioChannels", out var ac) ? ac.GetString() : null;
            var audioLanguage = root.TryGetProperty("AudioLanguage", out var al) ? al.GetString() : null;
            var subtitleLanguages = new List<string>();

            if (root.TryGetProperty("SubtitleTracks", out var st) && st.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in st.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        subtitleLanguages.Add(s);
                    }
                }
            }

            return (container, resolution, videoCodec, hdr, audioCodec, audioChannels, audioLanguage, subtitleLanguages);
        }
        catch
        {
            return (null, null, null, null, null, null, null, new List<string>());
        }
    }

    public static string EscapeMarkdownStatic(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            if (c is '_' or '*' or '[' or ']' or '(' or ')' or '~' or '`' or '>' or '#' or '+' or '-' or '=' or '|' or '{' or '}' or '.' or '!' or '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    public static string FormatEtaStatic(long seconds)
    {
        if (seconds <= 0 || seconds >= 8640000)
        {
            return "00:00:00";
        }

        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 24
            ? $"{(int)ts.TotalDays}d {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}
