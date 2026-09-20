using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace NzbDrone.Core.Blocklist;

public readonly record struct Ipv4Range(uint Start, uint End) : IComparable<Ipv4Range>
{
    public int CompareTo(Ipv4Range other)
    {
        var cmp = Start.CompareTo(other.Start);
        return cmp != 0 ? cmp : End.CompareTo(other.End);
    }
}

public class Ipv4IntervalTree
{
    private readonly Ipv4Range[] _intervals;

    public Ipv4IntervalTree(IEnumerable<Ipv4Range> ranges)
    {
        _intervals = MergeAndSort(ranges);
    }

    public int IntervalCount => _intervals.Length;

    public IReadOnlyList<Ipv4Range> Intervals => _intervals;

    public bool Contains(uint address)
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
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        return Contains(address.ToUInt32());
    }

    public static Ipv4Range FromCidr(IPAddress baseAddress, int prefixLength)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        if (baseAddress.IsIPv4MappedToIPv6)
        {
            baseAddress = baseAddress.MapToIPv4();
        }

        if (baseAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException($"Cannot convert non-IPv4 address to IPv4 CIDR: {baseAddress}", nameof(baseAddress));
        }

        if (prefixLength < 0 || prefixLength > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength), "Prefix length must be between 0 and 32.");
        }

        var ip = baseAddress.ToUInt32();
        var mask = prefixLength == 0 ? 0u : (uint.MaxValue << (32 - prefixLength));
        var start = ip & mask;
        var end = start | ~mask;

        return new Ipv4Range(start, end);
    }

    public static bool TryParse(string rule, out Ipv4Range range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        var trimmed = rule.Trim();
        if (trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith(';'))
        {
            return false;
        }

        // 1. Direct clean parse: 1.2.3.4-1.2.3.10, 001.002.003.004 - 001.002.003.010, 1.2.3.0/24, 1.2.3.4
        if (TryParseCleanIPv4Range(trimmed, out range))
        {
            return true;
        }

        // 2. eMule .dat format: Start_IP - End_IP , Access_Level , Description
        // Also handles single IP or CIDR followed by comma metadata.
        var commaIdx = trimmed.IndexOf(',');
        if (commaIdx > 0)
        {
            var beforeComma = trimmed[..commaIdx].Trim();
            if (TryParseCleanIPv4Range(beforeComma, out range))
            {
                return true;
            }
        }

        // 3. PeerGuardian .p2p format: Range_Name:Start_IP-End_IP
        // Range name may have colons or spaces: e.g. "Some:Org:Name:1.2.3.4-1.2.3.10"
        // Try candidate substrings after colons (from right to left)
        for (var colonIdx = trimmed.LastIndexOf(':'); colonIdx >= 0; colonIdx = trimmed.LastIndexOf(':', colonIdx - 1))
        {
            var candidate = trimmed[(colonIdx + 1)..].Trim();
            if (candidate.Length == 0)
            {
                continue;
            }

            // Strip any trailing comma metadata if present in the IP candidate
            var candidateComma = candidate.IndexOf(',');
            if (candidateComma > 0)
            {
                var candidateBeforeComma = candidate[..candidateComma].Trim();
                if (TryParseCleanIPv4Range(candidateBeforeComma, out range))
                {
                    return true;
                }
            }

            if (TryParseCleanIPv4Range(candidate, out range))
            {
                return true;
            }
        }

        // 4. Space-separated label prefix: e.g. "BadRange 1.2.3.4-1.2.3.10" or "BadRange 1.2.3.4"
        for (var spaceIdx = trimmed.LastIndexOf(' '); spaceIdx > 0; spaceIdx = trimmed.LastIndexOf(' ', spaceIdx - 1))
        {
            var candidate = trimmed[(spaceIdx + 1)..].Trim();
            if (candidate.Length == 0)
            {
                continue;
            }

            var candidateComma = candidate.IndexOf(',');
            if (candidateComma > 0)
            {
                var candidateBeforeComma = candidate[..candidateComma].Trim();
                if (TryParseCleanIPv4Range(candidateBeforeComma, out range))
                {
                    return true;
                }
            }

            if (TryParseCleanIPv4Range(candidate, out range))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseCleanIPv4Range(string input, out Ipv4Range range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();

        // CIDR notation: 1.2.3.0/24 or ::ffff:1.2.3.0/24
        var slashIdx = trimmed.IndexOf('/');
        if (slashIdx >= 0)
        {
            var ipStr = trimmed[..slashIdx].Trim();
            var prefixStr = trimmed[(slashIdx + 1)..].Trim();
            if (TryParseIPv4(ipStr, out var ip) &&
                int.TryParse(prefixStr, NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) &&
                prefix >= 0 && prefix <= 32)
            {
                range = FromCidr(ip, prefix);
                return true;
            }

            return false;
        }

        // Range notation: 1.2.3.4-1.2.3.10 or 001.002.003.004 - 001.002.003.010
        var dashIdx = trimmed.IndexOf('-');
        if (dashIdx >= 0)
        {
            var startStr = trimmed[..dashIdx].Trim();
            var endStr = trimmed[(dashIdx + 1)..].Trim();
            if (TryParseIPv4(startStr, out var startIp) &&
                TryParseIPv4(endStr, out var endIp))
            {
                var startVal = startIp.ToUInt32();
                var endVal = endIp.ToUInt32();
                if (startVal > endVal)
                {
                    (startVal, endVal) = (endVal, startVal);
                }

                range = new Ipv4Range(startVal, endVal);
                return true;
            }

            return false;
        }

        // Single IP: 1.2.3.4 or 001.002.003.004
        if (TryParseIPv4(trimmed, out var singleIp))
        {
            var val = singleIp.ToUInt32();
            range = new Ipv4Range(val, val);
            return true;
        }

        return false;
    }

    private static bool TryParseIPv4(string input, out IPAddress ip)
    {
        ip = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim();
        if (s.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
        {
            s = s[7..].Trim();
        }

        var parts = s.Split('.');
        if (parts.Length == 4)
        {
            var octets = new byte[4];
            for (var i = 0; i < 4; i++)
            {
                var part = parts[i].Trim();
                if (part.Length == 0 || part.Length > 3)
                {
                    return false;
                }

                for (var c = 0; c < part.Length; c++)
                {
                    if (!char.IsAsciiDigit(part[c]))
                    {
                        return false;
                    }
                }

                if (!byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out octets[i]))
                {
                    return false;
                }
            }

            ip = new IPAddress(octets);
            return true;
        }

        if (IPAddress.TryParse(input, out var parsed))
        {
            if (parsed.IsIPv4MappedToIPv6)
            {
                parsed = parsed.MapToIPv4();
            }

            if (parsed.AddressFamily == AddressFamily.InterNetwork)
            {
                ip = parsed;
                return true;
            }
        }

        return false;
    }

    public static Ipv4Range[] MergeAndSort(IEnumerable<Ipv4Range> ranges)
    {
        if (ranges == null)
        {
            return Array.Empty<Ipv4Range>();
        }

        var list = new List<Ipv4Range>();
        foreach (var r in ranges)
        {
            list.Add(r.Start <= r.End ? r : new Ipv4Range(r.End, r.Start));
        }

        if (list.Count == 0)
        {
            return Array.Empty<Ipv4Range>();
        }

        list.Sort();

        var merged = new List<Ipv4Range>(list.Count);
        var current = list[0];

        for (var i = 1; i < list.Count; i++)
        {
            var next = list[i];

            if (current.End == uint.MaxValue)
            {
                break;
            }

            if (current.End + 1 >= next.Start)
            {
                if (next.End > current.End)
                {
                    current = new Ipv4Range(current.Start, next.End);
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
}
