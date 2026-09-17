using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class ChokeManagerTest
{
    private IConnectionManager _connectionManager;
    private IConfigService _configService;
    private ChokeManager _subject;
    private List<PeerConnection> _connections;

    [SetUp]
    public void SetUp()
    {
        _connectionManager = Substitute.For<IConnectionManager>();
        _configService = Substitute.For<IConfigService>();
        _connections = new List<PeerConnection>();

        _connectionManager.GetAllConnections().Returns(_connections);
        _connectionManager.GetConnections(Arg.Any<string>()).Returns(x =>
            _connections.Where(c => string.Equals(c.InfoHash, x.Arg<string>(), StringComparison.OrdinalIgnoreCase)).ToList());
        _configService.MaxUploadSlots.Returns(4);
        _connectionManager.GetDynamicUploadSlotCount(Arg.Any<string>()).Returns(x => _configService.MaxUploadSlots);
        _connectionManager.GetDynamicUploadSlotCount().Returns(x => _configService.MaxUploadSlots);

        _subject = new ChokeManager(_connectionManager, _configService);
    }

    private PeerConnection CreatePeer(string infoHash, int port, bool interested = true, long rate = 1000)
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", port);
        typeof(PeerConnection).GetProperty("InfoHash")?.SetValue(conn, infoHash);
        conn.PeerInterested = interested;
        conn.AmChoking = true;
        conn.UploadRate = rate;
        _connections.Add(conn);
        return conn;
    }

    [Test]
    public void ProcessRegularUnchoke_should_allocate_slots_per_torrent_swarm()
    {
        // 2 torrents, each with 5 interested peers
        // MaxUploadSlots = 4 => regularSlotCount = 3
        for (var i = 1; i <= 5; i++)
        {
            CreatePeer("hashA", 1000 + i, rate: i * 100);
            CreatePeer("hashB", 2000 + i, rate: i * 100);
        }

        _subject.ProcessRegularUnchoke();

        var unchokedA = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        var unchokedB = _connections.Where(c => c.InfoHash == "hashB" && !c.AmChoking).ToList();

        // Total regular unchoked slots across swarms should equal regularSlotCount (3)
        Assert.That(unchokedA.Count + unchokedB.Count, Is.EqualTo(3));

        // Both torrents should receive at least 1 baseline unchoke slot
        Assert.That(unchokedA.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(unchokedB.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void ProcessRegularUnchoke_should_prevent_cross_swarm_starvation_when_one_torrent_has_faster_peers()
    {
        // MaxUploadSlots = 4 => regularSlotCount = 3
        // Torrent A has high speed peers, Torrent B has lower speed peers
        for (var i = 1; i <= 5; i++)
        {
            CreatePeer("hashA", 1000 + i, rate: 1000 * i);
            CreatePeer("hashB", 2000 + i, rate: 10 * i);
        }

        _subject.ProcessRegularUnchoke();

        var unchokedA = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        var unchokedB = _connections.Where(c => c.InfoHash == "hashB" && !c.AmChoking).ToList();

        // Total regular unchoked slots must not exceed regularSlotCount (3)
        Assert.That(unchokedA.Count + unchokedB.Count, Is.EqualTo(3));

        // Torrent B must receive unchoke slots and not be completely starved by Torrent A
        Assert.That(unchokedB.Count, Is.EqualTo(1));
        Assert.That(unchokedA.Count, Is.EqualTo(2));

        // Torrent B should unchoke its highest-rate peer (port 2005)
        Assert.That(unchokedB.Single().RemotePort, Is.EqualTo(2005));

        // Torrent A should unchoke its top peers (ports 1005, 1004)
        Assert.That(unchokedA.Select(c => c.RemotePort), Is.EquivalentTo(new[] { 1004, 1005 }));
    }

    [Test]
    public void ProcessRegularUnchoke_should_utilize_all_available_regular_slots_for_single_torrent()
    {
        // MaxUploadSlots = 4 => regularSlotCount = 3
        for (var i = 1; i <= 5; i++)
        {
            CreatePeer("hashA", 1000 + i, rate: i * 100);
        }

        _subject.ProcessRegularUnchoke();

        var unchokedA = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();

        // Single torrent should fully utilize all 3 available regular slots
        Assert.That(unchokedA.Count, Is.EqualTo(3));
        Assert.That(unchokedA.Select(c => c.RemotePort), Is.EquivalentTo(new[] { 1003, 1004, 1005 }));
    }

    [Test]
    public void ProcessOptimisticUnchoke_should_unchoke_one_choked_peer_per_torrent()
    {
        for (var i = 1; i <= 5; i++)
        {
            CreatePeer("hashA", 1000 + i, rate: i * 100);
            CreatePeer("hashB", 2000 + i, rate: i * 100);
        }

        _subject.ProcessRegularUnchoke();
        _subject.ProcessOptimisticUnchoke();

        var unchokedA = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        var unchokedB = _connections.Where(c => c.InfoHash == "hashB" && !c.AmChoking).ToList();

        // Exactly one optimistic unchoke per torrent
        Assert.That(unchokedA.Count(c => c.IsOptimisticUnchoked), Is.EqualTo(1));
        Assert.That(unchokedB.Count(c => c.IsOptimisticUnchoked), Is.EqualTo(1));

        // Both torrents have active unchoked peers (regular + optimistic)
        Assert.That(unchokedA.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(unchokedB.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void CanUnchoke_should_check_slots_for_specific_torrent_swarm()
    {
        _configService.MaxUploadSlots.Returns(2);

        var connA1 = CreatePeer("hashA", 1001);
        connA1.AmChoking = false;

        var connA2 = CreatePeer("hashA", 1002);
        connA2.AmChoking = false;

        var connA3 = CreatePeer("hashA", 1003);
        connA3.AmChoking = true;

        var connB1 = CreatePeer("hashB", 2001);
        connB1.AmChoking = true;

        // hashA already has 2 unchoked peers (limit 2)
        Assert.That(_subject.CanUnchoke(connA3), Is.False);

        // hashB has 0 unchoked peers, so it can unchoke
        Assert.That(_subject.CanUnchoke(connB1), Is.True);
    }

    [Test]
    public void CanUnchoke_should_return_false_when_infohash_is_empty()
    {
        var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 3000);
        Assert.That(_subject.CanUnchoke(conn), Is.False);
    }

    [Test]
    public void ProcessRegularUnchoke_should_unchoke_all_interested_when_slots_is_zero()
    {
        _configService.MaxUploadSlots.Returns(0);

        CreatePeer("hashA", 1001);
        CreatePeer("hashA", 1002);
        CreatePeer("hashB", 2001);

        _subject.ProcessRegularUnchoke();

        Assert.That(_connections.All(c => !c.AmChoking), Is.True);
    }

    [Test]
    public void ProcessRegularUnchoke_should_immediately_elect_replacement_optimistic_peer_when_optimistic_peer_is_promoted()
    {
        // MaxUploadSlots = 4 => 3 regular slots + 1 optimistic slot
        _configService.MaxUploadSlots.Returns(4);

        var peer1 = CreatePeer("hashA", 1001, rate: 100);
        var peer2 = CreatePeer("hashA", 1002, rate: 50);
        var peer3 = CreatePeer("hashA", 1003, rate: 300);
        var peer4 = CreatePeer("hashA", 1004, rate: 400);
        var peer5 = CreatePeer("hashA", 1005, rate: 500);

        // Pre-configure initial state: peer2 is optimistic unchoke, peer3/4/5 are regular unchokes
        peer2.IsOptimisticUnchoked = true;
        peer2.AmChoking = false;
        peer3.AmChoking = false;
        peer4.AmChoking = false;
        peer5.AmChoking = false;

        // Peer2 (optimistic) now outperforms peer3 and should be promoted into regular unchoke slots
        peer2.UploadRate = 600;

        _subject.ProcessRegularUnchoke();

        // Peer2 should have been promoted: now regular unchoked (IsOptimisticUnchoked cleared to false)
        Assert.That(peer2.AmChoking, Is.False);
        Assert.That(peer2.IsOptimisticUnchoked, Is.False);

        // Regular slots are peer2 (600), peer5 (500), peer4 (400)
        Assert.That(peer4.AmChoking, Is.False);
        Assert.That(peer5.AmChoking, Is.False);

        // Total unchoked slots must not collapse: exactly 4 unchoked peers (3 regular + 1 replacement optimistic)
        var unchoked = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        Assert.That(unchoked.Count, Is.EqualTo(4));

        // Exactly one peer in the swarm must hold the optimistic unchoked slot
        var optimisticPeers = _connections.Where(c => c.InfoHash == "hashA" && c.IsOptimisticUnchoked).ToList();
        Assert.That(optimisticPeers.Count, Is.EqualTo(1));
        var replacementOptimistic = optimisticPeers.Single();
        Assert.That(replacementOptimistic.AmChoking, Is.False);
        Assert.That(new[] { 1001, 1003 }, Does.Contain(replacementOptimistic.RemotePort));
    }

    [Test]
    public void PeerInterestedChanged_should_choke_peer_and_immediately_promote_next_eligible_peer_when_not_interested()
    {
        _configService.MaxUploadSlots.Returns(2);

        var peer1 = CreatePeer("hashA", 1001, rate: 100);
        var peer2 = CreatePeer("hashA", 1002, rate: 200);
        var peer3 = CreatePeer("hashA", 1003, rate: 300);
        var peer4 = CreatePeer("hashA", 1004, rate: 150);

        peer1.AmChoking = false;
        peer2.AmChoking = false;
        peer3.AmChoking = true;
        peer4.AmChoking = true;

        peer3.DownloadRate = 300;
        peer4.DownloadRate = 150;

        peer1.PeerInterested = false;
        _subject.PeerInterestedChanged(peer1);

        // peer1 should now be choked
        Assert.That(peer1.AmChoking, Is.True);

        // peer3 has the highest download rate among choked, interested peers and should be immediately promoted
        Assert.That(peer3.AmChoking, Is.False);

        // peer4 should remain choked
        Assert.That(peer4.AmChoking, Is.True);

        // Total unchoked count should remain at 2
        var unchoked = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        Assert.That(unchoked.Count, Is.EqualTo(2));
        Assert.That(unchoked, Does.Contain(peer2));
        Assert.That(unchoked, Does.Contain(peer3));
    }

    [Test]
    public void PeerInterestedChanged_should_choke_peer_without_errors_when_no_other_peers_are_interested()
    {
        _configService.MaxUploadSlots.Returns(2);

        var peer1 = CreatePeer("hashA", 1001);
        var peer2 = CreatePeer("hashA", 1002, interested: false);

        peer1.AmChoking = false;
        peer2.AmChoking = true;

        peer1.PeerInterested = false;
        Assert.DoesNotThrow(() => _subject.PeerInterestedChanged(peer1));

        Assert.That(peer1.AmChoking, Is.True);
        Assert.That(peer2.AmChoking, Is.True);

        var unchoked = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        Assert.That(unchoked, Is.Empty);
    }

    [Test]
    public void PeerInterestedChanged_should_not_promote_snubbed_peer_when_not_interested()
    {
        _configService.MaxUploadSlots.Returns(2);

        var peer1 = CreatePeer("hashA", 1001, rate: 100);
        var peer2 = CreatePeer("hashA", 1002, rate: 200);
        var peer3 = CreatePeer("hashA", 1003, rate: 300);
        var peer4 = CreatePeer("hashA", 1004, rate: 150);

        peer1.AmChoking = false;
        peer2.AmChoking = false;
        peer3.AmChoking = true;
        peer4.AmChoking = true;

        peer3.DownloadRate = 300;
        peer3.IsSnubbed = true;
        peer4.DownloadRate = 150;

        peer1.PeerInterested = false;
        _subject.PeerInterestedChanged(peer1);

        // peer1 choked, peer3 snubbed so skipped, peer4 promoted
        Assert.That(peer1.AmChoking, Is.True);
        Assert.That(peer3.AmChoking, Is.True);
        Assert.That(peer4.AmChoking, Is.False);
    }

    [Test]
    public void PeerInterestedChanged_should_not_promote_peer_when_already_choked_peer_sends_not_interested()
    {
        _configService.MaxUploadSlots.Returns(1);

        var peer1 = CreatePeer("hashA", 1001);
        var peer2 = CreatePeer("hashA", 1002);

        peer1.AmChoking = true;
        peer2.AmChoking = true;

        peer1.PeerInterested = false;
        _subject.PeerInterestedChanged(peer1);

        Assert.That(peer1.AmChoking, Is.True);
        Assert.That(peer2.AmChoking, Is.True);
    }

    [Test]
    public void CanUnchoke_should_return_false_when_connection_is_null()
    {
        Assert.That(_subject.CanUnchoke((PeerConnection)null), Is.False);
    }

    [Test]
    public void CanUnchoke_should_return_false_when_peer_is_seed()
    {
        var conn = CreatePeer("hashA", 1001);
        conn.IsSeed = true;
        Assert.That(_subject.CanUnchoke(conn), Is.False);
    }

    [Test]
    public void ProcessRegularUnchoke_should_exclude_and_choke_seeds_in_seeding_mode()
    {
        _configService.MaxUploadSlots.Returns(3);
        _subject.SetTorrentSeeding("hashA", true);

        var seed1 = CreatePeer("hashA", 1001, rate: 500);
        seed1.IsSeed = true;
        seed1.AmChoking = false; // was previously unchoked

        var leecher = CreatePeer("hashA", 1002, rate: 400);
        leecher.AmChoking = true;

        var seed2 = CreatePeer("hashA", 1003, rate: 300);
        seed2.Progress = 1.0; // IsSeed via progress

        _subject.ProcessRegularUnchoke();

        // Seed peers must be choked
        Assert.That(seed1.AmChoking, Is.True);
        Assert.That(seed2.AmChoking, Is.True);

        // Leecher must receive the unchoke slot
        Assert.That(leecher.AmChoking, Is.False);
    }

    [Test]
    public void PeerBecameSeed_should_immediately_choke_seed_and_promote_next_eligible_leecher()
    {
        _configService.MaxUploadSlots.Returns(2);
        _subject.SetTorrentSeeding("hashA", true);

        var peer1 = CreatePeer("hashA", 1001, rate: 100);
        var peer2 = CreatePeer("hashA", 1002, rate: 200);

        peer1.AmChoking = false;
        peer2.AmChoking = true;

        _subject.PeerBecameSeed(peer1);

        // peer1 is now seed and immediately choked
        Assert.That(peer1.IsSeed, Is.True);
        Assert.That(peer1.AmChoking, Is.True);

        // freed slot is immediately reallocated to peer2
        Assert.That(peer2.AmChoking, Is.False);
    }

    [Test]
    public void ProcessRegularUnchoke_should_rank_by_upload_rate_in_seeding_mode()
    {
        _configService.MaxUploadSlots.Returns(3); // 2 regular slots
        _subject.SetTorrentSeeding("hashA", true);

        var slowOldPeer = CreatePeer("hashA", 1001, rate: 100);
        slowOldPeer.BytesUploaded = 10000;

        var fastNewPeer = CreatePeer("hashA", 1002, rate: 500);
        fastNewPeer.BytesUploaded = 100;

        var mediumPeer = CreatePeer("hashA", 1003, rate: 300);
        mediumPeer.BytesUploaded = 500;

        _subject.ProcessRegularUnchoke();

        // Should unchoke the highest upload rate peers in seeding mode, ignoring BytesUploaded
        Assert.That(fastNewPeer.AmChoking, Is.False);
        Assert.That(mediumPeer.AmChoking, Is.False);
        Assert.That(slowOldPeer.AmChoking, Is.True);
    }

    [Test]
    public void ProcessRegularUnchoke_should_rotate_slots_round_robin_by_least_recently_unchoked_in_seeding_mode()
    {
        _configService.MaxUploadSlots.Returns(2); // 1 regular slot
        _subject.SetTorrentSeeding("hashA", true);

        var recentlyUnchoked = CreatePeer("hashA", 1001, rate: 0);
        recentlyUnchoked.LastUnchokedAt = DateTime.UtcNow.AddSeconds(-5);
        recentlyUnchoked.BytesUploaded = 5000;

        var neverUnchoked = CreatePeer("hashA", 1002, rate: 0);
        neverUnchoked.LastUnchokedAt = DateTime.MinValue;
        neverUnchoked.BytesUploaded = 0;

        _subject.ProcessRegularUnchoke();

        // Round-robin must pick the least recently unchoked peer rather than locking onto BytesUploaded
        Assert.That(neverUnchoked.AmChoking, Is.False);
        Assert.That(recentlyUnchoked.AmChoking, Is.True);
    }

    [Test]
    public void ProcessRegularUnchoke_should_unchoke_dynamic_number_of_peers_based_on_connection_manager()
    {
        for (var i = 1; i <= 10; i++)
        {
            CreatePeer("hashA", 1000 + i, rate: i * 100);
        }

        // Dynamically allocate 6 total slots (1 reserved for optimistic unchoke => 5 regular unchoke slots)
        _connectionManager.GetDynamicUploadSlotCount(Arg.Any<string>()).Returns(6);
        _connectionManager.GetDynamicUploadSlotCount().Returns(6);

        _subject.ProcessRegularUnchoke();

        var unchokedRegular = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        Assert.That(unchokedRegular.Count, Is.EqualTo(5));

        _subject.ProcessOptimisticUnchoke();

        var unchokedTotal = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        Assert.That(unchokedTotal.Count, Is.EqualTo(6));
        Assert.That(unchokedTotal.Count(c => c.IsOptimisticUnchoked), Is.EqualTo(1));
    }

    [Test]
    public void ProcessRegularUnchoke_should_not_displace_already_unchoked_peer_when_challenger_is_within_hysteresis_margin()
    {
        _configService.MaxUploadSlots.Returns(2); // 1 regular slot

        var peer1 = CreatePeer("hashA", 1001, rate: 500);
        peer1.AmChoking = false;
        peer1.LastUnchokedAt = DateTime.UtcNow.AddSeconds(-25); // unchoked 25s ago (> 20s min duration, < 60s max lease)

        var peer2 = CreatePeer("hashA", 1002, rate: 550); // 10% faster, within 15% hysteresis margin (500 * 1.15 = 575)
        peer2.AmChoking = true;

        _subject.ProcessRegularUnchoke();

        // peer1 should retain its unchoke slot due to hysteresis margin
        Assert.That(peer1.AmChoking, Is.False);
        Assert.That(peer2.AmChoking, Is.True);
    }

    [Test]
    public void ProcessRegularUnchoke_should_displace_slower_peer_when_challenger_exceeds_rate_by_more_than_hysteresis_margin()
    {
        _configService.MaxUploadSlots.Returns(2); // 1 regular slot

        var peer1 = CreatePeer("hashA", 1001, rate: 500);
        peer1.AmChoking = false;
        peer1.LastUnchokedAt = DateTime.UtcNow.AddSeconds(-25); // unchoked 25s ago (> 20s min duration, < 60s max lease)

        var peer2 = CreatePeer("hashA", 1002, rate: 600); // 20% faster, exceeds 15% hysteresis margin (500 * 1.15 = 575)
        peer2.AmChoking = true;

        _subject.ProcessRegularUnchoke();

        // peer2 should successfully displace peer1
        Assert.That(peer1.AmChoking, Is.True);
        Assert.That(peer2.AmChoking, Is.False);
    }

    [Test]
    public void ProcessRegularUnchoke_should_protect_recently_unchoked_peer_from_being_choked_within_minimum_unchoke_duration()
    {
        _configService.MaxUploadSlots.Returns(2); // 1 regular slot

        var peer1 = CreatePeer("hashA", 1001, rate: 100);
        peer1.AmChoking = false;
        peer1.LastUnchokedAt = DateTime.UtcNow.AddSeconds(-10); // unchoked 10s ago (< 20s min duration)

        var peer2 = CreatePeer("hashA", 1002, rate: 1000); // 10x faster challenger
        peer2.AmChoking = true;

        _subject.ProcessRegularUnchoke();

        // peer1 is within the minimum unchoke duration and must not be choked
        Assert.That(peer1.AmChoking, Is.False);
        Assert.That(peer2.AmChoking, Is.True);
    }

    [Test]
    public void ProcessRegularUnchoke_should_displace_peer_without_hysteresis_margin_after_max_lease_seconds()
    {
        _configService.MaxUploadSlots.Returns(2); // 1 regular slot

        var peer1 = CreatePeer("hashA", 1001, rate: 500);
        peer1.AmChoking = false;
        peer1.LastUnchokedAt = DateTime.UtcNow.AddSeconds(-65); // unchoked 65s ago (>= 60s max lease)

        var peer2 = CreatePeer("hashA", 1002, rate: 510); // only 2% faster (would fail hysteresis if lease hadn't expired)
        peer2.AmChoking = true;

        _subject.ProcessRegularUnchoke();

        // peer2 should displace peer1 because max lease expired
        Assert.That(peer1.AmChoking, Is.True);
        Assert.That(peer2.AmChoking, Is.False);
    }

    [Test]
    public void ProcessOptimisticUnchoke_should_preserve_optimistic_peer_across_multiple_rounds_and_rotate_after_three_rounds()
    {
        _configService.MaxUploadSlots.Returns(4);

        var peer1 = CreatePeer("hashA", 1001);
        var peer2 = CreatePeer("hashA", 1002);
        var peer3 = CreatePeer("hashA", 1003);

        // Round 1: first optimistic unchoke election
        _subject.ProcessOptimisticUnchoke();

        var round1Optimistic = _connections.Single(c => c.IsOptimisticUnchoked);
        Assert.That(round1Optimistic.AmChoking, Is.False);

        // Round 2: 3-round persistence preserves the optimistic peer
        _subject.ProcessOptimisticUnchoke();
        Assert.That(_connections.Single(c => c.IsOptimisticUnchoked), Is.SameAs(round1Optimistic));
        Assert.That(round1Optimistic.AmChoking, Is.False);

        // Round 3: 3-round persistence preserves the optimistic peer
        _subject.ProcessOptimisticUnchoke();
        Assert.That(_connections.Single(c => c.IsOptimisticUnchoked), Is.SameAs(round1Optimistic));
        Assert.That(round1Optimistic.AmChoking, Is.False);

        // Round 4: 3 rounds completed -> rotation occurs to a new peer
        _subject.ProcessOptimisticUnchoke();
        var round4Optimistic = _connections.Single(c => c.IsOptimisticUnchoked);
        Assert.That(round4Optimistic, Is.Not.SameAs(round1Optimistic));
        Assert.That(round4Optimistic.AmChoking, Is.False);
        Assert.That(round1Optimistic.IsOptimisticUnchoked, Is.False);
        Assert.That(round1Optimistic.AmChoking, Is.True);
    }

    [Test]
    public void ProcessRegularUnchoke_should_immediately_reallocate_optimistic_slot_when_optimistic_peer_is_promoted()
    {
        _configService.MaxUploadSlots.Returns(3); // 2 regular slots + 1 optimistic slot

        var peer1 = CreatePeer("hashA", 1001, rate: 50);  // initial optimistic
        var peer2 = CreatePeer("hashA", 1002, rate: 300); // regular
        var peer3 = CreatePeer("hashA", 1003, rate: 200); // regular
        var peer4 = CreatePeer("hashA", 1004, rate: 100); // choked candidate

        peer1.IsOptimisticUnchoked = true;
        peer1.AmChoking = false;
        peer2.AmChoking = false;
        peer3.AmChoking = false;
        peer4.AmChoking = true;

        // Peer 1 ramps up transfer rate and outperforms regular peers
        peer1.UploadRate = 500;

        _subject.ProcessRegularUnchoke();

        // Peer 1 should now be promoted to regular unchoke (IsOptimisticUnchoked cleared to false)
        Assert.That(peer1.AmChoking, Is.False);
        Assert.That(peer1.IsOptimisticUnchoked, Is.False);

        // An optimistic unchoke slot must have been immediately reallocated to one of the choked candidates
        var optimisticPeers = _connections.Where(c => c.InfoHash == "hashA" && c.IsOptimisticUnchoked).ToList();
        Assert.That(optimisticPeers.Count, Is.EqualTo(1));
        var newOptimistic = optimisticPeers.Single();
        Assert.That(newOptimistic.AmChoking, Is.False);
        Assert.That(new[] { peer3, peer4 }, Does.Contain(newOptimistic));

        // Total active unchoked slots must remain at maxUploadSlots (3)
        var totalUnchoked = _connections.Where(c => c.InfoHash == "hashA" && !c.AmChoking).ToList();
        Assert.That(totalUnchoked.Count, Is.EqualTo(3));
    }

    [Test]
    public void PeerDisconnected_should_immediately_reallocate_optimistic_slot_when_optimistic_peer_disconnects()
    {
        _configService.MaxUploadSlots.Returns(3);

        var peer1 = CreatePeer("hashA", 1001);
        var peer2 = CreatePeer("hashA", 1002);
        var peer3 = CreatePeer("hashA", 1003);

        // Unchoke an optimistic peer
        _subject.ProcessOptimisticUnchoke();
        var optimisticPeer = _connections.Single(c => c.IsOptimisticUnchoked);
        Assert.That(optimisticPeer.AmChoking, Is.False);

        // Optimistic peer disconnects
        _subject.PeerDisconnected(optimisticPeer);

        // Disconnected peer must no longer be optimistic
        Assert.That(optimisticPeer.IsOptimisticUnchoked, Is.False);

        // A new optimistic peer must have been immediately reallocated from the remaining peers
        var remainingOptimistic = _connections.Where(c => c.InfoHash == "hashA" && c.IsOptimisticUnchoked).ToList();
        Assert.That(remainingOptimistic.Count, Is.EqualTo(1));
        var replacement = remainingOptimistic.Single();
        Assert.That(replacement, Is.Not.SameAs(optimisticPeer));
        Assert.That(replacement.AmChoking, Is.False);
    }

    [Test]
    public void PeerInterestedChanged_should_immediately_reallocate_optimistic_slot_when_optimistic_peer_becomes_uninterested()
    {
        _configService.MaxUploadSlots.Returns(3);

        var peer1 = CreatePeer("hashA", 1001);
        var peer2 = CreatePeer("hashA", 1002);
        var peer3 = CreatePeer("hashA", 1003);

        // Unchoke an optimistic peer
        _subject.ProcessOptimisticUnchoke();
        var optimisticPeer = _connections.Single(c => c.IsOptimisticUnchoked);
        Assert.That(optimisticPeer.AmChoking, Is.False);

        // Optimistic peer sends NotInterested
        optimisticPeer.PeerInterested = false;
        _subject.PeerInterestedChanged(optimisticPeer);

        // The uninterested peer must be choked and lose optimistic flag
        Assert.That(optimisticPeer.AmChoking, Is.True);
        Assert.That(optimisticPeer.IsOptimisticUnchoked, Is.False);

        // A new optimistic peer must have been immediately reallocated from interested peers
        var remainingOptimistic = _connections.Where(c => c.InfoHash == "hashA" && c.IsOptimisticUnchoked).ToList();
        Assert.That(remainingOptimistic.Count, Is.EqualTo(1));
        var replacement = remainingOptimistic.Single();
        Assert.That(replacement, Is.Not.SameAs(optimisticPeer));
        Assert.That(replacement.AmChoking, Is.False);
        Assert.That(replacement.PeerInterested, Is.True);
    }

    [Test]
    public void ProcessOptimisticUnchoke_should_weight_new_peers_three_times_more_than_existing_peers()
    {
        var mockRandom = Substitute.For<IRandomNumberGenerator>();
        var manager = new ChokeManager(_connectionManager, _configService, random: mockRandom);

        _configService.MaxUploadSlots.Returns(2);

        // 1 existing peer (connected 60s ago, weight 1)
        var existingPeer = CreatePeer("hashA", 1001);
        existingPeer.ConnectedAt = DateTime.UtcNow.AddSeconds(-60);

        // 1 new peer (connected 10s ago, weight 3)
        var newPeer = CreatePeer("hashA", 1002);
        newPeer.ConnectedAt = DateTime.UtcNow.AddSeconds(-10);

        // Total weight = 1 + 3 = 4.
        // Sample 0: existingPeer (weight 1)
        // Samples 1, 2, 3: newPeer (weight 3) -> 3x selection probability
        mockRandom.Next(0, 4).Returns(1);
        manager.ProcessOptimisticUnchoke();
        Assert.That(newPeer.IsOptimisticUnchoked, Is.True);
        Assert.That(existingPeer.IsOptimisticUnchoked, Is.False);

        // Reset and test that sample 0 picks existingPeer
        newPeer.IsOptimisticUnchoked = false;
        newPeer.AmChoking = true;
        mockRandom.Next(0, 4).Returns(0);
        manager.ProcessOptimisticUnchoke();
        Assert.That(existingPeer.IsOptimisticUnchoked, Is.True);
        Assert.That(newPeer.IsOptimisticUnchoked, Is.False);
    }

    [Test]
    public void ProcessOptimisticUnchoke_should_track_optimistic_slots_independently_per_swarm()
    {
        _configService.MaxUploadSlots.Returns(4);

        for (var i = 1; i <= 3; i++)
        {
            CreatePeer("hashA", 1000 + i);
            CreatePeer("hashB", 2000 + i);
        }

        _subject.ProcessOptimisticUnchoke();

        var optimisticA = _connections.Single(c => c.InfoHash == "hashA" && c.IsOptimisticUnchoked);
        var optimisticB = _connections.Single(c => c.InfoHash == "hashB" && c.IsOptimisticUnchoked);

        Assert.That(optimisticA.AmChoking, Is.False);
        Assert.That(optimisticB.AmChoking, Is.False);
    }
}
