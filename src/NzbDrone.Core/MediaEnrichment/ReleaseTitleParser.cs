using System.Text.RegularExpressions;
using NzbDrone.Core.Notifications;

namespace NzbDrone.Core.MediaEnrichment;

public class ParsedReleaseInfo
{
    public string OriginalTitle { get; set; } = string.Empty;

    public string CleanTitle { get; set; } = string.Empty;

    public int? Year { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public int? AbsoluteEpisodeNumber { get; set; }

    public string Resolution { get; set; }

    public string Source { get; set; }

    public string Codec { get; set; }

    public string Audio { get; set; }

    public string ReleaseGroup { get; set; }

    public string ReleaseType { get; set; }

    public string Edition { get; set; }
}

public static class ReleaseTitleParser
{
    private static readonly Regex ExtensionRegex = new(
        @"(?i)\.(torrent|mkv|mp4|avi|ts|m2ts|wmv|mov|iso|nfo|flv|webm|mpg|mpeg)$",
        RegexOptions.Compiled);

    private static readonly Regex LeadingBracketRegex = new(
        @"^\[(?<group>[^\]]+)\]\s*",
        RegexOptions.Compiled);

    private static readonly Regex Crc32Regex = new(
        @"\[(?i:[0-9A-Fa-f]{8})\]",
        RegexOptions.Compiled);

    private static readonly Regex TrailingBracketMetadataRegex = new(
        @"\[(?i:(?:1080p|720p|2160p|4k|x264|x265|hevc|h264|h265|bluray|bdrip|web-dl|webrip|aac|flac|10bit|dual[- ]audio|multi(?:ple)?[- ]sub(?:title)?s?)[^\]]*)\]",
        RegexOptions.Compiled);

    private static readonly Regex ParenthesizedMetadataRegex = new(
        @"\((?i:(?:1080p|720p|2160p|4k|x264|x265|hevc|h264|h265|bluray|bdrip|web-dl|webrip|aac|flac|10bit|dual[- ]audio)[^)]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex ParenthesizedYearRegex = new(
        @"\((?<year>19\d{2}|20\d{2})\)",
        RegexOptions.Compiled);

    private static readonly Regex DelimitedYearRegex = new(
        @"(?:[\s\._\(\[])(?<year>19\d{2}|20\d{2})(?:$|[\s\._\)\],-])",
        RegexOptions.Compiled);

    private static readonly Regex StandardTvRegex = new(
        @"(?i)(?:[\s\._])S(?<season>\d{1,2})E(?<ep>\d{1,3})(?:-?E(?<ep2>\d{1,3}))?(?:$|[\s\._\-])",
        RegexOptions.Compiled);

    private static readonly Regex SeasonOnlyRegex = new(
        @"(?i)(?:[\s\._])(?:S(?<season>\d{1,2})|Season[\s\._]+(?<season>\d{1,2}))(?:$|[\s\._\-])",
        RegexOptions.Compiled);

    private static readonly Regex OrdinalSeasonRegex = new(
        @"(?i)(?:[\s\._])(?<season>\d{1,2})(?:st|nd|rd|th)[\s\._]+Season(?:$|[\s\._\-])",
        RegexOptions.Compiled);

    private static readonly Regex AnimeEpisodeRegex = new(
        @"(?i)(?:\s+-\s+|\s+EP?|\s+E)(?<ep>\d{1,4})(?:v\d+)?(?:\s+|$|[\._\(])",
        RegexOptions.Compiled);

    private static readonly Regex TrailingSceneGroupRegex = new(
        @"-(?<group>[A-Za-z0-9_]+)$",
        RegexOptions.Compiled);

    private static readonly Regex ResolutionRegex = new(
        @"(?i)\b(?<res>2160p|4k|uhd|1080p|1080i|720p|576p|480p)\b",
        RegexOptions.Compiled);

    private static readonly Regex SourceRegex = new(
        @"(?i)\b(?<source>Blu-?Ray|BDRip|BRRip|BD-?Remux|Remux|WEB-?DL|WEBRip|WEB|HDTV|DVDRip|DVD)\b",
        RegexOptions.Compiled);

    private static readonly Regex CodecRegex = new(
        @"(?i)\b(?<codec>x264|x265|h\.?264|h\.?265|hevc|av1|xvid|divx)\b",
        RegexOptions.Compiled);

    private static readonly Regex AudioRegex = new(
        @"(?i)\b(?<audio>DTS-HD(?:\s+MA)?|TrueHD|Atmos|DTS|FLAC|AAC|AC3|EAC3|DD\+?5\.1|DD\+|MP3)\b",
        RegexOptions.Compiled);

    private static readonly Regex SceneDelimiterRegex = new(
        @"(?i)\b(2160p|4k|uhd|1080p|1080i|720p|576p|480p|bluray|blu-ray|bdrip|brrip|remux|web-dl|webrip|webdl|hdtv|dvdrip|x264|x265|h264|h265|hevc|av1|xvid|repack|proper|extended|unrated|criterion|remastered|director'?s?[ ._-]?cut|special[ ._-]?edition|theatrical)\b",
        RegexOptions.Compiled);

    public static ParsedReleaseInfo Parse(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return new ParsedReleaseInfo
            {
                OriginalTitle = rawTitle ?? string.Empty,
                CleanTitle = string.Empty,
                ReleaseType = "Unknown",
            };
        }

        var info = new ParsedReleaseInfo
        {
            OriginalTitle = rawTitle.Trim(),
        };

        var working = info.OriginalTitle;

        // 1. Strip file extension
        working = ExtensionRegex.Replace(working, string.Empty);

        // 2. Extract and strip leading release group brackets
        var leadingMatch = LeadingBracketRegex.Match(working);
        if (leadingMatch.Success)
        {
            var groupCandidate = leadingMatch.Groups["group"].Value.Trim();
            if (!ResolutionRegex.IsMatch(groupCandidate) &&
                !CodecRegex.IsMatch(groupCandidate) &&
                !SourceRegex.IsMatch(groupCandidate))
            {
                info.ReleaseGroup = groupCandidate;
            }

            working = working.Substring(leadingMatch.Length).Trim();
        }

        // 3. Strip CRC32 hashes
        working = Crc32Regex.Replace(working, string.Empty).Trim();

        // 4. Extract structured attributes before stripping
        ExtractAttributes(working, info);

        // 5. Extract trailing scene group if not set
        if (string.IsNullOrEmpty(info.ReleaseGroup))
        {
            var trailingGroupMatch = TrailingSceneGroupRegex.Match(working);
            if (trailingGroupMatch.Success)
            {
                var candidate = trailingGroupMatch.Groups["group"].Value.Trim();
                if (!ResolutionRegex.IsMatch(candidate) &&
                    !CodecRegex.IsMatch(candidate) &&
                    !SourceRegex.IsMatch(candidate) &&
                    !AudioRegex.IsMatch(candidate))
                {
                    info.ReleaseGroup = candidate;
                    working = working.Substring(0, trailingGroupMatch.Index).Trim();
                }
            }
        }

        // 6. Strip bracketed / parenthesized quality and subtitle metadata
        working = TrailingBracketMetadataRegex.Replace(working, string.Empty);
        working = ParenthesizedMetadataRegex.Replace(working, string.Empty).Trim();

        // 7. TV Season / Episode detection
        int? tvBoundary = null;
        var standardTvMatch = StandardTvRegex.Match(working);
        if (standardTvMatch.Success && standardTvMatch.Index > 0)
        {
            if (int.TryParse(standardTvMatch.Groups["season"].Value, out var s))
            {
                info.SeasonNumber = s;
            }

            if (int.TryParse(standardTvMatch.Groups["ep"].Value, out var e))
            {
                info.EpisodeNumber = e;
            }

            tvBoundary = standardTvMatch.Index;
        }
        else
        {
            var ordinalSeasonMatch = OrdinalSeasonRegex.Match(working);
            if (ordinalSeasonMatch.Success && ordinalSeasonMatch.Index > 0)
            {
                if (int.TryParse(ordinalSeasonMatch.Groups["season"].Value, out var s))
                {
                    info.SeasonNumber = s;
                }

                tvBoundary = ordinalSeasonMatch.Index;
            }
            else
            {
                var seasonOnlyMatch = SeasonOnlyRegex.Match(working);
                if (seasonOnlyMatch.Success && seasonOnlyMatch.Index > 0)
                {
                    if (int.TryParse(seasonOnlyMatch.Groups["season"].Value, out var s))
                    {
                        info.SeasonNumber = s;
                    }

                    tvBoundary = seasonOnlyMatch.Index;
                }
            }
        }

        // 8. Anime episode detection
        int? animeBoundary = null;
        var animeMatch = AnimeEpisodeRegex.Match(working);
        if (animeMatch.Success && animeMatch.Index > 0)
        {
            if (int.TryParse(animeMatch.Groups["ep"].Value, out var ep))
            {
                info.EpisodeNumber = ep;
                info.AbsoluteEpisodeNumber = ep;
                animeBoundary = animeMatch.Index;
            }
        }

        // 9. Year detection
        int? yearBoundary = null;
        var parenYearMatch = ParenthesizedYearRegex.Match(working);
        if (parenYearMatch.Success && parenYearMatch.Index > 0)
        {
            if (int.TryParse(parenYearMatch.Groups["year"].Value, out var y))
            {
                info.Year = y;
                yearBoundary = parenYearMatch.Index;
            }
        }
        else
        {
            var yearMatches = DelimitedYearRegex.Matches(working);
            for (var i = yearMatches.Count - 1; i >= 0; i--)
            {
                var match = yearMatches[i];
                if (match.Index > 0)
                {
                    if (tvBoundary.HasValue && match.Index > tvBoundary.Value)
                    {
                        continue;
                    }

                    if (int.TryParse(match.Groups["year"].Value, out var y))
                    {
                        info.Year = y;
                        yearBoundary = match.Index;
                        break;
                    }
                }
            }
        }

        // 10. Scene delimiter boundary
        int? sceneBoundary = null;
        var sceneMatch = SceneDelimiterRegex.Match(working);
        if (sceneMatch.Success && sceneMatch.Index > 0)
        {
            sceneBoundary = sceneMatch.Index;
        }

        // 11. Select boundary for clean title
        var boundary = working.Length;
        if (yearBoundary.HasValue && yearBoundary.Value < boundary)
        {
            boundary = yearBoundary.Value;
        }

        if (tvBoundary.HasValue && tvBoundary.Value < boundary)
        {
            boundary = tvBoundary.Value;
        }

        if (animeBoundary.HasValue && animeBoundary.Value < boundary)
        {
            boundary = animeBoundary.Value;
        }

        if (sceneBoundary.HasValue && sceneBoundary.Value < boundary)
        {
            boundary = sceneBoundary.Value;
        }

        var clean = working.Substring(0, boundary).Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = working.Trim();
        }

        // 12. Normalize title whitespace and separators
        clean = clean.Replace('.', ' ').Replace('_', ' ').Replace('+', ' ');
        clean = Regex.Replace(clean, @"\s+", " ").Trim(' ', '-', '_', '.', ':', ',', ';');

        info.CleanTitle = clean;

        // 13. Determine ReleaseType
        if (info.SeasonNumber.HasValue || info.EpisodeNumber.HasValue)
        {
            info.ReleaseType = animeBoundary.HasValue || (!string.IsNullOrEmpty(info.ReleaseGroup) && info.AbsoluteEpisodeNumber.HasValue)
                ? "Anime"
                : "Series";
        }
        else if (info.Year.HasValue)
        {
            info.ReleaseType = "Movie";
        }
        else
        {
            info.ReleaseType = "Unknown";
        }

        return info;
    }

    public static string CleanTitle(string rawTitle)
    {
        return Parse(rawTitle).CleanTitle;
    }

    public static int? ExtractYear(string rawTitle)
    {
        return Parse(rawTitle).Year;
    }

    private static void ExtractAttributes(string text, ParsedReleaseInfo info)
    {
        var resMatch = ResolutionRegex.Match(text);
        if (resMatch.Success)
        {
            var res = resMatch.Groups["res"].Value.ToLowerInvariant();
            info.Resolution = res switch
            {
                "4k" or "uhd" or "2160p" => "2160p",
                "1080i" or "1080p" => "1080p",
                "720p" => "720p",
                "576p" or "480p" => "480p",
                _ => resMatch.Groups["res"].Value,
            };
        }

        var srcMatch = SourceRegex.Match(text);
        if (srcMatch.Success)
        {
            var src = srcMatch.Groups["source"].Value.ToLowerInvariant();
            info.Source = src switch
            {
                "bluray" or "blu-ray" => "BluRay",
                "remux" or "bd-remux" => "Remux",
                "web-dl" or "webdl" => "WEB-DL",
                "webrip" => "WEBRip",
                "web" => "WEB",
                "hdtv" => "HDTV",
                "dvdrip" or "dvd" => "DVD",
                "bdrip" or "brrip" => "BDRip",
                _ => srcMatch.Groups["source"].Value,
            };
        }

        var codecMatch = CodecRegex.Match(text);
        if (codecMatch.Success)
        {
            var cod = codecMatch.Groups["codec"].Value.ToLowerInvariant();
            info.Codec = cod switch
            {
                "x264" or "h264" or "h.264" => "x264",
                "x265" or "h265" or "h.265" or "hevc" => "x265",
                "av1" => "AV1",
                "xvid" or "divx" => "XviD",
                _ => codecMatch.Groups["codec"].Value,
            };
        }

        var audioMatch = AudioRegex.Match(text);
        if (audioMatch.Success)
        {
            var aud = audioMatch.Groups["audio"].Value.ToLowerInvariant();
            info.Audio = aud switch
            {
                "dts-hd ma" or "dts-hd" => "DTS-HD",
                "truehd" => "TrueHD",
                "atmos" => "Atmos",
                "dts" => "DTS",
                "flac" => "FLAC",
                "aac" => "AAC",
                "ac3" => "AC3",
                "eac3" or "dd+" or "ddp5.1" => "EAC3",
                "dd5.1" => "DD5.1",
                "mp3" => "MP3",
                _ => audioMatch.Groups["audio"].Value,
            };
        }

        info.Edition = EpisodicParser.ExtractEditionStatic(text);
    }
}
