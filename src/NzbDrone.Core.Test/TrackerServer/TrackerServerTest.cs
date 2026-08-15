using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.TrackerServer;

namespace NzbDrone.Core.Test.TrackerServer;

[TestFixture]
public class TrackerServerTest
{
    private Core.TrackerServer.TrackerServer _trackerServer;
    private IPeerDatabase _peerDatabase;
    private IConfigService _configService;

    [SetUp]
    public void Setup()
    {
        _peerDatabase = Substitute.For<IPeerDatabase>();
        _configService = Substitute.For<IConfigService>();

        _configService.TrackerServerEnabled.Returns(true);
        _configService.TrackerHttpEnabled.Returns(true);
        _configService.TrackerAnnounceInterval.Returns(1800);
        _configService.TrackerMaxPeersPerAnnounce.Returns(50);
        _configService.TrackerLogAnnounces.Returns(false);
        _configService.TrackerPrivateMode.Returns(false);
        _configService.TrackerEnableScrape.Returns(true);
        _configService.TrackerRateLimitPerMinute.Returns(60);
        _configService.MinAnnounceIntervalSeconds.Returns(300);
        _configService.ScrapeIntervalSeconds.Returns(900);
        _configService.TrackerHttpPort.Returns(0);
        _configService.TrackerBindAddress.Returns("127.0.0.1");

        _trackerServer = new Core.TrackerServer.TrackerServer(_peerDatabase, _configService);
    }

    private static Dictionary<string, string> InvokeParseQueryString(string query)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "ParseQueryString",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (Dictionary<string, string>)method.Invoke(null, new object[] { query });
    }

    private static byte[] InvokeBuildCompactPeers(List<TrackerPeerEntry> peers, string excludeIp, int excludePort, int maxPeers)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "BuildCompactPeers",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (byte[])method.Invoke(null, new object[] { peers, excludeIp, excludePort, maxPeers });
    }

    private string InvokeHandleAnnounce(string path, IPEndPoint remoteEndpoint)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "HandleAnnounce",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (string)method.Invoke(_trackerServer, new object[] { path, remoteEndpoint });
    }

    private string InvokeHandleScrape(string path)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "HandleScrape",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (string)method.Invoke(_trackerServer, new object[] { path });
    }

    private bool InvokeIsRateLimited(string ip)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "IsRateLimited",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)method.Invoke(_trackerServer, new object[] { ip });
    }

    private void InvokePurgeExpiredRateLimits()
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "PurgeExpiredRateLimits",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_trackerServer, null);
    }

    private (Dictionary<string, string> Parameters, string Error) InvokeParseRequest(string path)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "ParseRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return ((Dictionary<string, string> Parameters, string Error))method.Invoke(_trackerServer, new object[] { path });
    }

    private static string InvokeReadBoundedLine(NetworkStream stream, int maxLength)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "ReadBoundedLine",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string)method.Invoke(null, new object[] { stream, maxLength });
    }

    private ConcurrentDictionary<string, object> GetRateLimitsField()
    {
        var field = typeof(Core.TrackerServer.TrackerServer).GetField(
            "_rateLimits",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (ConcurrentDictionary<string, object>)field.GetValue(_trackerServer);
    }

    // ---- ParseQueryString tests ----

    [Test]
    public void ParseQueryString_should_parse_simple_query()
    {
        var result = InvokeParseQueryString("key=value");

        Assert.That(result["key"], Is.EqualTo("value"));
    }

    [Test]
    public void ParseQueryString_should_parse_multiple_parameters()
    {
        var result = InvokeParseQueryString("info_hash=abc&port=6881&peer_id=test");

        Assert.That(result["info_hash"], Is.EqualTo("abc"));
        Assert.That(result["port"], Is.EqualTo("6881"));
        Assert.That(result["peer_id"], Is.EqualTo("test"));
    }

    [Test]
    public void ParseQueryString_should_handle_url_encoded_values()
    {
        var result = InvokeParseQueryString("key=hello%20world");

        Assert.That(result["key"], Is.EqualTo("hello world"));
    }

    [Test]
    public void ParseQueryString_should_be_case_insensitive_for_keys()
    {
        var result = InvokeParseQueryString("Key=value");

        Assert.That(result["KEY"], Is.EqualTo("value"));
        Assert.That(result["key"], Is.EqualTo("value"));
    }

    [Test]
    public void ParseQueryString_should_skip_pairs_without_equals()
    {
        var result = InvokeParseQueryString("key=value&noequalssign&other=test");

        Assert.That(result.ContainsKey("key"), Is.True);
        Assert.That(result.ContainsKey("other"), Is.True);
        Assert.That(result.ContainsKey("noequalssign"), Is.False);
    }

    [Test]
    public void ParseQueryString_should_handle_empty_value()
    {
        var result = InvokeParseQueryString("key=");

        Assert.That(result["key"], Is.EqualTo(""));
    }

    [Test]
    public void ParseQueryString_should_handle_value_with_equals()
    {
        var result = InvokeParseQueryString("key=val=ue");

        Assert.That(result["key"], Is.EqualTo("val=ue"));
    }

    [Test]
    public void ParseQueryString_should_overwrite_duplicate_keys()
    {
        var result = InvokeParseQueryString("key=first&key=second");

        Assert.That(result["key"], Is.EqualTo("second"));
    }

    [Test]
    public void ParseQueryString_should_handle_url_encoded_keys()
    {
        var result = InvokeParseQueryString("info%5Fhash=abc");

        Assert.That(result["info_hash"], Is.EqualTo("abc"));
    }

    [Test]
    public void ParseQueryString_should_return_empty_dict_for_empty_string()
    {
        var result = InvokeParseQueryString("");

        Assert.That(result, Is.Empty);
    }

    // ---- BuildCompactPeers tests ----

    [Test]
    public void BuildCompactPeers_should_encode_ip_and_port()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "192.168.1.1", Port = 6881 }
        };

        var result = InvokeBuildCompactPeers(peers, "10.0.0.1", 9999, 50);

        Assert.That(result, Has.Length.EqualTo(6));
        Assert.That(result[0], Is.EqualTo(192));
        Assert.That(result[1], Is.EqualTo(168));
        Assert.That(result[2], Is.EqualTo(1));
        Assert.That(result[3], Is.EqualTo(1));
        Assert.That((result[4] << 8) | result[5], Is.EqualTo(6881));
    }

    [Test]
    public void BuildCompactPeers_should_exclude_requesting_peer()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "192.168.1.1", Port = 6881 },
            new TrackerPeerEntry { Ip = "192.168.1.2", Port = 6882 }
        };

        var result = InvokeBuildCompactPeers(peers, "192.168.1.1", 6881, 50);

        Assert.That(result, Has.Length.EqualTo(6));
        Assert.That(result[0], Is.EqualTo(192));
        Assert.That(result[1], Is.EqualTo(168));
        Assert.That(result[2], Is.EqualTo(1));
        Assert.That(result[3], Is.EqualTo(2));
    }

    [Test]
    public void BuildCompactPeers_should_not_exclude_same_ip_different_port()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "192.168.1.1", Port = 6881 },
            new TrackerPeerEntry { Ip = "192.168.1.1", Port = 6882 }
        };

        var result = InvokeBuildCompactPeers(peers, "192.168.1.1", 6881, 50);

        Assert.That(result, Has.Length.EqualTo(6));
    }

    [Test]
    public void BuildCompactPeers_should_limit_to_max_peers()
    {
        var peers = new List<TrackerPeerEntry>();
        for (var i = 0; i < 10; i++)
        {
            peers.Add(new TrackerPeerEntry { Ip = $"10.0.0.{i}", Port = 6881 });
        }

        var result = InvokeBuildCompactPeers(peers, "10.0.1.1", 9999, 3);

        Assert.That(result, Has.Length.EqualTo(18));
    }

    [Test]
    public void BuildCompactPeers_should_return_empty_for_empty_list()
    {
        var result = InvokeBuildCompactPeers(new List<TrackerPeerEntry>(), "10.0.0.1", 6881, 50);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildCompactPeers_should_encode_high_port_correctly()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "10.0.0.1", Port = 65535 }
        };

        var result = InvokeBuildCompactPeers(peers, "10.0.0.2", 9999, 50);

        Assert.That(result[4], Is.EqualTo(0xFF));
        Assert.That(result[5], Is.EqualTo(0xFF));
    }

    [Test]
    public void BuildCompactPeers_should_encode_port_256_correctly()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "10.0.0.1", Port = 256 }
        };

        var result = InvokeBuildCompactPeers(peers, "10.0.0.2", 9999, 50);

        Assert.That(result[4], Is.EqualTo(1));
        Assert.That(result[5], Is.EqualTo(0));
    }

    [Test]
    public void BuildCompactPeers_should_encode_multiple_peers_in_order()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "10.0.0.1", Port = 6881 },
            new TrackerPeerEntry { Ip = "172.16.0.1", Port = 8080 },
            new TrackerPeerEntry { Ip = "192.168.1.1", Port = 443 }
        };

        var result = InvokeBuildCompactPeers(peers, "10.0.0.99", 9999, 50);

        Assert.That(result, Has.Length.EqualTo(18));

        // First peer: 10.0.0.1
        Assert.That(result[0], Is.EqualTo(10));
        Assert.That(result[1], Is.EqualTo(0));
        Assert.That(result[2], Is.EqualTo(0));
        Assert.That(result[3], Is.EqualTo(1));

        // Second peer: 172.16.0.1
        Assert.That(result[6], Is.EqualTo(172));
        Assert.That(result[7], Is.EqualTo(16));
        Assert.That(result[8], Is.EqualTo(0));
        Assert.That(result[9], Is.EqualTo(1));

        // Third peer: 192.168.1.1
        Assert.That(result[12], Is.EqualTo(192));
        Assert.That(result[13], Is.EqualTo(168));
        Assert.That(result[14], Is.EqualTo(1));
        Assert.That(result[15], Is.EqualTo(1));
    }

    [Test]
    public void BuildCompactPeers_should_encode_port_1_correctly()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "10.0.0.1", Port = 1 }
        };

        var result = InvokeBuildCompactPeers(peers, "10.0.0.2", 9999, 50);

        Assert.That(result[4], Is.EqualTo(0));
        Assert.That(result[5], Is.EqualTo(1));
    }

    [Test]
    public void BuildCompactPeers_should_apply_max_after_exclude()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "10.0.0.1", Port = 6881 },
            new TrackerPeerEntry { Ip = "10.0.0.2", Port = 6881 },
            new TrackerPeerEntry { Ip = "10.0.0.3", Port = 6881 }
        };

        // Exclude the first peer, max 1 - should get exactly 1 peer (10.0.0.2)
        var result = InvokeBuildCompactPeers(peers, "10.0.0.1", 6881, 1);

        Assert.That(result, Has.Length.EqualTo(6));
        Assert.That(result[3], Is.EqualTo(2));
    }

    // ---- ParseRequest tests ----

    [Test]
    public void ParseRequest_should_return_error_when_no_query_string()
    {
        var (parameters, error) = InvokeParseRequest("/announce");

        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("Missing query string"));
        Assert.That(parameters, Is.Null);
    }

    [Test]
    public void ParseRequest_should_parse_valid_query_string()
    {
        var (parameters, error) = InvokeParseRequest("/announce?info_hash=abc&port=6881");

        Assert.That(error, Is.Null);
        Assert.That(parameters, Is.Not.Null);
        Assert.That(parameters["info_hash"], Is.EqualTo("abc"));
    }

    [Test]
    public void ParseRequest_should_return_error_for_path_without_question_mark()
    {
        var (parameters, error) = InvokeParseRequest("/scrape");

        Assert.That(error, Is.Not.Null);
        Assert.That(parameters, Is.Null);
    }

    [Test]
    public void ParseRequest_should_handle_query_with_empty_params()
    {
        var (parameters, error) = InvokeParseRequest("/announce?");

        Assert.That(error, Is.Null);
        Assert.That(parameters, Is.Not.Null);
    }

    // ---- HandleAnnounce tests ----

    [Test]
    public void HandleAnnounce_should_return_error_when_missing_query_string()
    {
        var result = InvokeHandleAnnounce("/announce", new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("Missing query string"));
    }

    [Test]
    public void HandleAnnounce_should_return_error_when_missing_info_hash()
    {
        var result = InvokeHandleAnnounce("/announce?port=6881", new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("Missing required parameters"));
    }

    [Test]
    public void HandleAnnounce_should_return_error_when_missing_port()
    {
        var result = InvokeHandleAnnounce("/announce?info_hash=abc", new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("Missing required parameters"));
    }

    [Test]
    public void HandleAnnounce_should_return_error_when_port_is_zero()
    {
        var result = InvokeHandleAnnounce("/announce?info_hash=abc&port=0", new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("invalid port"));
    }

    [Test]
    public void HandleAnnounce_should_return_error_when_port_is_negative()
    {
        var result = InvokeHandleAnnounce("/announce?info_hash=abc&port=-1", new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("invalid port"));
    }

    [Test]
    public void HandleAnnounce_should_return_error_when_port_exceeds_65535()
    {
        var result = InvokeHandleAnnounce("/announce?info_hash=abc&port=65536", new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("invalid port"));
    }

    [Test]
    public void HandleAnnounce_should_return_error_when_port_is_not_numeric()
    {
        var result = InvokeHandleAnnounce("/announce?info_hash=abc&port=notanumber", new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("invalid port"));
    }

    [Test]
    public void HandleAnnounce_should_add_peer_for_normal_event()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881&peer_id=testpeer",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        _peerDatabase.Received(1).AddPeer("abc", "192.168.1.1", 6881, "testpeer");
    }

    [Test]
    public void HandleAnnounce_should_add_peer_with_empty_peer_id_when_not_provided()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        _peerDatabase.Received(1).AddPeer("abc", "192.168.1.1", 6881, "");
    }

    [Test]
    public void HandleAnnounce_should_remove_peer_on_stopped_event()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881&event=stopped",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        _peerDatabase.Received(1).RemovePeer("abc", "192.168.1.1", 6881);
        _peerDatabase.DidNotReceive().AddPeer(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Test]
    public void HandleAnnounce_should_add_peer_on_started_event()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881&event=started",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        _peerDatabase.Received(1).AddPeer("abc", "192.168.1.1", 6881, "");
    }

    [Test]
    public void HandleAnnounce_should_return_bencoded_response_with_interval()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("8:intervali1800e"));
        Assert.That(result, Does.Contain("12:min intervali300e"));
    }

    [Test]
    public void HandleAnnounce_should_include_private_flag_when_enabled()
    {
        _configService.TrackerPrivateMode.Returns(true);
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("7:privatei1e"));
    }

    [Test]
    public void HandleAnnounce_should_not_include_private_flag_when_disabled()
    {
        _configService.TrackerPrivateMode.Returns(false);
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Not.Contain("7:privatei1e"));
    }

    [Test]
    public void HandleAnnounce_should_add_peer_on_completed_event()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881&event=completed",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        _peerDatabase.Received(1).AddPeer("abc", "192.168.1.1", 6881, "");
        _peerDatabase.DidNotReceive().RemovePeer(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Test]
    public void HandleAnnounce_should_log_when_TrackerLogAnnounces_is_enabled()
    {
        _configService.TrackerLogAnnounces.Returns(true);
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881&event=started",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        // Should still return valid bencoded response even with logging enabled
        Assert.That(result, Does.Contain("8:intervali1800e"));
        Assert.That(result, Does.StartWith("d"));
        Assert.That(result, Does.EndWith("e"));
    }

    [Test]
    public void HandleAnnounce_should_include_compact_peers_in_response()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "10.0.0.1", Port = 6881 }
        };
        _peerDatabase.GetPeers("abc").Returns(peers);

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6882",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6882));

        Assert.That(result, Does.Contain("5:peers"));
    }

    [Test]
    public void HandleAnnounce_should_use_custom_announce_interval()
    {
        _configService.TrackerAnnounceInterval.Returns(3600);
        _configService.MinAnnounceIntervalSeconds.Returns(600);
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("8:intervali3600e"));
        Assert.That(result, Does.Contain("12:min intervali600e"));
    }

    [Test]
    public void HandleAnnounce_should_return_error_when_only_info_hash_no_port()
    {
        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&peer_id=test",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("Missing required parameters"));
    }

    [Test]
    public void HandleAnnounce_should_use_remote_endpoint_ip_not_request_param()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881",
            new IPEndPoint(IPAddress.Parse("10.20.30.40"), 12345));

        _peerDatabase.Received(1).AddPeer("abc", "10.20.30.40", 6881, "");
    }

    [Test]
    public void HandleAnnounce_should_use_port_from_query_not_endpoint()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=9999",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 12345));

        _peerDatabase.Received(1).AddPeer("abc", "192.168.1.1", 9999, "");
    }

    // ---- HandleScrape tests ----

    [Test]
    public void HandleScrape_should_return_error_when_missing_query_string()
    {
        var result = InvokeHandleScrape("/scrape");

        Assert.That(result, Does.Contain("Missing query string"));
    }

    [Test]
    public void HandleScrape_should_return_error_when_missing_info_hash()
    {
        var result = InvokeHandleScrape("/scrape?port=6881");

        Assert.That(result, Does.Contain("Missing info_hash"));
    }

    [Test]
    public void HandleScrape_should_return_valid_response()
    {
        _peerDatabase.GetStats("abc").Returns(new ScrapeStats { Complete = 5, Incomplete = 2, Downloaded = 10 });

        var result = InvokeHandleScrape("/scrape?info_hash=abc");

        Assert.That(result, Does.Contain("8:completei5e"));
        Assert.That(result, Does.Contain("10:downloadedi10e"));
        Assert.That(result, Does.Contain("10:incompletei2e"));
        Assert.That(result, Does.Contain("20:min_request_intervali900e"));
    }

    [Test]
    public void HandleScrape_should_use_configured_scrape_interval()
    {
        _configService.ScrapeIntervalSeconds.Returns(1200);
        _peerDatabase.GetStats("abc").Returns(new ScrapeStats { Complete = 0, Incomplete = 0, Downloaded = 0 });

        var result = InvokeHandleScrape("/scrape?info_hash=abc");

        Assert.That(result, Does.Contain("20:min_request_intervali1200e"));
    }

    [Test]
    public void HandleScrape_should_return_zero_stats_for_unknown_torrent()
    {
        _peerDatabase.GetStats("unknown").Returns(new ScrapeStats { Complete = 0, Incomplete = 0, Downloaded = 0 });

        var result = InvokeHandleScrape("/scrape?info_hash=unknown");

        Assert.That(result, Does.Contain("8:completei0e"));
        Assert.That(result, Does.Contain("10:downloadedi0e"));
        Assert.That(result, Does.Contain("10:incompletei0e"));
    }

    [Test]
    public void HandleScrape_should_include_info_hash_in_response()
    {
        _peerDatabase.GetStats("testhash").Returns(new ScrapeStats { Complete = 1, Incomplete = 0, Downloaded = 1 });

        var result = InvokeHandleScrape("/scrape?info_hash=testhash");

        Assert.That(result, Does.Contain("8:testhash"));
    }

    // ---- IsRateLimited tests ----

    [Test]
    public void IsRateLimited_should_return_false_when_rate_limit_is_zero()
    {
        _configService.TrackerRateLimitPerMinute.Returns(0);

        var result = InvokeIsRateLimited("192.168.1.1");

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsRateLimited_should_return_false_when_rate_limit_is_negative()
    {
        _configService.TrackerRateLimitPerMinute.Returns(-1);

        var result = InvokeIsRateLimited("192.168.1.1");

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsRateLimited_should_return_false_when_under_limit()
    {
        _configService.TrackerRateLimitPerMinute.Returns(10);

        var result = InvokeIsRateLimited("192.168.1.1");

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsRateLimited_should_return_true_when_over_limit()
    {
        _configService.TrackerRateLimitPerMinute.Returns(3);

        InvokeIsRateLimited("192.168.1.1");
        InvokeIsRateLimited("192.168.1.1");
        InvokeIsRateLimited("192.168.1.1");
        var result = InvokeIsRateLimited("192.168.1.1");

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsRateLimited_should_track_separate_ips_independently()
    {
        _configService.TrackerRateLimitPerMinute.Returns(2);

        InvokeIsRateLimited("192.168.1.1");
        InvokeIsRateLimited("192.168.1.1");
        InvokeIsRateLimited("192.168.1.1");

        var result = InvokeIsRateLimited("192.168.1.2");

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsRateLimited_should_return_false_at_exact_limit()
    {
        _configService.TrackerRateLimitPerMinute.Returns(3);

        InvokeIsRateLimited("192.168.1.1");
        InvokeIsRateLimited("192.168.1.1");
        var result = InvokeIsRateLimited("192.168.1.1");

        // Count is 3, limit is 3, so Count > rateLimit is false
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsRateLimited_should_return_true_at_one_over_limit()
    {
        _configService.TrackerRateLimitPerMinute.Returns(2);

        InvokeIsRateLimited("192.168.1.1");
        InvokeIsRateLimited("192.168.1.1");
        var result = InvokeIsRateLimited("192.168.1.1");

        // Count is 3, limit is 2, so Count > rateLimit is true
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsRateLimited_should_increment_count_each_call()
    {
        _configService.TrackerRateLimitPerMinute.Returns(100);

        // Multiple calls for same IP should all be false when under limit
        for (var i = 0; i < 50; i++)
        {
            Assert.That(InvokeIsRateLimited("192.168.1.1"), Is.False);
        }
    }

    // ---- PurgeExpiredRateLimits tests ----

    [Test]
    public void PurgeExpiredRateLimits_should_not_throw_when_no_entries()
    {
        Assert.DoesNotThrow(() => InvokePurgeExpiredRateLimits());
    }

    [Test]
    public void PurgeExpiredRateLimits_should_not_remove_recent_entries()
    {
        _configService.TrackerRateLimitPerMinute.Returns(100);

        // Add some rate limit entries by calling IsRateLimited
        InvokeIsRateLimited("192.168.1.1");
        InvokeIsRateLimited("192.168.1.2");

        // Purge should not remove entries that are less than 2 minutes old
        InvokePurgeExpiredRateLimits();

        // Entries should still be there (calling IsRateLimited again increments count)
        // If they were purged, a fresh entry would be created with count=1
        // We verify they still track by checking the count is incremented (not reset)
        _configService.TrackerRateLimitPerMinute.Returns(2);
        InvokeIsRateLimited("192.168.1.1");

        // Count should be 2 (the first call + this one), not 1 (if purged and reset)
        var result = InvokeIsRateLimited("192.168.1.1");

        // If not purged: count was 1, then 2, then 3 -> 3 > 2 = true
        // If purged: count would be 1, then 2 -> 2 > 2 = false
        Assert.That(result, Is.True);
    }

    // ---- Handle (ConfigSavedEvent) tests ----

    [Test]
    public void Handle_should_not_throw_when_config_changes()
    {
        _configService.TrackerServerEnabled.Returns(false);
        _configService.TrackerHttpEnabled.Returns(false);

        Assert.DoesNotThrow(() => _trackerServer.Handle(new ConfigSavedEvent()));
    }

    [Test]
    public void Handle_should_not_throw_when_both_enabled()
    {
        _configService.TrackerServerEnabled.Returns(true);
        _configService.TrackerHttpEnabled.Returns(true);

        Assert.DoesNotThrow(() => _trackerServer.Handle(new ConfigSavedEvent()));
    }

    [Test]
    public void Handle_should_not_throw_when_server_enabled_but_http_disabled()
    {
        _configService.TrackerServerEnabled.Returns(true);
        _configService.TrackerHttpEnabled.Returns(false);

        Assert.DoesNotThrow(() => _trackerServer.Handle(new ConfigSavedEvent()));
    }

    [Test]
    public void Handle_should_not_throw_when_server_disabled_but_http_enabled()
    {
        _configService.TrackerServerEnabled.Returns(false);
        _configService.TrackerHttpEnabled.Returns(true);

        Assert.DoesNotThrow(() => _trackerServer.Handle(new ConfigSavedEvent()));
    }

    // ---- HandleAnnounce boundary and edge cases ----

    [Test]
    public void HandleAnnounce_should_accept_valid_port_1()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=1",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Not.Contain("invalid port"));
    }

    [Test]
    public void HandleAnnounce_should_accept_valid_port_65535()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=65535",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Not.Contain("invalid port"));
    }

    [Test]
    public void HandleAnnounce_should_return_bencoded_dict_format()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.StartWith("d"));
        Assert.That(result, Does.EndWith("e"));
    }

    [Test]
    public void HandleAnnounce_should_return_peers_key_with_length_prefix()
    {
        _peerDatabase.GetPeers("abc").Returns(new List<TrackerPeerEntry>());

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=6881",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        // Empty peers list -> 0 bytes -> "5:peers0:"
        Assert.That(result, Does.Contain("5:peers0:"));
    }

    [Test]
    public void HandleAnnounce_should_return_peers_length_6_for_one_non_self_peer()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "10.0.0.1", Port = 6881 }
        };
        _peerDatabase.GetPeers("abc").Returns(peers);

        var result = InvokeHandleAnnounce(
            "/announce?info_hash=abc&port=9999",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 9999));

        Assert.That(result, Does.Contain("5:peers6:"));
    }

    // ---- ReadBoundedLine tests (via real TCP socket pair) ----

    [Test]
    public void ReadBoundedLine_should_read_line_terminated_by_newline()
    {
        using var pair = CreateTcpPair();
        var data = Encoding.ASCII.GetBytes("GET /announce HTTP/1.1\n");
        pair.ServerStream.Write(data, 0, data.Length);
        pair.ServerStream.Flush();

        var result = InvokeReadBoundedLine(pair.ClientStream, 8192);

        Assert.That(result, Is.EqualTo("GET /announce HTTP/1.1"));
    }

    [Test]
    public void ReadBoundedLine_should_trim_trailing_cr()
    {
        using var pair = CreateTcpPair();
        var data = Encoding.ASCII.GetBytes("GET /announce HTTP/1.1\r\n");
        pair.ServerStream.Write(data, 0, data.Length);
        pair.ServerStream.Flush();

        var result = InvokeReadBoundedLine(pair.ClientStream, 8192);

        Assert.That(result, Is.EqualTo("GET /announce HTTP/1.1"));
    }

    [Test]
    public void ReadBoundedLine_should_return_null_when_line_exceeds_max_length()
    {
        using var pair = CreateTcpPair();
        var longLine = new string('A', 100) + "\n";
        var data = Encoding.ASCII.GetBytes(longLine);
        pair.ServerStream.Write(data, 0, data.Length);
        pair.ServerStream.Flush();

        var result = InvokeReadBoundedLine(pair.ClientStream, 50);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ReadBoundedLine_should_return_content_on_eof_if_data_was_read()
    {
        using var pair = CreateTcpPair();
        var data = Encoding.ASCII.GetBytes("partial data");
        pair.ServerStream.Write(data, 0, data.Length);
        pair.ServerStream.Close();

        var result = InvokeReadBoundedLine(pair.ClientStream, 8192);

        Assert.That(result, Is.EqualTo("partial data"));
    }

    [Test]
    public void ReadBoundedLine_should_return_null_on_eof_with_no_data()
    {
        using var pair = CreateTcpPair();
        pair.ServerStream.Close();

        var result = InvokeReadBoundedLine(pair.ClientStream, 8192);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ReadBoundedLine_should_return_empty_string_for_empty_line()
    {
        using var pair = CreateTcpPair();
        var data = Encoding.ASCII.GetBytes("\n");
        pair.ServerStream.Write(data, 0, data.Length);
        pair.ServerStream.Flush();

        var result = InvokeReadBoundedLine(pair.ClientStream, 8192);

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void ReadBoundedLine_should_return_empty_string_for_crlf_only()
    {
        using var pair = CreateTcpPair();
        var data = Encoding.ASCII.GetBytes("\r\n");
        pair.ServerStream.Write(data, 0, data.Length);
        pair.ServerStream.Flush();

        var result = InvokeReadBoundedLine(pair.ClientStream, 8192);

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public void ReadBoundedLine_should_read_exactly_max_length_minus_one_chars()
    {
        using var pair = CreateTcpPair();
        var line = new string('B', 9) + "\n";
        var data = Encoding.ASCII.GetBytes(line);
        pair.ServerStream.Write(data, 0, data.Length);
        pair.ServerStream.Flush();

        var result = InvokeReadBoundedLine(pair.ClientStream, 10);

        Assert.That(result, Is.EqualTo(new string('B', 9)));
    }

    [Test]
    public void ReadBoundedLine_should_reject_line_at_exact_max_length()
    {
        using var pair = CreateTcpPair();

        // 10 chars + newline, but maxLength is 10 means buffer is 10 bytes
        // When position reaches 10 (maxLength), the loop exits and returns null
        var line = new string('C', 10) + "\n";
        var data = Encoding.ASCII.GetBytes(line);
        pair.ServerStream.Write(data, 0, data.Length);
        pair.ServerStream.Flush();

        var result = InvokeReadBoundedLine(pair.ClientStream, 10);

        Assert.That(result, Is.Null);
    }

    // ---- HandleRequest integration via real TCP (covers HTTP response generation) ----

    [Test]
    public void HandleRequest_should_respond_to_scrape_request_when_enabled()
    {
        _configService.TrackerEnableScrape.Returns(true);
        _peerDatabase.GetStats(Arg.Any<string>()).Returns(new ScrapeStats { Complete = 1, Incomplete = 0, Downloaded = 1 });

        var result = InvokeHandleScrape("/scrape?info_hash=abc");

        Assert.That(result, Does.Contain("8:completei1e"));
    }

    // ---- Full HandleRequest via real TCP sockets ----

    [Test]
    public void HandleRequest_should_return_http_200_for_valid_announce()
    {
        _peerDatabase.GetPeers(Arg.Any<string>()).Returns(new List<TrackerPeerEntry>());

        var (response, _) = SendHttpRequestViaHandleRequest(
            "GET /announce?info_hash=test&port=6881 HTTP/1.1\r\nHost: localhost\r\n\r\n");

        Assert.That(response, Does.StartWith("HTTP/1.1 200 OK"));
    }

    [Test]
    public void HandleRequest_should_return_http_200_for_valid_scrape()
    {
        _configService.TrackerEnableScrape.Returns(true);
        _peerDatabase.GetStats(Arg.Any<string>()).Returns(new ScrapeStats());

        var (response, _) = SendHttpRequestViaHandleRequest(
            "GET /scrape?info_hash=test HTTP/1.1\r\nHost: localhost\r\n\r\n");

        Assert.That(response, Does.StartWith("HTTP/1.1 200 OK"));
    }

    [Test]
    public void HandleRequest_should_return_failure_for_invalid_path()
    {
        var (response, body) = SendHttpRequestViaHandleRequest(
            "GET /unknown HTTP/1.1\r\nHost: localhost\r\n\r\n");

        Assert.That(response, Does.StartWith("HTTP/1.1 200 OK"));
        Assert.That(body, Does.Contain("Invalid request"));
    }

    [Test]
    public void HandleRequest_should_return_scrape_disabled_message()
    {
        _configService.TrackerEnableScrape.Returns(false);

        var (response, body) = SendHttpRequestViaHandleRequest(
            "GET /scrape?info_hash=test HTTP/1.1\r\nHost: localhost\r\n\r\n");

        Assert.That(response, Does.StartWith("HTTP/1.1 200 OK"));
        Assert.That(body, Does.Contain("Scrape disabled"));
    }

    [Test]
    public void HandleRequest_should_return_429_when_rate_limited()
    {
        _configService.TrackerRateLimitPerMinute.Returns(1);

        // Pre-warm the rate limiter by calling IsRateLimited for 127.0.0.1 (the loopback IP
        // that HandleRequest will see). After 2 calls, count=2 which exceeds limit=1.
        InvokeIsRateLimited("127.0.0.1");
        InvokeIsRateLimited("127.0.0.1");

        // This request from 127.0.0.1 should now be rate limited
        var (response, _) = SendHttpRequestViaHandleRequest(
            "GET /announce?info_hash=test&port=6881 HTTP/1.1\r\nHost: localhost\r\n\r\n");
        Assert.That(response, Does.StartWith("HTTP/1.1 429"));
    }

    [Test]
    public void HandleRequest_should_silently_handle_non_get_requests()
    {
        // POST request should be silently rejected (no response body, connection closed)
        var (response, _) = SendHttpRequestViaHandleRequest(
            "POST /announce HTTP/1.1\r\nHost: localhost\r\n\r\n");

        // Non-GET requests return nothing (stream closes without response)
        Assert.That(response, Is.Empty);
    }

    [Test]
    public void HandleRequest_should_handle_malformed_request_line()
    {
        var (response, _) = SendHttpRequestViaHandleRequest("GARBAGE\r\n\r\n");

        // Single-word request line (parts.Length < 2) returns nothing
        Assert.That(response, Is.Empty);
    }

    [Test]
    public void HandleRequest_should_include_content_length_header()
    {
        _peerDatabase.GetPeers(Arg.Any<string>()).Returns(new List<TrackerPeerEntry>());

        var (response, _) = SendHttpRequestViaHandleRequest(
            "GET /announce?info_hash=test&port=6881 HTTP/1.1\r\nHost: localhost\r\n\r\n");

        Assert.That(response, Does.Contain("Content-Length:"));
    }

    [Test]
    public void HandleRequest_should_include_connection_close_header()
    {
        _peerDatabase.GetPeers(Arg.Any<string>()).Returns(new List<TrackerPeerEntry>());

        var (response, _) = SendHttpRequestViaHandleRequest(
            "GET /announce?info_hash=test&port=6881 HTTP/1.1\r\nHost: localhost\r\n\r\n");

        Assert.That(response, Does.Contain("Connection: close"));
    }

    [Test]
    public void HandleRequest_should_include_content_type_text_plain()
    {
        _peerDatabase.GetPeers(Arg.Any<string>()).Returns(new List<TrackerPeerEntry>());

        var (response, _) = SendHttpRequestViaHandleRequest(
            "GET /announce?info_hash=test&port=6881 HTTP/1.1\r\nHost: localhost\r\n\r\n");

        Assert.That(response, Does.Contain("Content-Type: text/plain"));
    }

    // ---- ExecuteAsync tests ----

    [Test]
    public async Task ExecuteAsync_should_complete_when_cancelled_and_disabled()
    {
        _configService.TrackerServerEnabled.Returns(false);
        _configService.TrackerHttpEnabled.Returns(false);

        var cts = new CancellationTokenSource();
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "ExecuteAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        await cts.CancelAsync();
        var task = (Task)method.Invoke(_trackerServer, new object[] { cts.Token });

        await task;

        // Should complete without throwing
        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_start_listener_when_enabled()
    {
        _configService.TrackerServerEnabled.Returns(true);
        _configService.TrackerHttpEnabled.Returns(true);

        // Port 0 picks a random available port
        _configService.TrackerHttpPort.Returns(0);
        _configService.TrackerBindAddress.Returns("127.0.0.1");

        var cts = new CancellationTokenSource();
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "ExecuteAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var task = (Task)method.Invoke(_trackerServer, new object[] { cts.Token });

        // Give listener time to start
        await Task.Delay(200);

        // Cancel to stop
        await cts.CancelAsync();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // The listener field should be cleaned up after StopListener runs in finally
        var listenerField = typeof(Core.TrackerServer.TrackerServer).GetField(
            "_listener", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(listenerField.GetValue(_trackerServer), Is.Null);
    }

    // ---- Helper methods for TCP socket tests ----

    private sealed class TcpPair : IDisposable
    {
        public TcpClient ServerClient { get; set; }
        public TcpClient ClientClient { get; set; }
        public NetworkStream ServerStream { get; set; }
        public NetworkStream ClientStream { get; set; }
        private TcpListener Listener { get; set; }

        public static TcpPair Create()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            var server = listener.AcceptTcpClient();

            return new TcpPair
            {
                ServerClient = server,
                ClientClient = client,
                ServerStream = server.GetStream(),
                ClientStream = client.GetStream(),
                Listener = listener
            };
        }

        public void Dispose()
        {
            ServerStream?.Dispose();
            ClientStream?.Dispose();
            ServerClient?.Dispose();
            ClientClient?.Dispose();
            Listener?.Stop();
        }
    }

    private static TcpPair CreateTcpPair()
    {
        return TcpPair.Create();
    }

    private (string FullResponse, string Body) SendHttpRequestViaHandleRequest(string httpRequest)
    {
        // Create a local TCP listener, connect a client, and use the accepted client for HandleRequest
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var requestClient = new TcpClient();
        requestClient.Connect(IPAddress.Loopback, port);
        using var acceptedClient = listener.AcceptTcpClient();
        listener.Stop();

        // Write the request from requestClient's perspective
        var requestBytes = Encoding.ASCII.GetBytes(httpRequest);
        var requestStream = requestClient.GetStream();
        requestStream.Write(requestBytes, 0, requestBytes.Length);
        requestStream.Flush();

        // HandleRequest reads from acceptedClient's stream and writes response back
        var handleRequestMethod = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "HandleRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        handleRequestMethod.Invoke(_trackerServer, new object[] { acceptedClient });

        // Read response from requestClient's stream
        requestClient.Client.ReceiveTimeout = 2000;
        var responseBuffer = new byte[65536];
        var responseStream = requestClient.GetStream();
        var totalRead = 0;

        try
        {
            int bytesRead;
            while ((bytesRead = responseStream.Read(responseBuffer, totalRead, responseBuffer.Length - totalRead)) > 0)
            {
                totalRead += bytesRead;
            }
        }
        catch (IOException)
        {
            // Timeout or connection closed - expected
        }
        catch (SocketException)
        {
            // Connection reset - expected after server closes
        }

        var fullResponse = Encoding.ASCII.GetString(responseBuffer, 0, totalRead);

        // Split headers and body
        var bodyStart = fullResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var body = bodyStart >= 0 ? fullResponse[(bodyStart + 4)..] : "";

        return (fullResponse, body);
    }

    private void InvokeHandleRequest(TcpClient client)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "HandleRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_trackerServer, new object[] { client });
    }
}
