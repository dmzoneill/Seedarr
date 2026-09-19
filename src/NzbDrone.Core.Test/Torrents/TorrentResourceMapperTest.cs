using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Peers;
using Seedarr.Api.V1.Torrents;

namespace Seedarr.Api.V1.Test.Torrents;

[TestFixture]
public class TorrentResourceMapperTest
{
    private PeerConnection CreateMockPeerConnection(string ip = "93.184.216.34", int port = 6881)
    {
        var ms = new MemoryStream();
        return new PeerConnection(ms, ip, port);
    }

    [Test]
    public void ToPeerResource_should_set_L_flag_for_RFC1918_addresses()
    {
        using var conn1 = CreateMockPeerConnection("192.168.1.50");
        using var conn2 = CreateMockPeerConnection("10.0.0.5");
        using var conn3 = CreateMockPeerConnection("172.16.5.10");

        var res1 = TorrentResourceMapper.ToPeerResource(conn1, 1);
        var res2 = TorrentResourceMapper.ToPeerResource(conn2, 2);
        var res3 = TorrentResourceMapper.ToPeerResource(conn3, 3);

        Assert.That(res1.Flags, Does.Contain("L"));
        Assert.That(res2.Flags, Does.Contain("L"));
        Assert.That(res3.Flags, Does.Contain("L"));
    }

    [Test]
    public void ToPeerResource_should_set_L_flag_when_discovered_via_lpd()
    {
        using var conn = CreateMockPeerConnection("93.184.216.34");
        conn.DiscoverySource = "lpd";

        var res = TorrentResourceMapper.ToPeerResource(conn, 1);

        Assert.That(res.Flags, Does.Contain("L"));
    }

    [Test]
    public void ToPeerResource_should_not_set_L_flag_for_public_ip_without_lpd()
    {
        using var conn = CreateMockPeerConnection("93.184.216.34");
        conn.DiscoverySource = "tracker";

        var res = TorrentResourceMapper.ToPeerResource(conn, 1);

        Assert.That(res.Flags, Does.Not.Contain("L"));
    }

    [Test]
    public void ToPeerResource_should_set_I_flag_only_when_inbound()
    {
        using var outboundConn = CreateMockPeerConnection();
        outboundConn.IsInbound = false;
        outboundConn.PeerInterested = true; // should NOT trigger 'I' flag

        using var inboundConn = CreateMockPeerConnection();
        inboundConn.IsInbound = true;
        inboundConn.PeerInterested = false; // still inbound

        var outboundRes = TorrentResourceMapper.ToPeerResource(outboundConn, 1);
        var inboundRes = TorrentResourceMapper.ToPeerResource(inboundConn, 2);

        Assert.That(outboundRes.Flags, Does.Not.Contain("I"));
        Assert.That(inboundRes.Flags, Does.Contain("I"));
    }

    [Test]
    public void ToPeerResource_should_set_d_and_D_flags_based_on_interest_and_peer_choke()
    {
        using var connD = CreateMockPeerConnection();
        connD.AmInterested = true;
        connD.PeerChoking = false;

        using var connd = CreateMockPeerConnection();
        connd.AmInterested = true;
        connd.PeerChoking = true;

        using var connNeither = CreateMockPeerConnection();
        connNeither.AmInterested = false;

        var resD = TorrentResourceMapper.ToPeerResource(connD, 1);
        var resd = TorrentResourceMapper.ToPeerResource(connd, 2);
        var resNeither = TorrentResourceMapper.ToPeerResource(connNeither, 3);

        Assert.That(resD.Flags, Does.Contain("D"));
        Assert.That(resD.Flags, Does.Not.Contain("d"));

        Assert.That(resd.Flags, Does.Contain("d"));
        Assert.That(resd.Flags, Does.Not.Contain("D"));

        Assert.That(resNeither.Flags, Does.Not.Contain("D"));
        Assert.That(resNeither.Flags, Does.Not.Contain("d"));
    }

    [Test]
    public void ToPeerResource_should_set_u_and_U_flags_based_on_peer_interest_and_am_choking()
    {
        using var connU = CreateMockPeerConnection();
        connU.PeerInterested = true;
        connU.AmChoking = false;

        using var connu = CreateMockPeerConnection();
        connu.PeerInterested = true;
        connu.AmChoking = true;

        var resU = TorrentResourceMapper.ToPeerResource(connU, 1);
        var resu = TorrentResourceMapper.ToPeerResource(connu, 2);

        Assert.That(resU.Flags, Does.Contain("U"));
        Assert.That(resU.Flags, Does.Not.Contain("u"));

        Assert.That(resu.Flags, Does.Contain("u"));
        Assert.That(resu.Flags, Does.Not.Contain("U"));
    }

    [Test]
    public void ToPeerResource_should_set_X_flag_for_pex_peers()
    {
        using var conn = CreateMockPeerConnection();
        conn.DiscoverySource = "pex";

        var res = TorrentResourceMapper.ToPeerResource(conn, 1);

        Assert.That(res.Flags, Does.Contain("X"));
    }

    [Test]
    public void ToPeerResource_should_set_E_H_O_S_flags()
    {
        using var conn = CreateMockPeerConnection();
        conn.SupportsFastExtension = true;
        conn.IsOptimisticUnchoked = true;
        conn.IsSnubbed = true;

        var res = TorrentResourceMapper.ToPeerResource(conn, 1);

        Assert.That(res.Flags, Does.Contain("H"));
        Assert.That(res.Flags, Does.Contain("O"));
        Assert.That(res.Flags, Does.Contain("S"));
    }
}
