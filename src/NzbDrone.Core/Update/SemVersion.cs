using System;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Update;

public readonly struct SemVersion : IComparable<SemVersion>, IComparable, IEquatable<SemVersion>
{
    public Version Core { get; }
    public string Prerelease { get; }
    public string BuildMetadata { get; }
    public string OriginalString { get; }

    public int Major => Core?.Major ?? 0;
    public int Minor => Core?.Minor ?? 0;
    public int Patch => Math.Max(0, Core?.Build ?? 0);
    public bool IsPrerelease => !string.IsNullOrEmpty(Prerelease);

    public SemVersion(Version core, string prerelease = "", string buildMetadata = "", string originalString = null)
    {
        Core = core ?? new Version(0, 0, 0);
        Prerelease = prerelease ?? string.Empty;
        BuildMetadata = buildMetadata ?? string.Empty;
        OriginalString = originalString;
    }

    public static bool TryParse(string input, out SemVersion result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var clean = input.Trim().TrimStart('v', 'V');
        if (string.IsNullOrWhiteSpace(clean))
        {
            return false;
        }

        var plusIndex = clean.IndexOf('+');
        var build = string.Empty;
        if (plusIndex >= 0)
        {
            build = clean[(plusIndex + 1)..];
            clean = clean[..plusIndex];
        }

        var dashIndex = clean.IndexOf('-');
        var prerelease = string.Empty;
        if (dashIndex >= 0)
        {
            prerelease = clean[(dashIndex + 1)..];
            clean = clean[..dashIndex];
        }
        else
        {
            var match = Regex.Match(clean, @"^([0-9]+(?:\.[0-9]+)*)[-_.]?([a-zA-Z]+[0-9A-Za-z.-]*)$");
            if (match.Success)
            {
                clean = match.Groups[1].Value;
                prerelease = match.Groups[2].Value;
            }
        }

        if (string.IsNullOrWhiteSpace(clean))
        {
            return false;
        }

        // Support single-part numeric versions like "1" by appending ".0"
        if (!clean.Contains('.'))
        {
            clean += ".0";
        }

        if (!Version.TryParse(clean, out var coreVersion))
        {
            return false;
        }

        result = new SemVersion(coreVersion, prerelease, build, input.Trim());
        return true;
    }

    public static SemVersion Parse(string input)
    {
        if (TryParse(input, out var result))
        {
            return result;
        }

        throw new FormatException($"Invalid SemVer string: '{input}'");
    }

    public int CompareTo(SemVersion other)
    {
        var coreCmp = CompareCore(Core, other.Core);
        if (coreCmp != 0)
        {
            return coreCmp;
        }

        var thisHasPre = !string.IsNullOrEmpty(Prerelease);
        var otherHasPre = !string.IsNullOrEmpty(other.Prerelease);

        if (!thisHasPre && otherHasPre)
        {
            return 1;
        }

        if (thisHasPre && !otherHasPre)
        {
            return -1;
        }

        if (!thisHasPre && !otherHasPre)
        {
            return 0;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public int CompareTo(object obj)
    {
        if (obj is SemVersion other)
        {
            return CompareTo(other);
        }

        return 1;
    }

    public bool Equals(SemVersion other)
    {
        return CompareTo(other) == 0;
    }

    public override bool Equals(object obj)
    {
        return obj is SemVersion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Core?.Major ?? 0, Core?.Minor ?? 0, Math.Max(0, Core?.Build ?? 0), Prerelease?.ToLowerInvariant());
    }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(OriginalString))
        {
            return OriginalString;
        }

        var coreStr = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(Prerelease))
        {
            coreStr += $"-{Prerelease}";
        }

        if (!string.IsNullOrEmpty(BuildMetadata))
        {
            coreStr += $"+{BuildMetadata}";
        }

        return coreStr;
    }

    public static bool operator >(SemVersion left, SemVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(SemVersion left, SemVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(SemVersion left, SemVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(SemVersion left, SemVersion right) => left.CompareTo(right) <= 0;
    public static bool operator ==(SemVersion left, SemVersion right) => left.Equals(right);
    public static bool operator !=(SemVersion left, SemVersion right) => !left.Equals(right);

    private static int CompareCore(Version a, Version b)
    {
        if (a == null && b == null)
        {
            return 0;
        }

        if (a == null)
        {
            return -1;
        }

        if (b == null)
        {
            return 1;
        }

        var aMajor = Math.Max(0, a.Major);
        var bMajor = Math.Max(0, b.Major);
        if (aMajor != bMajor)
        {
            return aMajor.CompareTo(bMajor);
        }

        var aMinor = Math.Max(0, a.Minor);
        var bMinor = Math.Max(0, b.Minor);
        if (aMinor != bMinor)
        {
            return aMinor.CompareTo(bMinor);
        }

        var aBuild = Math.Max(0, a.Build);
        var bBuild = Math.Max(0, b.Build);
        if (aBuild != bBuild)
        {
            return aBuild.CompareTo(bBuild);
        }

        var aRev = Math.Max(0, a.Revision);
        var bRev = Math.Max(0, b.Revision);
        return aRev.CompareTo(bRev);
    }

    private static int ComparePrerelease(string a, string b)
    {
        var aParts = a.Split('.');
        var bParts = b.Split('.');
        var minLen = Math.Min(aParts.Length, bParts.Length);

        for (var i = 0; i < minLen; i++)
        {
            var cmp = CompareIdentifiers(aParts[i], bParts[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return aParts.Length.CompareTo(bParts.Length);
    }

    private static int CompareIdentifiers(string a, string b)
    {
        var aIsNum = long.TryParse(a, out var aNum);
        var bIsNum = long.TryParse(b, out var bNum);

        if (aIsNum && bIsNum)
        {
            return aNum.CompareTo(bNum);
        }

        if (aIsNum)
        {
            return -1;
        }

        if (bIsNum)
        {
            return 1;
        }

        var matchA = Regex.Match(a, @"^([A-Za-z]+)(\d+)$");
        var matchB = Regex.Match(b, @"^([A-Za-z]+)(\d+)$");
        if (matchA.Success && matchB.Success)
        {
            var prefixCmp = string.Compare(matchA.Groups[1].Value, matchB.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
            if (prefixCmp != 0)
            {
                return prefixCmp;
            }

            if (long.TryParse(matchA.Groups[2].Value, out var numA) && long.TryParse(matchB.Groups[2].Value, out var numB))
            {
                return numA.CompareTo(numB);
            }
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
