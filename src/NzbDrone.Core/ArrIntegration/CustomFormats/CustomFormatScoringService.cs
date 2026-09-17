using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.MediaEnrichment;

namespace NzbDrone.Core.ArrIntegration.CustomFormats;

public class CustomFormatScoringService : ICustomFormatScoringService
{
    public CustomFormatScoreResult Evaluate(string releaseTitle, IEnumerable<CustomFormat> customFormats, IDictionary<int, int> profileScores)
    {
        var result = new CustomFormatScoreResult();

        if (string.IsNullOrWhiteSpace(releaseTitle) || customFormats == null)
        {
            return result;
        }

        var parsed = ReleaseTitleParser.Parse(releaseTitle);

        foreach (var format in customFormats)
        {
            if (format == null)
            {
                continue;
            }

            if (IsCustomFormatMatch(format, releaseTitle, parsed))
            {
                result.MatchedFormats.Add(format.Name ?? string.Empty);

                if (profileScores != null && profileScores.TryGetValue(format.Id, out var score))
                {
                    result.TotalScore += score;
                }
            }
        }

        return result;
    }

    public CustomFormatScoreResult Evaluate(string releaseTitle, IEnumerable<CustomFormat> customFormats)
    {
        return Evaluate(releaseTitle, customFormats, null);
    }

    private static bool IsCustomFormatMatch(CustomFormat format, string releaseTitle, ParsedReleaseInfo parsed)
    {
        if (format.Specifications == null || format.Specifications.Count == 0)
        {
            return false;
        }

        var requiredSpecs = format.Specifications.Where(s => s.Required).ToList();
        var optionalSpecs = format.Specifications.Where(s => !s.Required).ToList();

        foreach (var spec in requiredSpecs)
        {
            if (!EvaluateSpecification(spec, releaseTitle, parsed))
            {
                return false;
            }
        }

        if (optionalSpecs.Count > 0)
        {
            var anyOptionalPassed = optionalSpecs.Any(s => EvaluateSpecification(s, releaseTitle, parsed));
            if (!anyOptionalPassed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateSpecification(CustomFormatSpecification spec, string releaseTitle, ParsedReleaseInfo parsed)
    {
        if (spec == null)
        {
            return false;
        }

        var (targetText, pattern) = ResolveTargetAndPattern(spec, releaseTitle, parsed);

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var isMatch = false;

        try
        {
            var textToTest = targetText ?? string.Empty;
            isMatch = Regex.IsMatch(textToTest, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!isMatch && !string.IsNullOrEmpty(textToTest))
            {
                var normalized = textToTest.Replace('.', ' ').Replace('_', ' ');
                isMatch = Regex.IsMatch(normalized, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            if (!isMatch && !string.IsNullOrEmpty(releaseTitle) && textToTest != releaseTitle)
            {
                isMatch = Regex.IsMatch(releaseTitle, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!isMatch)
                {
                    var normalizedTitle = releaseTitle.Replace('.', ' ').Replace('_', ' ');
                    isMatch = Regex.IsMatch(normalizedTitle, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
            }
        }
        catch (ArgumentException)
        {
            isMatch = false;
        }

        return spec.Negate ? !isMatch : isMatch;
    }

    private static (string TargetText, string Pattern) ResolveTargetAndPattern(
        CustomFormatSpecification spec,
        string releaseTitle,
        ParsedReleaseInfo parsed)
    {
        var impl = spec.Implementation ?? string.Empty;

        if (spec.Fields != null && spec.Fields.Count > 0)
        {
            if (spec.Fields.TryGetValue("releaseGroup", out var groupPattern) ||
                spec.Fields.TryGetValue("group", out groupPattern))
            {
                return (parsed?.ReleaseGroup ?? string.Empty, groupPattern);
            }

            if (spec.Fields.TryGetValue("codec", out var codecPattern))
            {
                return (parsed?.Codec ?? string.Empty, codecPattern);
            }

            if (spec.Fields.TryGetValue("source", out var sourcePattern))
            {
                return (parsed?.Source ?? string.Empty, sourcePattern);
            }

            if (spec.Fields.TryGetValue("resolution", out var resPattern))
            {
                return (parsed?.Resolution ?? string.Empty, resPattern);
            }

            if (spec.Fields.TryGetValue("audio", out var audioPattern))
            {
                return (parsed?.Audio ?? string.Empty, audioPattern);
            }

            if (spec.Fields.TryGetValue("edition", out var editionPattern))
            {
                return (parsed?.Edition ?? string.Empty, editionPattern);
            }

            if (spec.Fields.TryGetValue("releaseTitle", out var titlePattern) ||
                spec.Fields.TryGetValue("title", out titlePattern))
            {
                return (releaseTitle ?? string.Empty, titlePattern);
            }

            var generalPattern = GetGeneralPattern(spec.Fields);
            if (generalPattern != null)
            {
                if (impl.Contains("group", StringComparison.OrdinalIgnoreCase))
                {
                    return (parsed?.ReleaseGroup ?? string.Empty, generalPattern);
                }

                if (impl.Contains("codec", StringComparison.OrdinalIgnoreCase))
                {
                    return (parsed?.Codec ?? string.Empty, generalPattern);
                }

                if (impl.Contains("source", StringComparison.OrdinalIgnoreCase))
                {
                    return (parsed?.Source ?? string.Empty, generalPattern);
                }

                if (impl.Contains("resolution", StringComparison.OrdinalIgnoreCase) ||
                    impl.Contains("res", StringComparison.OrdinalIgnoreCase))
                {
                    return (parsed?.Resolution ?? string.Empty, generalPattern);
                }

                if (impl.Contains("audio", StringComparison.OrdinalIgnoreCase))
                {
                    return (parsed?.Audio ?? string.Empty, generalPattern);
                }

                if (impl.Contains("edition", StringComparison.OrdinalIgnoreCase))
                {
                    return (parsed?.Edition ?? string.Empty, generalPattern);
                }

                return (releaseTitle ?? string.Empty, generalPattern);
            }
        }

        if (impl.Contains("group", StringComparison.OrdinalIgnoreCase))
        {
            return (parsed?.ReleaseGroup ?? string.Empty, null);
        }

        return (releaseTitle ?? string.Empty, null);
    }

    private static string GetGeneralPattern(Dictionary<string, string> fields)
    {
        if (fields == null)
        {
            return null;
        }

        if (fields.TryGetValue("value", out var val))
        {
            return val;
        }

        if (fields.TryGetValue("pattern", out var pat))
        {
            return pat;
        }

        if (fields.TryGetValue("regularExpression", out var regexVal))
        {
            return regexVal;
        }

        if (fields.TryGetValue("regex", out var r))
        {
            return r;
        }

        return fields.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v));
    }
}
