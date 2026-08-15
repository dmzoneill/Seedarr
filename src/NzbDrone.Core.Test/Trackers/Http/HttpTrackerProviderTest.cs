using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Http;

namespace NzbDrone.Core.Test.Trackers.Http;

[TestFixture]
public class HttpTrackerProviderTest
{
    private HttpTrackerProvider _provider;
    private IConfigService _configService;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.HttpTrackerTimeoutSeconds.Returns(10);
        _configService.BitTorrentUserAgent.Returns("qBittorrent/4.4.2");
        _provider = new HttpTrackerProvider(_configService);
    }

    [Test]
    public void Name_should_return_http()
    {
        Assert.That(_provider.Name, Is.EqualTo("HTTP"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_tracker_url()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.TrackerUrl = "http://tracker.example.com:8080/announce";

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.StartWith("http://tracker.example.com:8080/announce?"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_info_hash()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.InfoHash = "AABBCCDDEE112233445566778899AABBCCDDEEFF";

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("info_hash=%AA%BB%CC%DD%EE%11%22%33%44%55%66%77%88%99%AA%BB%CC%DD%EE%FF"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_peer_id()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("peer_id="));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_port()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Port = 12345;

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("port=12345"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_uploaded()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Uploaded = 5000;

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("uploaded=5000"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_downloaded()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Downloaded = 3000;

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("downloaded=3000"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_left()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Left = 10000;

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("left=10000"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_compact_1_when_true()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Compact = true;

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("compact=1"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_compact_0_when_false()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Compact = false;

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("compact=0"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_numwant()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.NumWant = 100;

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("numwant=100"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_event_when_set()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = "started";

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("event=started"));
    }

    [Test]
    public void BuildAnnounceUrl_should_exclude_event_when_empty()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = "";

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Not.Contain("event="));
    }

    [Test]
    public void BuildAnnounceUrl_should_exclude_event_when_null()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = null;

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Not.Contain("event="));
    }

    [Test]
    public void Announce_should_return_failure_on_exception()
    {
        var request = CreateRequest();
        request.TrackerUrl = "http://nonexistent.invalid:9999/announce";

        var result = _provider.Announce(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.Not.Empty);
    }

    [Test]
    public void Scrape_should_return_failure_on_exception()
    {
        var result = _provider.Scrape(
            "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            "http://nonexistent.invalid:9999/announce");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.Not.Empty);
    }

    [Test]
    public void BuildAnnounceUrl_should_url_encode_special_characters_in_peer_id()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.PeerId = "-qB4420-abc def&ghi=jkl";

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Not.Contain("peer_id=-qB4420-abc def&ghi=jkl"));
        Assert.That(result, Does.Contain("peer_id="));
        Assert.That(result, Does.Not.Contain("peer_id=-qB4420-abc def"));
    }

    [Test]
    public void BuildAnnounceUrl_should_handle_zero_values_for_upload_download_left()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Uploaded = 0;
        request.Downloaded = 0;
        request.Left = 0;

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("uploaded=0"));
        Assert.That(result, Does.Contain("downloaded=0"));
        Assert.That(result, Does.Contain("left=0"));
    }

    [Test]
    public void BuildAnnounceUrl_should_handle_large_uploaded_value()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Uploaded = 10_737_418_240; // 10 GB

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("uploaded=10737418240"));
    }

    [Test]
    public void BuildAnnounceUrl_should_handle_large_downloaded_value()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Downloaded = 53_687_091_200; // 50 GB

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("downloaded=53687091200"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_event_completed()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = "completed";

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("&event=completed"));
    }

    [Test]
    public void BuildAnnounceUrl_should_include_event_stopped()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = "stopped";

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.Contain("&event=stopped"));
    }

    [Test]
    public void BuildAnnounceUrl_should_construct_url_with_parameters_in_correct_order()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.Event = "started";

        var result = (string)method.Invoke(null, new object[] { request });

        var queryStart = result.IndexOf('?');
        Assert.That(queryStart, Is.GreaterThan(0));

        var queryString = result.Substring(queryStart + 1);
        var infoHashPos = queryString.IndexOf("info_hash=");
        var peerIdPos = queryString.IndexOf("peer_id=");
        var portPos = queryString.IndexOf("port=");
        var uploadedPos = queryString.IndexOf("uploaded=");
        var downloadedPos = queryString.IndexOf("downloaded=");
        var leftPos = queryString.IndexOf("left=");
        var compactPos = queryString.IndexOf("compact=");
        var numwantPos = queryString.IndexOf("numwant=");
        var eventPos = queryString.IndexOf("event=");

        Assert.That(infoHashPos, Is.LessThan(peerIdPos));
        Assert.That(peerIdPos, Is.LessThan(portPos));
        Assert.That(portPos, Is.LessThan(uploadedPos));
        Assert.That(uploadedPos, Is.LessThan(downloadedPos));
        Assert.That(downloadedPos, Is.LessThan(leftPos));
        Assert.That(leftPos, Is.LessThan(compactPos));
        Assert.That(compactPos, Is.LessThan(numwantPos));
        Assert.That(numwantPos, Is.LessThan(eventPos));
    }

    [Test]
    public void BuildAnnounceUrl_should_produce_correct_full_url()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = new TrackerAnnounceRequest
        {
            TrackerUrl = "http://tracker.test.com/announce",
            InfoHash = "0000000000000000000000000000000000000000",
            PeerId = "-qB4420-123456789012",
            Port = 6881,
            Uploaded = 100,
            Downloaded = 200,
            Left = 300,
            Compact = true,
            NumWant = 25,
            Event = "started"
        };

        var result = (string)method.Invoke(null, new object[] { request });

        Assert.That(result, Does.StartWith("http://tracker.test.com/announce?"));
        Assert.That(result, Does.Contain("info_hash=%00%00%00%00%00%00%00%00%00%00%00%00%00%00%00%00%00%00%00%00"));
        Assert.That(result, Does.Contain("port=6881"));
        Assert.That(result, Does.Contain("uploaded=100"));
        Assert.That(result, Does.Contain("downloaded=200"));
        Assert.That(result, Does.Contain("left=300"));
        Assert.That(result, Does.Contain("compact=1"));
        Assert.That(result, Does.Contain("numwant=25"));
        Assert.That(result, Does.Contain("event=started"));
    }

    [Test]
    public void BuildAnnounceUrl_should_escape_info_hash_bytes_as_uppercase_hex()
    {
        var method = typeof(HttpTrackerProvider).GetMethod("BuildAnnounceUrl", BindingFlags.NonPublic | BindingFlags.Static);
        var request = CreateRequest();
        request.InfoHash = "aabbccddee112233445566778899aabbccddeeff";

        var result = (string)method.Invoke(null, new object[] { request });

        // The format string $"%{b:X2}" always produces uppercase hex
        Assert.That(result, Does.Contain("info_hash=%AA%BB%CC%DD%EE%11%22%33%44%55%66%77%88%99%AA%BB%CC%DD%EE%FF"));
    }

    [Test]
    public void Announce_should_return_failure_for_malformed_url()
    {
        var request = CreateRequest();
        request.TrackerUrl = "not-a-valid-url";

        var result = _provider.Announce(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Announce_should_return_failure_for_empty_tracker_url()
    {
        var request = CreateRequest();
        request.TrackerUrl = "";

        var result = _provider.Announce(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Scrape_should_return_failure_for_invalid_hex_info_hash()
    {
        var result = _provider.Scrape(
            "ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ",
            "http://nonexistent.invalid:9999/announce");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Scrape_should_return_failure_for_odd_length_info_hash()
    {
        var result = _provider.Scrape(
            "AABBCCDDEE112233445566778899AABBCCDDEEF",
            "http://nonexistent.invalid:9999/announce");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Scrape_should_return_failure_for_empty_info_hash()
    {
        var result = _provider.Scrape(
            "",
            "http://nonexistent.invalid:9999/announce");

        // Empty hex string converts to empty byte array, then the HTTP call fails
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Scrape_should_return_failure_for_url_without_announce_path()
    {
        var result = _provider.Scrape(
            "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            "http://nonexistent.invalid:9999/tracker");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.Not.Null.And.Not.Empty);
    }

    private static TrackerAnnounceRequest CreateRequest()
    {
        return new TrackerAnnounceRequest
        {
            TrackerUrl = "http://tracker.example.com/announce",
            InfoHash = "AABBCCDDEE112233445566778899AABBCCDDEEFF",
            PeerId = "-qB4420-abcdefghijkl",
            Port = 6881,
            Uploaded = 0,
            Downloaded = 0,
            Left = 1000,
            Compact = true,
            NumWant = 50
        };
    }

    // ---- helpers for response-parsing tests ----

    private HttpTrackerProvider CreateProviderWithResponse(byte[] responseBytes)
    {
        var provider = new HttpTrackerProvider(_configService);
        var handler = new FixedResponseHandler(responseBytes);
        var client = new HttpClient(handler);
        var field = typeof(HttpTrackerProvider).GetField("_client",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(provider, client);
        return provider;
    }

    private static byte[] BencodeBytes(BDictionary dict) => dict.EncodeAsBytes();

    // ---- Announce response-parsing tests ----

    [Test]
    public void Announce_should_return_failure_when_tracker_responds_with_failure_reason()
    {
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["failure reason"] = new BString("tracker overloaded")
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("tracker overloaded"));
    }

    [Test]
    public void Announce_should_return_success_when_response_has_no_optional_fields()
    {
        var responseBytes = BencodeBytes(new BDictionary());
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Success, Is.True);
        Assert.That(result.Interval, Is.EqualTo(1800));
        Assert.That(result.MinInterval, Is.EqualTo(900));
        Assert.That(result.Complete, Is.EqualTo(0));
        Assert.That(result.Incomplete, Is.EqualTo(0));
        Assert.That(result.Peers, Is.Empty);
    }

    [Test]
    public void Announce_should_parse_interval_from_response()
    {
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["interval"] = new BNumber(3600)
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Success, Is.True);
        Assert.That(result.Interval, Is.EqualTo(3600));
    }

    [Test]
    public void Announce_should_parse_min_interval_from_response()
    {
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["interval"] = new BNumber(1800),
            ["min interval"] = new BNumber(600)
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.MinInterval, Is.EqualTo(600));
    }

    [Test]
    public void Announce_should_parse_complete_and_incomplete_from_response()
    {
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["complete"] = new BNumber(42),
            ["incomplete"] = new BNumber(7)
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Complete, Is.EqualTo(42));
        Assert.That(result.Incomplete, Is.EqualTo(7));
    }

    [Test]
    public void Announce_should_parse_warning_message_from_response()
    {
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["warning message"] = new BString("tracker maintenance soon")
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Success, Is.True);
        Assert.That(result.WarningMessage, Is.EqualTo("tracker maintenance soon"));
    }

    [Test]
    public void Announce_should_parse_compact_peers_from_response()
    {
        // Compact peer: 192.168.1.100:6881 = [192,168,1,100, 26(0x1A), 225(0xE1)]
        var peerBytes = new byte[] { 192, 168, 1, 100, 0x1A, 0xE1 };
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["peers"] = new BString(peerBytes)
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers.Count, Is.EqualTo(1));
        Assert.That(result.Peers[0].Ip, Is.EqualTo("192.168.1.100"));
        Assert.That(result.Peers[0].Port, Is.EqualTo(6881));
    }

    [Test]
    public void Announce_should_parse_multiple_compact_peers_from_response()
    {
        // Two compact peers: 10.0.0.1:8080 and 172.16.0.5:51413
        var peerBytes = new byte[]
        {
            10, 0, 0, 1, (byte)(8080 >> 8), (byte)(8080 & 0xFF),
            172, 16, 0, 5, (byte)(51413 >> 8), (byte)(51413 & 0xFF)
        };
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["peers"] = new BString(peerBytes)
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Peers.Count, Is.EqualTo(2));
        Assert.That(result.Peers[0].Ip, Is.EqualTo("10.0.0.1"));
        Assert.That(result.Peers[0].Port, Is.EqualTo(8080));
        Assert.That(result.Peers[1].Ip, Is.EqualTo("172.16.0.5"));
        Assert.That(result.Peers[1].Port, Is.EqualTo(51413));
    }

    [Test]
    public void Announce_should_ignore_incomplete_compact_peer_at_end()
    {
        // 5 bytes (incomplete peer) - loop condition: i + 5 < data.Length => 0 + 5 < 5 => false
        var peerBytes = new byte[] { 10, 0, 0, 1, 0x1A };
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["peers"] = new BString(peerBytes)
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Peers, Is.Empty);
    }

    [Test]
    public void Announce_should_parse_dictionary_peers_from_response()
    {
        var peerDict = new BDictionary
        {
            ["ip"] = new BString("127.0.0.1"),
            ["port"] = new BNumber(8080)
        };
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["peers"] = new BList { peerDict }
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Peers.Count, Is.EqualTo(1));
        Assert.That(result.Peers[0].Ip, Is.EqualTo("127.0.0.1"));
        Assert.That(result.Peers[0].Port, Is.EqualTo(8080));
        Assert.That(result.Peers[0].PeerId, Is.Null);
    }

    [Test]
    public void Announce_should_parse_peer_id_from_dictionary_peers()
    {
        var peerDict = new BDictionary
        {
            ["ip"] = new BString("10.0.0.2"),
            ["port"] = new BNumber(6881),
            ["peer id"] = new BString("-BT1000-abcdefghijkl")
        };
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["peers"] = new BList { peerDict }
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Peers.Count, Is.EqualTo(1));
        Assert.That(result.Peers[0].PeerId, Is.EqualTo("-BT1000-abcdefghijkl"));
    }

    [Test]
    public void Announce_should_return_empty_peers_when_no_peers_key_in_response()
    {
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["interval"] = new BNumber(1800)
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Announce(CreateRequest());

        Assert.That(result.Success, Is.True);
        Assert.That(result.Peers, Is.Empty);
    }

    // ---- Scrape response-parsing tests ----

    [Test]
    public void Scrape_should_return_failure_when_tracker_responds_with_failure_reason()
    {
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["failure reason"] = new BString("info hash not found")
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Scrape("AABBCCDDEE112233445566778899AABBCCDDEEFF",
            "http://tracker.example.com/announce");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo("info hash not found"));
    }

    [Test]
    public void Scrape_should_parse_files_response_with_complete_downloaded_incomplete()
    {
        var hashKey = new BString("placeholder");
        var fileInfo = new BDictionary
        {
            ["complete"] = new BNumber(100),
            ["incomplete"] = new BNumber(15),
            ["downloaded"] = new BNumber(500)
        };
        var filesDict = new BDictionary { [hashKey] = fileInfo };
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["files"] = filesDict
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Scrape("AABBCCDDEE112233445566778899AABBCCDDEEFF",
            "http://tracker.example.com/announce");

        Assert.That(result.Success, Is.True);
        Assert.That(result.Complete, Is.EqualTo(100));
        Assert.That(result.Incomplete, Is.EqualTo(15));
        Assert.That(result.Downloaded, Is.EqualTo(500));
    }

    [Test]
    public void Scrape_should_return_success_with_zeros_when_files_dict_is_empty()
    {
        var responseBytes = BencodeBytes(new BDictionary
        {
            ["files"] = new BDictionary()
        });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Scrape("AABBCCDDEE112233445566778899AABBCCDDEEFF",
            "http://tracker.example.com/announce");

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void Scrape_should_return_success_when_no_files_key_in_response()
    {
        var responseBytes = BencodeBytes(new BDictionary());
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Scrape("AABBCCDDEE112233445566778899AABBCCDDEEFF",
            "http://tracker.example.com/announce");

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void Scrape_should_use_default_zero_when_files_dict_entry_missing_complete()
    {
        var fileInfo = new BDictionary
        {
            ["downloaded"] = new BNumber(99)
        };
        var filesDict = new BDictionary { [new BString("x")] = fileInfo };
        var responseBytes = BencodeBytes(new BDictionary { ["files"] = filesDict });
        var provider = CreateProviderWithResponse(responseBytes);

        var result = provider.Scrape("AABBCCDDEE112233445566778899AABBCCDDEEFF",
            "http://tracker.example.com/announce");

        Assert.That(result.Complete, Is.EqualTo(0));
        Assert.That(result.Incomplete, Is.EqualTo(0));
        Assert.That(result.Downloaded, Is.EqualTo(99));
    }

    // ---- private mock handler ----

    private sealed class FixedResponseHandler : HttpMessageHandler
    {
        private readonly byte[] _responseBytes;

        public FixedResponseHandler(byte[] responseBytes)
        {
            _responseBytes = responseBytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_responseBytes)
            };
            return Task.FromResult(response);
        }
    }
}
