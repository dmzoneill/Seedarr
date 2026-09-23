using System;
using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Blocklist;

namespace NzbDrone.Core.Test.Blocklist;

[TestFixture]
public class Ipv4IntervalTreeTests
{
    [Test]
    public void Empty_tree_returns_false_for_all_queries()
    {
        var tree = new Ipv4IntervalTree(Array.Empty<Ipv4Range>());

        Assert.That(tree.IntervalCount, Is.EqualTo(0));
        Assert.That(tree.Intervals, Is.Empty);

        Assert.That(tree.Contains(0u), Is.False);
        Assert.That(tree.Contains(uint.MaxValue), Is.False);
        Assert.That(tree.Contains(IPAddress.Loopback), Is.False);
        Assert.That(tree.Contains(IPAddress.Parse("1.1.1.1")), Is.False);
    }

    [Test]
    public void Constructor_with_null_ranges_creates_empty_tree()
    {
        var tree = new Ipv4IntervalTree(null);

        Assert.That(tree.IntervalCount, Is.EqualTo(0));
        Assert.That(tree.Contains(12345u), Is.False);
    }

    [Test]
    public void FromCidr_prefix_0_covers_entire_ipv4_space()
    {
        var range = Ipv4IntervalTree.FromCidr(IPAddress.Any, 0);

        Assert.That(range.Start, Is.EqualTo(0u));
        Assert.That(range.End, Is.EqualTo(uint.MaxValue));

        var tree = new Ipv4IntervalTree(new[] { range });

        Assert.That(tree.Contains(IPAddress.Any), Is.True);
        Assert.That(tree.Contains(IPAddress.Loopback), Is.True);
        Assert.That(tree.Contains(IPAddress.Broadcast), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("192.168.1.1")), Is.True);
    }

    [Test]
    public void FromCidr_prefix_32_covers_single_exact_ip()
    {
        var ip = IPAddress.Parse("192.168.1.50");
        var range = Ipv4IntervalTree.FromCidr(ip, 32);

        Assert.That(range.Start, Is.EqualTo(range.End));
        Assert.That(range.Start, Is.EqualTo(ip.ToUInt32()));

        var tree = new Ipv4IntervalTree(new[] { range });

        Assert.That(tree.Contains(ip), Is.True);
        Assert.That(tree.Contains(range.Start - 1), Is.False);
        Assert.That(tree.Contains(range.End + 1), Is.False);
    }

    [Test]
    public void FromCidr_prefix_24_covers_subnet_boundaries()
    {
        var baseIp = IPAddress.Parse("192.168.5.0");
        var range = Ipv4IntervalTree.FromCidr(baseIp, 24);
        var tree = new Ipv4IntervalTree(new[] { range });

        Assert.That(tree.Contains(IPAddress.Parse("192.168.5.0")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("192.168.5.1")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("192.168.5.254")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("192.168.5.255")), Is.True);

        Assert.That(tree.Contains(IPAddress.Parse("192.168.4.255")), Is.False);
        Assert.That(tree.Contains(IPAddress.Parse("192.168.6.0")), Is.False);
    }

    [Test]
    public void FromCidr_handles_ipv4_mapped_ipv6_address()
    {
        var mappedIp = IPAddress.Parse("::ffff:10.0.0.0");
        var range = Ipv4IntervalTree.FromCidr(mappedIp, 8);

        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("10.0.0.0").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("10.255.255.255").ToUInt32()));
    }

    [Test]
    public void FromCidr_throws_for_invalid_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => Ipv4IntervalTree.FromCidr(null, 24));
        Assert.Throws<ArgumentException>(() => Ipv4IntervalTree.FromCidr(IPAddress.IPv6Loopback, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ipv4IntervalTree.FromCidr(IPAddress.Parse("1.2.3.4"), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ipv4IntervalTree.FromCidr(IPAddress.Parse("1.2.3.4"), 33));
    }

    [Test]
    public void MergeAndSort_merges_overlapping_and_adjacent_ranges()
    {
        var r1 = new Ipv4Range(100, 200);
        var r2 = new Ipv4Range(150, 250); // overlaps with r1 -> [100, 250]
        var r3 = new Ipv4Range(251, 300); // adjacent to [100, 250] -> [100, 300]
        var r4 = new Ipv4Range(400, 500); // separate -> [400, 500]

        var merged = Ipv4IntervalTree.MergeAndSort(new[] { r4, r3, r2, r1 });

        Assert.That(merged.Length, Is.EqualTo(2));
        Assert.That(merged[0], Is.EqualTo(new Ipv4Range(100, 300)));
        Assert.That(merged[1], Is.EqualTo(new Ipv4Range(400, 500)));
    }

    [Test]
    public void MergeAndSort_normalizes_inverted_ranges()
    {
        var inverted = new Ipv4Range(500, 100);
        var merged = Ipv4IntervalTree.MergeAndSort(new[] { inverted });

        Assert.That(merged.Length, Is.EqualTo(1));
        Assert.That(merged[0], Is.EqualTo(new Ipv4Range(100, 500)));
    }

    [Test]
    public void MergeAndSort_collapses_duplicates_and_subsets()
    {
        var r1 = new Ipv4Range(100, 500);
        var r2 = new Ipv4Range(200, 300); // subset
        var r3 = new Ipv4Range(100, 500); // duplicate

        var merged = Ipv4IntervalTree.MergeAndSort(new[] { r1, r2, r3 });

        Assert.That(merged.Length, Is.EqualTo(1));
        Assert.That(merged[0], Is.EqualTo(new Ipv4Range(100, 500)));
    }

    [Test]
    public void MergeAndSort_handles_range_ending_at_uint_max_without_overflow()
    {
        var r1 = new Ipv4Range(uint.MaxValue - 100, uint.MaxValue);
        var r2 = new Ipv4Range(10, 20);

        var merged = Ipv4IntervalTree.MergeAndSort(new[] { r1, r2 });

        Assert.That(merged.Length, Is.EqualTo(2));
        Assert.That(merged[0], Is.EqualTo(new Ipv4Range(10, 20)));
        Assert.That(merged[1], Is.EqualTo(new Ipv4Range(uint.MaxValue - 100, uint.MaxValue)));
    }

    [Test]
    public void Contains_checks_exact_interval_boundaries()
    {
        var start = IPAddress.Parse("10.0.0.10").ToUInt32();
        var end = IPAddress.Parse("10.0.0.20").ToUInt32();
        var tree = new Ipv4IntervalTree(new[] { new Ipv4Range(start, end) });

        Assert.That(tree.Contains(start), Is.True);
        Assert.That(tree.Contains(end), Is.True);
        Assert.That(tree.Contains(start - 1), Is.False);
        Assert.That(tree.Contains(end + 1), Is.False);
    }

    [Test]
    public void Contains_checks_extreme_ipv4_boundaries()
    {
        var zeroRange = new Ipv4Range(0u, 10u);
        var maxRange = new Ipv4Range(uint.MaxValue - 10u, uint.MaxValue);
        var tree = new Ipv4IntervalTree(new[] { zeroRange, maxRange });

        Assert.That(tree.Contains(0u), Is.True);
        Assert.That(tree.Contains(IPAddress.Any), Is.True);
        Assert.That(tree.Contains(10u), Is.True);
        Assert.That(tree.Contains(11u), Is.False);

        Assert.That(tree.Contains(uint.MaxValue), Is.True);
        Assert.That(tree.Contains(IPAddress.Broadcast), Is.True);
        Assert.That(tree.Contains(uint.MaxValue - 10u), Is.True);
        Assert.That(tree.Contains(uint.MaxValue - 11u), Is.False);
    }

    [Test]
    public void Contains_with_ipaddress_throws_for_null_and_handles_mapped_ipv6()
    {
        var ip = IPAddress.Parse("1.2.3.4");
        var range = new Ipv4Range(ip.ToUInt32(), ip.ToUInt32());
        var tree = new Ipv4IntervalTree(new[] { range });

        Assert.Throws<ArgumentNullException>(() => tree.Contains((IPAddress)null));

        var mapped = IPAddress.Parse("::ffff:1.2.3.4");
        Assert.That(tree.Contains(mapped), Is.True);

        var nativeIpv6 = IPAddress.Parse("2001:db8::1");
        Assert.That(tree.Contains(nativeIpv6), Is.False);
    }

    [TestCase("10.0.0.0/8")]
    [TestCase("::ffff:10.0.0.0/8")]
    [TestCase("192.168.1.0/24")]
    [TestCase("1.2.3.4")]
    [TestCase("1.2.3.4-1.2.3.10")]
    [TestCase("1.2.3.10-1.2.3.4")]
    [TestCase("001.002.003.004 - 001.002.003.010")]
    [TestCase("1.2.3.4 - 1.2.3.10 , 100 , Bad IP Range")]
    [TestCase("PeerGuardianList:1.2.3.4-1.2.3.10")]
    [TestCase("BadActors 1.2.3.4-1.2.3.10")]
    public void TryParse_successfully_parses_valid_formats(string rule)
    {
        var parsed = Ipv4IntervalTree.TryParse(rule, out var range);

        Assert.That(parsed, Is.True);
        Assert.That(range.Start, Is.LessThanOrEqualTo(range.End));
    }

    [TestCase("")]
    [TestCase("  ")]
    [TestCase(null)]
    [TestCase("# This is a comment")]
    [TestCase("// Another comment")]
    [TestCase("; Semicolon comment")]
    [TestCase("999.999.999.999")]
    [TestCase("1.2.3.4/33")]
    [TestCase("invalid.ip.string")]
    public void TryParse_returns_false_for_comments_and_invalid_strings(string rule)
    {
        var parsed = Ipv4IntervalTree.TryParse(rule, out var range);

        Assert.That(parsed, Is.False);
        Assert.That(range, Is.EqualTo(default(Ipv4Range)));
    }

    [Test]
    public void Ipv4Range_compares_by_start_then_end()
    {
        var r1 = new Ipv4Range(10, 50);
        var r2 = new Ipv4Range(10, 60);
        var r3 = new Ipv4Range(20, 30);

        Assert.That(r1.CompareTo(r2), Is.LessThan(0));
        Assert.That(r2.CompareTo(r1), Is.GreaterThan(0));
        Assert.That(r1.CompareTo(r3), Is.LessThan(0));
        Assert.That(r1.CompareTo(r1), Is.EqualTo(0));
    }
}
