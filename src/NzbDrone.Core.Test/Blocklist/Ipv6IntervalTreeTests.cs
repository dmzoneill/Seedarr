using System;
using System.Diagnostics;
using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Blocklist;

namespace NzbDrone.Core.Test.Blocklist;

[TestFixture]
public class Ipv6IntervalTreeTests
{
    [Test]
    public void FromCidr_Prefix_0_should_cover_entire_address_space()
    {
        var range = Ipv6IntervalTree.FromCidr(IPAddress.IPv6Any, 0);

        Assert.That(range.Start, Is.EqualTo(UInt128.MinValue));
        Assert.That(range.End, Is.EqualTo(UInt128.MaxValue));

        var tree = new Ipv6IntervalTree(new[] { range });

        Assert.That(tree.Contains(IPAddress.IPv6Any), Is.True);
        Assert.That(tree.Contains(IPAddress.IPv6Loopback), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8::1")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")), Is.True);
    }

    [Test]
    public void FromCidr_Prefix_128_should_cover_exact_single_ip()
    {
        var ip = IPAddress.Parse("2001:db8::dead:beef");
        var range = Ipv6IntervalTree.FromCidr(ip, 128);

        Assert.That(range.Start, Is.EqualTo(ip.ToUInt128()));
        Assert.That(range.End, Is.EqualTo(ip.ToUInt128()));

        var tree = new Ipv6IntervalTree(new[] { range });

        Assert.That(tree.Contains(ip), Is.True);
        Assert.That(tree.Contains(range.Start - 1), Is.False);
        Assert.That(tree.Contains(range.End + 1), Is.False);
    }

    [Test]
    public void FromCidr_Prefix_64_should_cover_subnet_boundaries()
    {
        var baseIp = IPAddress.Parse("2001:db8:abcd:0012::");
        var range = Ipv6IntervalTree.FromCidr(baseIp, 64);
        var tree = new Ipv6IntervalTree(new[] { range });

        Assert.That(tree.Contains(IPAddress.Parse("2001:db8:abcd:12::")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8:abcd:12::1")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8:abcd:12:ffff:ffff:ffff:ffff")), Is.True);

        Assert.That(tree.Contains(IPAddress.Parse("2001:db8:abcd:11:ffff:ffff:ffff:ffff")), Is.False);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8:abcd:13::")), Is.False);
    }

    [Test]
    public void FromCidr_with_invalid_prefix_should_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ipv6IntervalTree.FromCidr(IPAddress.IPv6Loopback, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ipv6IntervalTree.FromCidr(IPAddress.IPv6Loopback, 129));
    }

    [Test]
    public void MergeAndSort_should_merge_overlapping_and_adjacent_intervals()
    {
        var ranges = new[]
        {
            new Ipv6Range((UInt128)10, (UInt128)20),
            new Ipv6Range((UInt128)15, (UInt128)25),
            new Ipv6Range((UInt128)26, (UInt128)30),
            new Ipv6Range((UInt128)40, (UInt128)50),
            new Ipv6Range((UInt128)45, (UInt128)48),
            new Ipv6Range((UInt128)100, (UInt128)200),
            new Ipv6Range((UInt128)201, (UInt128)300)
        };

        var merged = Ipv6IntervalTree.MergeAndSort(ranges);

        Assert.That(merged.Length, Is.EqualTo(3));
        Assert.That(merged[0], Is.EqualTo(new Ipv6Range((UInt128)10, (UInt128)30)));
        Assert.That(merged[1], Is.EqualTo(new Ipv6Range((UInt128)40, (UInt128)50)));
        Assert.That(merged[2], Is.EqualTo(new Ipv6Range((UInt128)100, (UInt128)300)));
    }

    [Test]
    public void MergeAndSort_near_UInt128_MaxValue_should_not_overflow()
    {
        var nearMax = UInt128.MaxValue - 10;
        var max = UInt128.MaxValue;

        var ranges = new[]
        {
            new Ipv6Range(nearMax, max - 1),
            new Ipv6Range(max, max)
        };

        var merged = Ipv6IntervalTree.MergeAndSort(ranges);

        Assert.That(merged.Length, Is.EqualTo(1));
        Assert.That(merged[0].Start, Is.EqualTo(nearMax));
        Assert.That(merged[0].End, Is.EqualTo(max));

        var tree = new Ipv6IntervalTree(merged);
        Assert.That(tree.Contains(max), Is.True);
        Assert.That(tree.Contains(nearMax), Is.True);
        Assert.That(tree.Contains(nearMax - 1), Is.False);
    }

    [Test]
    public void Boundary_edge_cases_all_zero_and_all_ones()
    {
        var treeZero = new Ipv6IntervalTree(new[] { new Ipv6Range(UInt128.MinValue, UInt128.MinValue) });
        Assert.That(treeZero.Contains(IPAddress.IPv6Any), Is.True);
        Assert.That(treeZero.Contains((UInt128)0), Is.True);
        Assert.That(treeZero.Contains((UInt128)1), Is.False);

        var treeMax = new Ipv6IntervalTree(new[] { new Ipv6Range(UInt128.MaxValue, UInt128.MaxValue) });
        Assert.That(treeMax.Contains(IPAddress.Parse("ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")), Is.True);
        Assert.That(treeMax.Contains(UInt128.MaxValue), Is.True);
        Assert.That(treeMax.Contains(UInt128.MaxValue - 1), Is.False);
    }

    [Test]
    public void Dual_stack_IPv4_mapped_IPv6_normalization()
    {
        var rules = new[]
        {
            "192.0.2.0/24",
            "2001:db8::/32"
        };

        var tree = Ipv6IntervalTree.Parse(rules);

        // IPv4 query
        Assert.That(tree.Contains(IPAddress.Parse("192.0.2.1")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("192.0.2.254")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("192.0.3.1")), Is.False);

        // IPv4-mapped IPv6 query
        Assert.That(tree.Contains(IPAddress.Parse("::ffff:192.0.2.1")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("::ffff:192.0.2.254")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("::ffff:192.0.3.1")), Is.False);

        // Native IPv6 query
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8::1")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db9::1")), Is.False);
    }

    [Test]
    public void Dual_stack_rule_specified_as_IPv4_mapped_IPv6_CIDR()
    {
        var rules = new[]
        {
            "::ffff:10.0.0.0/120"
        };

        var tree = Ipv6IntervalTree.Parse(rules);

        Assert.That(tree.Contains(IPAddress.Parse("10.0.0.1")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("10.0.0.254")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("::ffff:10.0.0.5")), Is.True);

        Assert.That(tree.Contains(IPAddress.Parse("10.0.1.1")), Is.False);
        Assert.That(tree.Contains(IPAddress.Parse("::ffff:10.0.1.1")), Is.False);
    }

    [Test]
    public void Parse_handles_diverse_rule_formats()
    {
        var rules = new[]
        {
            "# A comment to ignore",
            "// Another comment",
            "  ",
            "2001:db8::/32",
            "EvilTracker:2001:db8:1234::1-2001:db8:1234::ff",
            "2001:db8:abcd::1",
            "BadPeer:192.168.1.50-192.168.1.60",
            "10.20.30.40"
        };

        var tree = Ipv6IntervalTree.Parse(rules);

        Assert.That(tree.Contains(IPAddress.Parse("2001:db8::55")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8:1234::10")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8:abcd::1")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("192.168.1.55")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("::ffff:192.168.1.55")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("10.20.30.40")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("10.20.30.41")), Is.False);
    }

    [Test]
    public void Parse_inverted_range_should_normalize()
    {
        var rules = new[]
        {
            "2001:db8::ffff-2001:db8::1"
        };

        var tree = Ipv6IntervalTree.Parse(rules);

        Assert.That(tree.Contains(IPAddress.Parse("2001:db8::10")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8::1")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8::ffff")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8::1:0")), Is.False);
    }

    [Test]
    public void Lookup_performance_on_large_interval_set()
    {
        var ranges = new Ipv6Range[10_000];
        var baseAddress = (UInt128)1_000_000_000;

        for (var i = 0; i < ranges.Length; i++)
        {
            var start = baseAddress + ((UInt128)i * 100);
            var end = start + 50;
            ranges[i] = new Ipv6Range(start, end);
        }

        var tree = new Ipv6IntervalTree(ranges);
        Assert.That(tree.IntervalCount, Is.EqualTo(10_000));

        var sw = Stopwatch.StartNew();
        var hits = 0;

        for (var i = 0; i < 50_000; i++)
        {
            var testIp = baseAddress + ((UInt128)(i % 10_000) * 100) + 25;
            if (tree.Contains(testIp))
            {
                hits++;
            }

            var testMiss = baseAddress + ((UInt128)(i % 10_000) * 100) + 75;
            if (tree.Contains(testMiss))
            {
                hits++;
            }
        }

        sw.Stop();

        Assert.That(hits, Is.EqualTo(50_000));
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(200), $"50,000 lookups took {sw.ElapsedMilliseconds} ms, expected < 200 ms");
    }

    [Test]
    public void IpAddressExtensions_roundtrip_conversions()
    {
        var v6 = IPAddress.Parse("2001:db8:85a3::8a2e:370:7334");
        var u128 = v6.ToUInt128();
        var reconstructedV6 = u128.ToIPv6Address();

        Assert.That(reconstructedV6, Is.EqualTo(v6));

        var v4 = IPAddress.Parse("203.0.113.195");
        var u32 = v4.ToUInt32();
        var reconstructedV4 = u32.ToIPv4Address();

        Assert.That(reconstructedV4, Is.EqualTo(v4));

        var mapped = v4.MapToIPv6();
        Assert.That(mapped.ToUInt32(), Is.EqualTo(u32));
    }

    [Test]
    public void Ipv4IntervalTree_binary_search_and_merge_tests()
    {
        var ranges = new[]
        {
            new Ipv4Range(100, 200),
            new Ipv4Range(50, 80),
            new Ipv4Range(81, 99), // adjacent to [50..80] and [100..200] -> merges to [50..200]
            new Ipv4Range(300, 400),
            new Ipv4Range(350, 380), // fully contained in [300..400]
            new Ipv4Range(600, 500)  // inverted range -> should normalize to [500..600]
        };

        var tree = new Ipv4IntervalTree(ranges);

        Assert.That(tree.IntervalCount, Is.EqualTo(3));
        Assert.That(tree.Intervals[0], Is.EqualTo(new Ipv4Range(50, 200)));
        Assert.That(tree.Intervals[1], Is.EqualTo(new Ipv4Range(300, 400)));
        Assert.That(tree.Intervals[2], Is.EqualTo(new Ipv4Range(500, 600)));

        // Binary search correctness at and around boundary values
        Assert.That(tree.Contains(49u), Is.False);
        Assert.That(tree.Contains(50u), Is.True);
        Assert.That(tree.Contains(150u), Is.True);
        Assert.That(tree.Contains(200u), Is.True);
        Assert.That(tree.Contains(201u), Is.False);

        Assert.That(tree.Contains(299u), Is.False);
        Assert.That(tree.Contains(300u), Is.True);
        Assert.That(tree.Contains(400u), Is.True);
        Assert.That(tree.Contains(401u), Is.False);

        Assert.That(tree.Contains(500u), Is.True);
        Assert.That(tree.Contains(550u), Is.True);
        Assert.That(tree.Contains(600u), Is.True);
        Assert.That(tree.Contains(601u), Is.False);
    }

    [Test]
    public void Ipv4IntervalTree_near_uint_max_boundary_should_not_overflow()
    {
        var nearMax = uint.MaxValue - 10;
        var max = uint.MaxValue;

        var tree = new Ipv4IntervalTree(new[]
        {
            new Ipv4Range(nearMax, max - 1),
            new Ipv4Range(max, max)
        });

        Assert.That(tree.IntervalCount, Is.EqualTo(1));
        Assert.That(tree.Intervals[0].Start, Is.EqualTo(nearMax));
        Assert.That(tree.Intervals[0].End, Is.EqualTo(max));

        Assert.That(tree.Contains(max), Is.True);
        Assert.That(tree.Contains(nearMax), Is.True);
        Assert.That(tree.Contains(nearMax - 1), Is.False);
    }

    [Test]
    public void Ipv6IntervalTree_MergeAndSort_enclosed_and_inverted_intervals()
    {
        var ranges = new[]
        {
            new Ipv6Range((UInt128)10, (UInt128)50),
            new Ipv6Range((UInt128)20, (UInt128)30), // fully enclosed
            new Ipv6Range((UInt128)5, (UInt128)60),  // encloses [10..50]
            new Ipv6Range((UInt128)200, (UInt128)150) // inverted
        };

        var merged = Ipv6IntervalTree.MergeAndSort(ranges);

        Assert.That(merged.Length, Is.EqualTo(2));
        Assert.That(merged[0], Is.EqualTo(new Ipv6Range((UInt128)5, (UInt128)60)));
        Assert.That(merged[1], Is.EqualTo(new Ipv6Range((UInt128)150, (UInt128)200)));
    }

    [Test]
    public void Ipv6IntervalTree_broad_cidr_fallback_matches_ipv4_and_mapped()
    {
        // ::/0 blocks entire IPv6 space, which should also match mapped IPv4 addresses
        var rules = new[]
        {
            "::/0"
        };

        var tree = Ipv6IntervalTree.Parse(rules);

        Assert.That(tree.Contains(IPAddress.Parse("1.2.3.4")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("::ffff:1.2.3.4")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2600::1")), Is.True);
    }

    [Test]
    public void Ipv4IntervalTree_TryParse_handles_comments_labels_and_mapped_addresses()
    {
        Assert.That(Ipv4IntervalTree.TryParse("# comment", out _), Is.False);
        Assert.That(Ipv4IntervalTree.TryParse("// comment", out _), Is.False);
        Assert.That(Ipv4IntervalTree.TryParse("", out _), Is.False);

        Assert.That(Ipv4IntervalTree.TryParse("Spammer:192.168.1.1", out var r1), Is.True);
        Assert.That(r1.Start, Is.EqualTo(IPAddress.Parse("192.168.1.1").ToUInt32()));

        Assert.That(Ipv4IntervalTree.TryParse("Spammer 192.168.1.0/24", out var r2), Is.True);
        Assert.That(r2.Start, Is.EqualTo(IPAddress.Parse("192.168.1.0").ToUInt32()));
        Assert.That(r2.End, Is.EqualTo(IPAddress.Parse("192.168.1.255").ToUInt32()));

        Assert.That(Ipv4IntervalTree.TryParse("::ffff:10.0.0.1", out var r3), Is.True);
        Assert.That(r3.Start, Is.EqualTo(IPAddress.Parse("10.0.0.1").ToUInt32()));

        Assert.That(Ipv4IntervalTree.TryParse("::ffff:10.0.0.0/24", out var r4), Is.True);
        Assert.That(r4.Start, Is.EqualTo(IPAddress.Parse("10.0.0.0").ToUInt32()));
        Assert.That(r4.End, Is.EqualTo(IPAddress.Parse("10.0.0.255").ToUInt32()));
    }
}
