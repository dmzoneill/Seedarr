using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Blocklist;

namespace NzbDrone.Core.Test.Blocklist;

[TestFixture]
public class PeerBlocklistFormatParsingTests
{
    [Test]
    public void TryParse_PeerGuardian_standard_label_should_parse()
    {
        var line = "Bad Range:1.2.3.4-1.2.3.10";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("1.2.3.4").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("1.2.3.10").ToUInt32()));
    }

    [Test]
    public void TryParse_PeerGuardian_label_with_colons_should_parse()
    {
        var line = "Some:Org:Name:With:Colons:1.2.3.4-1.2.3.10";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("1.2.3.4").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("1.2.3.10").ToUInt32()));
    }

    [Test]
    public void TryParse_PeerGuardian_label_with_spaces_should_parse()
    {
        var line = "Bad Organization Range Name With Spaces: 1.2.3.4 - 1.2.3.10";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("1.2.3.4").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("1.2.3.10").ToUInt32()));
    }

    [Test]
    public void TryParse_PeerGuardian_label_with_commas_and_dashes_should_parse()
    {
        var line = "Bad, Company, Inc. - Dept A:1.2.3.4-1.2.3.10";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("1.2.3.4").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("1.2.3.10").ToUInt32()));
    }

    [Test]
    public void TryParse_PeerGuardian_label_with_CIDR_should_parse()
    {
        var line = "Malicious Subnet:192.168.1.0/24";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("192.168.1.0").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("192.168.1.255").ToUInt32()));
    }

    [Test]
    public void TryParse_PeerGuardian_label_with_single_IP_should_parse()
    {
        var line = "Known Bad Host:192.168.1.50";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        var expectedVal = IPAddress.Parse("192.168.1.50").ToUInt32();
        Assert.That(range.Start, Is.EqualTo(expectedVal));
        Assert.That(range.End, Is.EqualTo(expectedVal));
    }

    [Test]
    public void TryParse_PeerGuardian_label_with_zero_padded_octets_should_normalize_and_parse()
    {
        var line = "P2P Padded:001.002.003.004-001.002.003.010";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("1.2.3.4").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("1.2.3.10").ToUInt32()));
    }

    [Test]
    public void TryParse_eMule_dat_standard_with_access_level_and_description_should_parse()
    {
        var line = "001.002.003.004 - 001.002.003.010 , 000 , Bad IP Range";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("1.2.3.4").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("1.2.3.10").ToUInt32()));
    }

    [Test]
    public void TryParse_eMule_dat_compact_without_spaces_should_parse()
    {
        var line = "001.002.003.004-001.002.003.010,109,Level 1 Organization";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("1.2.3.4").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("1.2.3.10").ToUInt32()));
    }

    [Test]
    public void TryParse_eMule_dat_with_trailing_commas_should_parse()
    {
        var line = "001.002.003.004 - 001.002.003.010 , 000 ,";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("1.2.3.4").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("1.2.3.10").ToUInt32()));
    }

    [Test]
    public void TryParse_eMule_dat_with_trailing_comma_after_range_should_parse()
    {
        var line = "001.002.003.004 - 001.002.003.010 ,";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("1.2.3.4").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("1.2.3.10").ToUInt32()));
    }

    [Test]
    public void TryParse_eMule_dat_with_colon_in_description_should_parse()
    {
        var line = "001.002.003.004 - 001.002.003.010 , 000 , Note: Suspicious bot network";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("1.2.3.4").ToUInt32()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("1.2.3.10").ToUInt32()));
    }

    [Test]
    public void TryParse_eMule_dat_single_IP_with_metadata_should_parse()
    {
        var line = "001.002.003.004 , 000 , Single Bot Host";
        var success = Ipv4IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        var expectedVal = IPAddress.Parse("1.2.3.4").ToUInt32();
        Assert.That(range.Start, Is.EqualTo(expectedVal));
        Assert.That(range.End, Is.EqualTo(expectedVal));
    }

    [Test]
    public void TryParse_comments_should_be_ignored()
    {
        Assert.That(Ipv4IntervalTree.TryParse("# Comment line", out _), Is.False);
        Assert.That(Ipv4IntervalTree.TryParse("// C++ style comment", out _), Is.False);
        Assert.That(Ipv4IntervalTree.TryParse("; eMule dat header comment", out _), Is.False);
        Assert.That(Ipv4IntervalTree.TryParse("   ", out _), Is.False);
        Assert.That(Ipv4IntervalTree.TryParse(null, out _), Is.False);
    }

    [Test]
    public void TryParse_CIDR_and_single_IP_standard_and_zero_padded()
    {
        // Standard CIDR
        Assert.That(Ipv4IntervalTree.TryParse("10.0.0.0/8", out var cidrRange), Is.True);
        Assert.That(cidrRange.Start, Is.EqualTo(IPAddress.Parse("10.0.0.0").ToUInt32()));
        Assert.That(cidrRange.End, Is.EqualTo(IPAddress.Parse("10.255.255.255").ToUInt32()));

        // Zero-padded CIDR
        Assert.That(Ipv4IntervalTree.TryParse("010.000.000.000/08", out var paddedCidr), Is.True);
        Assert.That(paddedCidr.Start, Is.EqualTo(IPAddress.Parse("10.0.0.0").ToUInt32()));
        Assert.That(paddedCidr.End, Is.EqualTo(IPAddress.Parse("10.255.255.255").ToUInt32()));

        // Standard single IP
        Assert.That(Ipv4IntervalTree.TryParse("172.16.0.1", out var singleIp), Is.True);
        var expectedSingle = IPAddress.Parse("172.16.0.1").ToUInt32();
        Assert.That(singleIp.Start, Is.EqualTo(expectedSingle));
        Assert.That(singleIp.End, Is.EqualTo(expectedSingle));

        // Zero-padded single IP
        Assert.That(Ipv4IntervalTree.TryParse("001.002.003.004", out var paddedSingle), Is.True);
        var expectedPadded = IPAddress.Parse("1.2.3.4").ToUInt32();
        Assert.That(paddedSingle.Start, Is.EqualTo(expectedPadded));
        Assert.That(paddedSingle.End, Is.EqualTo(expectedPadded));
    }

    [Test]
    public void TryParse_IPv6_PeerGuardian_with_colons_and_spaces_in_label()
    {
        var line = "Evil:Tracker:Organization: 2001:db8:1234::1 - 2001:db8:1234::ff";
        var success = Ipv6IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("2001:db8:1234::1").ToUInt128()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("2001:db8:1234::ff").ToUInt128()));
    }

    [Test]
    public void TryParse_IPv6_eMule_dat_format_with_metadata()
    {
        var line = "2001:db8:1234::1 - 2001:db8:1234::ff , 000 , Bad IPv6 Range";
        var success = Ipv6IntervalTree.TryParse(line, out var range);

        Assert.That(success, Is.True);
        Assert.That(range.Start, Is.EqualTo(IPAddress.Parse("2001:db8:1234::1").ToUInt128()));
        Assert.That(range.End, Is.EqualTo(IPAddress.Parse("2001:db8:1234::ff").ToUInt128()));
    }

    [Test]
    public void Ipv6IntervalTree_Parse_should_process_mixed_format_rules_and_query_correctly()
    {
        var rules = new[]
        {
            "; Header comment",
            "# Another comment",
            "// Third comment",
            "PeerGuardian P2P:Some:Label:1.2.3.4-1.2.3.10",
            "005.006.007.008 - 005.006.007.020 , 000 , eMule DAT Range",
            "10.0.0.0/24",
            "192.168.1.100",
            "Evil:Tracker:2001:db8:cafe::1-2001:db8:cafe::10"
        };

        var tree = Ipv6IntervalTree.Parse(rules);

        // From PeerGuardian rule
        Assert.That(tree.Contains(IPAddress.Parse("1.2.3.5")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("1.2.3.11")), Is.False);

        // From eMule DAT rule with zero-padded octets
        Assert.That(tree.Contains(IPAddress.Parse("5.6.7.8")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("5.6.7.15")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("5.6.7.20")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("5.6.7.21")), Is.False);

        // From CIDR
        Assert.That(tree.Contains(IPAddress.Parse("10.0.0.50")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("10.0.1.50")), Is.False);

        // From single IP
        Assert.That(tree.Contains(IPAddress.Parse("192.168.1.100")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("192.168.1.101")), Is.False);

        // From IPv6 PeerGuardian
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8:cafe::5")), Is.True);
        Assert.That(tree.Contains(IPAddress.Parse("2001:db8:cafe::20")), Is.False);
    }
}
