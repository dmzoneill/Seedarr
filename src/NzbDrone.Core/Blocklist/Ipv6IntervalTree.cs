using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace NzbDrone.Core.Blocklist;

public readonly record struct Ipv6Range(UInt128 Start, UInt128 End) : IComparable<Ipv6Range>
{
    public int CompareTo(Ipv6Range other)
    {
        var cmp = Start.CompareTo(other.Start);
        return cmp != 0 ? cmp : End.CompareTo(other.End);
    }
}

public class Ipv6IntervalTree
{
    private readonly Ipv6Range[] _intervals;
    private readonly Ipv4IntervalTree _ipv4Tree;

    public Ipv6IntervalTree(IEnumerable<Ipv6Range> ranges, Ipv4IntervalTree ipv4Tree = null)
    {
        _intervals = MergeAndSort(ranges);
        _ipv4Tree = ipv4Tree;
    }

    public int IntervalCount => _intervals.Length;

    public IReadOnlyList<Ipv6Range> Intervals => _intervals;

    public Ipv4IntervalTree Ipv4Tree => _ipv4Tree;

    public bool Contains(UInt128 address)
    {
        var intervals = _intervals;
        var low = 0;
        var high = intervals.Length - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            ref readonly var range = ref intervals[mid];

            if (address < range.Start)
            {
                high = mid - 1;
            }
            else if (address > range.End)
            {
                low = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    public bool Contains(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            var ipv4 = address.MapToIPv4();
            if (_ipv4Tree != null && _ipv4Tree.Contains(ipv4))
            {
                return true;
            }

            return Contains(address.ToUInt128());
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            if (_ipv4Tree != null && _ipv4Tree.Contains(address))
            {
                return true;
            }

            return Contains(address.MapToIPv6().ToUInt128());
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return Contains(address.ToUInt128());
        }

        return false;
    }

    public static Ipv6Range FromCidr(IPAddress baseAddress, int prefixLength)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        if (baseAddress.IsIPv4MappedToIPv6)
        {
            if (prefixLength >= 0 && prefixLength <= 32)
            {
                return FromCidr(baseAddress.ToUInt128(), 96 + prefixLength);
            }
        }

        if (baseAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            if (prefixLength < 0 || prefixLength > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(prefixLength), "IPv4 prefix length must be between 0 and 32.");
            }

            return FromCidr(baseAddress.MapToIPv6().ToUInt128(), 96 + prefixLength);
        }

        if (baseAddress.AddressFamily != AddressFamily.InterNetworkV6)
        {
            throw new ArgumentException($"Unsupported address family: {baseAddress.AddressFamily}", nameof(baseAddress));
        }

        if (prefixLength < 0 || prefixLength > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength), "IPv6 prefix length must be between 0 and 128.");
        }

        return FromCidr(baseAddress.ToUInt128(), prefixLength);
    }

    public static Ipv6Range FromCidr(UInt128 baseAddress, int prefixLength)
    {
        if (prefixLength < 0 || prefixLength > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength), "Prefix length must be between 0 and 128.");
        }

        var mask = prefixLength == 0 ? UInt128.MinValue : (UInt128.MaxValue << (128 - prefixLength));
        var start = baseAddress & mask;
        var end = start | ~mask;

        return new Ipv6Range(start, end);
    }

    public static bool TryParse(string rule, out Ipv6Range range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        var trimmed = rule.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
            trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith(";", StringComparison.Ordinal))
        {
            return false;
        }

        if (TryParseCleanIPv6(trimmed, out range))
        {
            return true;
        }

        // Handle eMule trailing comma metadata (e.g. "2001:db8::1 - 2001:db8::ff , 000 , Bad IPv6")
        var commaIdx = trimmed.IndexOf(',');
        if (commaIdx > 0)
        {
            var beforeComma = trimmed[..commaIdx].Trim();
            if (TryParseCleanIPv6(beforeComma, out range))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseCleanIPv6(string trimmed, out Ipv6Range range)
    {
        range = default;

        // CIDR notation: 2001:db8::/32 or ::ffff:192.0.2.0/120
        var slashIdx = trimmed.LastIndexOf('/');
        if (slashIdx >= 0)
        {
            var ipPart = trimmed[..slashIdx].Trim();
            var prefixPart = trimmed[(slashIdx + 1)..].Trim();

            if (!int.TryParse(prefixPart, NumberStyles.None, CultureInfo.InvariantCulture, out var prefix))
            {
                return false;
            }

            if (TryExtractIp(ipPart, out var ip))
            {
                if (ip.IsIPv4MappedToIPv6)
                {
                    if (prefix >= 0 && prefix <= 32)
                    {
                        range = FromCidr(ip.ToUInt128(), 96 + prefix);
                        return true;
                    }

                    if (prefix >= 96 && prefix <= 128)
                    {
                        range = FromCidr(ip.ToUInt128(), prefix);
                        return true;
                    }

                    return false;
                }

                if (ip.AddressFamily == AddressFamily.InterNetworkV6 && prefix >= 0 && prefix <= 128)
                {
                    range = FromCidr(ip, prefix);
                    return true;
                }

                if (ip.AddressFamily == AddressFamily.InterNetwork && prefix >= 0 && prefix <= 32)
                {
                    range = FromCidr(ip, prefix);
                    return true;
                }
            }

            return false;
        }

        // Range notation: start-end
        var dashIdx = trimmed.IndexOf('-');
        if (dashIdx >= 0)
        {
            var startPart = trimmed[..dashIdx].Trim();
            var endPart = trimmed[(dashIdx + 1)..].Trim();

            if (TryExtractIp(startPart, out var startIp) && TryExtractIp(endPart, out var endIp))
            {
                var startVal = startIp.ToUInt128();
                var endVal = endIp.ToUInt128();

                if (startVal > endVal)
                {
                    (startVal, endVal) = (endVal, startVal);
                }

                range = new Ipv6Range(startVal, endVal);
                return true;
            }

            return false;
        }

        // Single IP notation
        if (TryExtractIp(trimmed, out var singleIp))
        {
            var val = singleIp.ToUInt128();
            range = new Ipv6Range(val, val);
            return true;
        }

        return false;
    }

    public static Ipv6IntervalTree Parse(IEnumerable<string> rules)
    {
        var v6Ranges = new List<Ipv6Range>();
        var v4Ranges = new List<Ipv4Range>();

        if (rules == null)
        {
            return new Ipv6IntervalTree(v6Ranges);
        }

        var v4MappedBase = IPAddress.Parse("::ffff:0.0.0.0").ToUInt128();
        var v4MappedMax = IPAddress.Parse("::ffff:255.255.255.255").ToUInt128();

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                continue;
            }

            var trimmed = rule.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            // Check if it parses as IPv4 rule
            if (Ipv4IntervalTree.TryParse(trimmed, out var v4Range))
            {
                v4Ranges.Add(v4Range);

                // Also map IPv4 range to IPv6 mapped range
                var mappedStart = v4Range.Start.ToIPv4Address().MapToIPv6().ToUInt128();
                var mappedEnd = v4Range.End.ToIPv4Address().MapToIPv6().ToUInt128();
                v6Ranges.Add(new Ipv6Range(mappedStart, mappedEnd));
            }
            else if (TryParse(trimmed, out var v6Range))
            {
                v6Ranges.Add(v6Range);

                // If IPv6 rule overlaps IPv4-mapped range ::ffff:0:0/96, also extract IPv4 range
                if (v6Range.Start <= v4MappedMax && v6Range.End >= v4MappedBase)
                {
                    var intersectStart = v6Range.Start > v4MappedBase ? v6Range.Start : v4MappedBase;
                    var intersectEnd = v6Range.End < v4MappedMax ? v6Range.End : v4MappedMax;

                    if (intersectStart <= intersectEnd)
                    {
                        var v4Start = (uint)(intersectStart - v4MappedBase);
                        var v4End = (uint)(intersectEnd - v4MappedBase);
                        v4Ranges.Add(new Ipv4Range(v4Start, v4End));
                    }
                }
            }
        }

        var ipv4Tree = v4Ranges.Count > 0 ? new Ipv4IntervalTree(v4Ranges) : null;
        return new Ipv6IntervalTree(v6Ranges, ipv4Tree);
    }

    public static Ipv6Range[] MergeAndSort(IEnumerable<Ipv6Range> ranges)
    {
        if (ranges == null)
        {
            return Array.Empty<Ipv6Range>();
        }

        var list = new List<Ipv6Range>();
        foreach (var r in ranges)
        {
            list.Add(r.Start <= r.End ? r : new Ipv6Range(r.End, r.Start));
        }

        if (list.Count == 0)
        {
            return Array.Empty<Ipv6Range>();
        }

        list.Sort();

        var merged = new List<Ipv6Range>(list.Count);
        var current = list[0];

        for (var i = 1; i < list.Count; i++)
        {
            var next = list[i];

            if (current.End == UInt128.MaxValue)
            {
                break;
            }

            if (current.End + 1 >= next.Start)
            {
                if (next.End > current.End)
                {
                    current = new Ipv6Range(current.Start, next.End);
                }
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged.ToArray();
    }

    private static bool TryExtractIp(string input, out IPAddress ip)
    {
        ip = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim();
        if (IPAddress.TryParse(s, out ip))
        {
            return true;
        }

        // Try stripping label before colons (e.g. "Bad:Peer:2001:db8::")
        for (var colonIdx = s.IndexOf(':'); colonIdx > 0 && colonIdx < s.Length - 1; colonIdx = s.IndexOf(':', colonIdx + 1))
        {
            var candidate = s[(colonIdx + 1)..].Trim();
            if (IPAddress.TryParse(candidate, out ip))
            {
                return true;
            }
        }

        // Try stripping label before whitespace (e.g. "Bad Peer 2001:db8::")
        for (var spaceIdx = s.IndexOf(' '); spaceIdx > 0 && spaceIdx < s.Length - 1; spaceIdx = s.IndexOf(' ', spaceIdx + 1))
        {
            var candidate = s[(spaceIdx + 1)..].Trim();
            if (IPAddress.TryParse(candidate, out ip))
            {
                return true;
            }
        }

        return false;
    }
}
