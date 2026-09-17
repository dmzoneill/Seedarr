using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public class RssRule : ModelBase
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public string Name { get; set; }

    public bool IsEnabled { get; set; } = true;

    public string MustContain { get; set; }

    public string MustNotContain { get; set; }

    public int MinSeeders { get; set; } = 1;

    public long MinSizeBytes { get; set; }

    public long MaxSizeBytes { get; set; }

    public int MaxAgeDays { get; set; }

    public bool FreeleechOnly { get; set; }

    public int CategoryId { get; set; }

    public List<int> IndexerIds { get; set; } = new();

    public List<int> Tags { get; set; } = new();

    public bool AllowUnknownSeeders { get; set; } = true;

    public int Priority { get; set; }

    public bool Matches(ReleaseInfo release, DateTime? now = null)
    {
        if (release == null || !IsEnabled)
        {
            return false;
        }

        if (IndexerIds != null && IndexerIds.Count > 0 && !IndexerIds.Contains(release.IndexerId))
        {
            return false;
        }

        if (CategoryId > 0 && release.Categories != null && release.Categories.Count > 0)
        {
            if (!release.Categories.Contains(CategoryId.ToString()))
            {
                return false;
            }
        }

        if (FreeleechOnly && !release.IsFreeleech)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(MustContain) && !IsTitleMatch(MustContain, release.Title))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(MustNotContain) && IsTitleMatch(MustNotContain, release.Title))
        {
            return false;
        }

        if (MinSizeBytes > 0 && release.Size < MinSizeBytes)
        {
            return false;
        }

        if (MaxSizeBytes > 0 && release.Size > MaxSizeBytes)
        {
            return false;
        }

        if (MaxAgeDays > 0 && release.PublishDate.HasValue)
        {
            var currentTime = now ?? DateTime.UtcNow;
            var age = (currentTime - release.PublishDate.Value).TotalDays;
            if (age > MaxAgeDays)
            {
                return false;
            }
        }

        if (MinSeeders > 0)
        {
            if (!release.Seeders.HasValue)
            {
                if (!AllowUnknownSeeders)
                {
                    return false;
                }
            }
            else if (release.Seeders.Value < MinSeeders)
            {
                return false;
            }
        }

        return true;
    }

    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Pattern supplied by user in rule definition")]
    private static bool IsTitleMatch(string pattern, string title)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        if (title == null)
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return title.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
