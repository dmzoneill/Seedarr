using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Indexers;

public class ParsedReleaseQuality
{
    public string Resolution { get; set; }
    public string Source { get; set; }
    public string Codec { get; set; }
    public string AudioChannels { get; set; }
    public string AudioCodec { get; set; }
}

public static class ReleaseQualityParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    // Resolution regex patterns
    private static readonly Regex Res2160pRegex = new(@"(?i)\b(2160p|4k|uhd)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex Res1080pRegex = new(@"(?i)\b(1080p|1080i)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex Res720pRegex = new(@"(?i)\b(720p)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex ResSdRegex = new(@"(?i)\b(576p|480p|576i|480i|sd)\b", RegexOptions.Compiled, RegexTimeout);

    // Source regex patterns (evaluated in priority order: Remux -> WEB-DL -> WEBRip -> BluRay -> HDTV -> DVD)
    private static readonly Regex RemuxRegex = new(@"(?i)\b(bd-?remux|remux)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex WebDlRegex = new(@"(?i)\b(web-?dl|webdl)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex WebRipRegex = new(@"(?i)\b(web-?rip|webrip)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex BluRayRegex = new(@"(?i)\b(blu-?ray|bdrip|brrip|bd25|bd50|bd66|bd100)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex HdtvRegex = new(@"(?i)\b(hdtv|pdtv|dsr|dsrip|tvrip)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex DvdRegex = new(@"(?i)\b(dvd-?rip|dvd-?r|dvd5|dvd9|dvd)\b", RegexOptions.Compiled, RegexTimeout);

    // Codec regex patterns
    private static readonly Regex Av1Regex = new(@"(?i)\b(av1)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex HevcRegex = new(@"(?i)\b(x265|h\.?265|hevc)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex AvcRegex = new(@"(?i)\b(x264|h\.?264|avc)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex XvidRegex = new(@"(?i)\b(xvid|divx)\b", RegexOptions.Compiled, RegexTimeout);

    // Audio channels regex patterns
    private static readonly Regex Audio71Regex = new(@"(?i)(?:\b|(?<=[a-z]))(7\.1|8ch)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex Audio51Regex = new(@"(?i)(?:\b|(?<=[a-z]))(5\.1|6ch)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex Audio20Regex = new(@"(?i)(?:\b|(?<=[a-z]))(2\.0|2ch|stereo)\b", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex Audio10Regex = new(@"(?i)(?:\b|(?<=[a-z]))(1\.0|1ch|mono)\b", RegexOptions.Compiled, RegexTimeout);

    // Audio codec regex patterns
    private static readonly Regex AudioCodecRegex = new(@"\b(dts-hd(?:\s+ma)?|truehd|atmos|dts|flac|eac3|dd\+?(?:\d\.\d)?|ac3|aac|mp3|opus)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    public static ParsedReleaseQuality Parse(string rawTitle)
    {
        var result = new ParsedReleaseQuality();
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return result;
        }

        var normalized = rawTitle.Replace('_', ' ');

        // 1. Resolution
        if (Res2160pRegex.IsMatch(normalized))
        {
            result.Resolution = "2160p";
        }
        else if (Res1080pRegex.IsMatch(normalized))
        {
            result.Resolution = "1080p";
        }
        else if (Res720pRegex.IsMatch(normalized))
        {
            result.Resolution = "720p";
        }
        else if (ResSdRegex.IsMatch(normalized))
        {
            result.Resolution = "SD";
        }

        // 2. Source (Remux before BluRay; WEB-DL before WEBRip)
        if (RemuxRegex.IsMatch(normalized))
        {
            result.Source = "Remux";
        }
        else if (WebDlRegex.IsMatch(normalized))
        {
            result.Source = "WEB-DL";
        }
        else if (WebRipRegex.IsMatch(normalized))
        {
            result.Source = "WEBRip";
        }
        else if (BluRayRegex.IsMatch(normalized))
        {
            result.Source = "BluRay";
        }
        else if (HdtvRegex.IsMatch(normalized))
        {
            result.Source = "HDTV";
        }
        else if (DvdRegex.IsMatch(normalized))
        {
            result.Source = "DVD";
        }

        // 3. Codec
        if (Av1Regex.IsMatch(normalized))
        {
            result.Codec = "AV1";
        }
        else if (HevcRegex.IsMatch(normalized))
        {
            result.Codec = "HEVC";
        }
        else if (AvcRegex.IsMatch(normalized))
        {
            result.Codec = "AVC";
        }
        else if (XvidRegex.IsMatch(normalized))
        {
            result.Codec = "XviD";
        }

        // 4. Audio Channels
        if (Audio71Regex.IsMatch(normalized))
        {
            result.AudioChannels = "7.1";
        }
        else if (Audio51Regex.IsMatch(normalized))
        {
            result.AudioChannels = "5.1";
        }
        else if (Audio20Regex.IsMatch(normalized))
        {
            result.AudioChannels = "2.0";
        }
        else if (Audio10Regex.IsMatch(normalized))
        {
            result.AudioChannels = "1.0";
        }

        // 5. Audio Codec
        var audioMatch = AudioCodecRegex.Match(normalized);
        if (audioMatch.Success)
        {
            var aud = audioMatch.Value.ToUpperInvariant().Replace(" ", "");
            result.AudioCodec = aud switch
            {
                var s when s.Contains("TRUEHD") => "TrueHD",
                var s when s.Contains("ATMOS") => "Atmos",
                var s when s.Contains("DTS-HD") || s.Contains("DTSHD") => "DTS-HD MA",
                var s when s.Contains("DTS") => "DTS",
                var s when s.Contains("EAC3") || s.Contains("DD+") => "EAC3",
                var s when s.Contains("AC3") || s.StartsWith("DD") => "AC3",
                var s when s.Contains("FLAC") => "FLAC",
                var s when s.Contains("AAC") => "AAC",
                var s when s.Contains("OPUS") => "Opus",
                var s when s.Contains("MP3") => "MP3",
                _ => audioMatch.Value
            };
        }

        return result;
    }

    public static bool MatchesResolution(string parsedResolution, IEnumerable<string> allowedResolutions)
    {
        if (allowedResolutions == null)
        {
            return true;
        }

        var allowedList = allowedResolutions.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (allowedList.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(parsedResolution))
        {
            return false;
        }

        foreach (var allowed in allowedList)
        {
            var trimmed = allowed.Trim();
            if (string.Equals(parsedResolution, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Synonym mapping
            if (string.Equals(parsedResolution, "2160p", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(trimmed, "4k", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "uhd", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (string.Equals(parsedResolution, "1080p", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(trimmed, "1080i", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "fhd", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (string.Equals(parsedResolution, "720p", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(trimmed, "hd", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(parsedResolution, "SD", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(trimmed, "480p", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "576p", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "480i", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "576i", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesSource(string parsedSource, IEnumerable<string> allowedSources)
    {
        if (allowedSources == null)
        {
            return true;
        }

        var allowedList = allowedSources.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (allowedList.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(parsedSource))
        {
            return false;
        }

        foreach (var allowed in allowedList)
        {
            var trimmed = allowed.Trim();
            if (string.Equals(parsedSource, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Synonym mapping
            if (string.Equals(parsedSource, "Remux", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(trimmed, "BD-Remux", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "BDRemux", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (string.Equals(parsedSource, "BluRay", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(trimmed, "Blu-Ray", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "BDRip", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "BRRip", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (string.Equals(parsedSource, "WEB-DL", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(trimmed, "WEBDL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(parsedSource, "WEBRip", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(trimmed, "WEB-Rip", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(parsedSource, "HDTV", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(trimmed, "PDTV", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "TVRip", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (string.Equals(parsedSource, "DVD", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(trimmed, "DVDRip", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "DVD-R", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesCodec(string parsedCodec, IEnumerable<string> allowedCodecs)
    {
        if (allowedCodecs == null)
        {
            return true;
        }

        var allowedList = allowedCodecs.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (allowedList.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(parsedCodec))
        {
            return false;
        }

        foreach (var allowed in allowedList)
        {
            var trimmed = allowed.Trim();
            if (string.Equals(parsedCodec, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Synonym mapping
            if (string.Equals(parsedCodec, "HEVC", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(trimmed, "x265", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "h265", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "h.265", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "HEVC/x265", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "x265/HEVC", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (string.Equals(parsedCodec, "AVC", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(trimmed, "x264", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "h264", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "h.264", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "AVC/x264", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(trimmed, "x264/AVC", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (string.Equals(parsedCodec, "AV1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(trimmed, "AV1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(parsedCodec, "XviD", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(trimmed, "DivX", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
