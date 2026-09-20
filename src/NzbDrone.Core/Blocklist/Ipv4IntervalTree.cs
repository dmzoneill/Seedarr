using System;
using System.Collections.Generic;
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
        if (trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        // Check if rule has name prefix "name:ip"
        var colonIdx = trimmed.IndexOf(':');
        if (colonIdx > 0 && !trimmed.Contains("::") && trimmed.IndexOf(':', colonIdx + 1) < 0)
        {
            trimmed = trimmed[(colonIdx + 1)..].Trim();
        }
        else
        {
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx > 0)
            {
                var candidate = trimmed[(spaceIdx + 1)..].Trim();
                if (candidate.Length > 0 && (char.IsAsciiDigit(candidate[0]) || candidate[0] == ':'))
                {
                    trimmed = candidate;
                }
            }
        }

        // CIDR notation: 1.2.3.0/24 or ::ffff:1.2.3.0/24
        var slashIdx = trimmed.IndexOf('/');
        if (slashIdx >= 0)
        {
            var ipStr = trimmed[..slashIdx].Trim();
            var prefixStr = trimmed[(slashIdx + 1)..].Trim();
            if (TryParseIPv4(ipStr, out var ip) &&
                int.TryParse(prefixStr, out var prefix) &&
                prefix >= 0 && prefix <= 32)
            {
                range = FromCidr(ip, prefix);
                return true;
            }

            return false;
        }

        // Range notation: 1.2.3.4-1.2.3.10
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

        // Single IP: 1.2.3.4
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
        if (IPAddress.TryParse(input, out ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            return ip.AddressFamily == AddressFamily.InterNetwork;
        }

        ip = null;
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
