using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.TrackerServer;
using NzbDrone.Core.TrackerServer.Users;

namespace NzbDrone.Core.Test.TrackerServer;

[TestFixture]
public class TrackerServerPasskeyTests
{
    private const string DefaultInfoHash = "0123456789abcdef0123456789abcdef01234567";
    private const string DefaultPeerId = "01234567890123456789";

    private Core.TrackerServer.TrackerServer _trackerServer;
    private IPeerDatabase _peerDatabase;
    private IConfigService _configService;
    private TrackerUserService _trackerUserService;

    [SetUp]
    public void Setup()
    {
        _peerDatabase = Substitute.For<IPeerDatabase>();
        _configService = Substitute.For<IConfigService>();
        _trackerUserService = new TrackerUserService();

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
        _configService.TrackerPasskeyAuthEnabled.Returns(false);

        _peerDatabase.GetPeers(Arg.Any<string>()).Returns(new List<TrackerPeerEntry>());

        _trackerServer = new Core.TrackerServer.TrackerServer(
            _peerDatabase,
            _configService,
            new ScrapeCache(),
            _trackerUserService);
    }

    [Test]
    public void ExtractPasskey_from_path_returns_passkey()
    {
        var passkey = "0123456789abcdef0123456789abcdef"; // gitleaks:allow

        Assert.That(
            Core.TrackerServer.TrackerServer.ExtractPasskey($"/announce/{passkey}"),
            Is.EqualTo(passkey));

        Assert.That(
            Core.TrackerServer.TrackerServer.ExtractPasskey($"/announce/{passkey}?info_hash={DefaultInfoHash}"),
            Is.EqualTo(passkey));

        Assert.That(
            Core.TrackerServer.TrackerServer.ExtractPasskey($"/announce/{passkey}/"),
            Is.EqualTo(passkey));

        Assert.That(
            Core.TrackerServer.TrackerServer.ExtractPasskey($"/announce/{passkey}/extra?info_hash={DefaultInfoHash}"),
            Is.EqualTo(passkey));
    }

    [Test]
    public void ExtractPasskey_from_query_returns_passkey()
    {
        var passkey = "querypasskey1234567890abcdef1234";

        Assert.That(
            Core.TrackerServer.TrackerServer.ExtractPasskey($"/announce?passkey={passkey}"),
            Is.EqualTo(passkey));

        Assert.That(
            Core.TrackerServer.TrackerServer.ExtractPasskey($"/announce?info_hash={DefaultInfoHash}&passkey={passkey}&port=6881"),
            Is.EqualTo(passkey));
    }

    [Test]
    public void ExtractPasskey_returns_null_when_no_passkey()
    {
        Assert.That(Core.TrackerServer.TrackerServer.ExtractPasskey("/announce"), Is.Null);
        Assert.That(Core.TrackerServer.TrackerServer.ExtractPasskey("/announce/"), Is.Null);
        Assert.That(Core.TrackerServer.TrackerServer.ExtractPasskey($"/announce?info_hash={DefaultInfoHash}"), Is.Null);
        Assert.That(Core.TrackerServer.TrackerServer.ExtractPasskey("/announce?passkey="), Is.Null);
        Assert.That(Core.TrackerServer.TrackerServer.ExtractPasskey("/announce/   "), Is.Null);
        Assert.That(Core.TrackerServer.TrackerServer.ExtractPasskey(null), Is.Null);
    }

    [Test]
    public void ExtractPasskey_prioritizes_path_over_query()
    {
        var pathPasskey = "pathpasskey111111111111111111111";
        var queryPasskey = "querypasskey22222222222222222222";

        var extracted = Core.TrackerServer.TrackerServer.ExtractPasskey(
            $"/announce/{pathPasskey}?passkey={queryPasskey}&info_hash={DefaultInfoHash}");

        Assert.That(extracted, Is.EqualTo(pathPasskey));
    }

    [Test]
    public void HandleAnnounce_when_auth_enabled_and_missing_passkey_returns_missing_passkey_error()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);

        var result = InvokeHandleAnnounceText(
            $"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("Missing announce passkey"));
        Assert.That(result, Is.EqualTo("d14:failure reason25:Missing announce passkeye"));
    }

    [Test]
    public void HandleAnnounce_when_auth_enabled_and_unknown_passkey_in_path_returns_invalid_or_revoked_passkey()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);

        var result = InvokeHandleAnnounceText(
            $"/announce/unknownpasskey1234567890abcdef?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("Invalid or revoked passkey"));
        Assert.That(result, Is.EqualTo("d14:failure reason27:Invalid or revoked passkeye"));
    }

    [Test]
    public void HandleAnnounce_when_auth_enabled_and_unknown_passkey_in_query_returns_invalid_or_revoked_passkey()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);

        var result = InvokeHandleAnnounceText(
            $"/announce?passkey=unknownpasskey1234567890abcdef&info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("Invalid or revoked passkey"));
        Assert.That(result, Is.EqualTo("d14:failure reason27:Invalid or revoked passkeye"));
    }

    [Test]
    public void HandleAnnounce_when_auth_enabled_and_user_disabled_returns_invalid_or_revoked_passkey()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);
        var user = _trackerUserService.AddUser("disabled_user", "disabledpasskey1234567890abcdef");
        user.IsEnabled = false;
        _trackerUserService.UpdateUser(user);

        var result = InvokeHandleAnnounceText(
            $"/announce/disabledpasskey1234567890abcdef?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("Invalid or revoked passkey"));
        Assert.That(result, Is.EqualTo("d14:failure reason27:Invalid or revoked passkeye"));
    }

    [Test]
    public void HandleAnnounce_when_auth_enabled_and_user_banned_returns_invalid_or_revoked_passkey()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);
        var user = _trackerUserService.AddUser("banned_user", "bannedpasskey1234567890abcdef12");
        user.IsBanned = true;
        _trackerUserService.UpdateUser(user);

        var result = InvokeHandleAnnounceText(
            $"/announce/bannedpasskey1234567890abcdef12?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0",
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881));

        Assert.That(result, Does.Contain("Invalid or revoked passkey"));
        Assert.That(result, Is.EqualTo("d14:failure reason27:Invalid or revoked passkeye"));
    }

    [Test]
    public void HandleAnnounce_when_auth_enabled_and_valid_passkey_succeeds_and_processes_announce()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);
        var user = _trackerUserService.AddUser("valid_user", "validpasskey1234567890abcdef123");

        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881);
        var result = InvokeHandleAnnounceText(
            $"/announce/{user.Passkey}?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=1000&downloaded=500&left=0",
            endpoint);

        Assert.That(result, Does.Contain("interval"));
        _peerDatabase.Received(1).AddPeer(DefaultInfoHash, "192.168.1.1", 6881, DefaultPeerId);
        _peerDatabase.Received(1).IncrementAnnounces();

        var updatedUser = _trackerUserService.GetByPasskey(user.Passkey);
        Assert.That(updatedUser.Uploaded, Is.EqualTo(1000));
        Assert.That(updatedUser.Downloaded, Is.EqualTo(500));
        Assert.That(updatedUser.LastAnnounceAt, Is.Not.Null);
    }

    [Test]
    public void HandleAnnounce_when_auth_disabled_allows_announce_without_passkey()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(false);

        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881);
        var result = InvokeHandleAnnounceText(
            $"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0",
            endpoint);

        Assert.That(result, Does.Contain("interval"));
        _peerDatabase.Received(1).AddPeer(DefaultInfoHash, "192.168.1.1", 6881, DefaultPeerId);
    }

    [Test]
    public void HandleAnnounce_when_auth_disabled_allows_announce_with_unknown_passkey()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(false);

        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881);
        var result = InvokeHandleAnnounceText(
            $"/announce/somepasskey?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0",
            endpoint);

        Assert.That(result, Does.Contain("interval"));
        _peerDatabase.Received(1).AddPeer(DefaultInfoHash, "192.168.1.1", 6881, DefaultPeerId);
    }

    [Test]
    public void HandleAnnounce_records_traffic_deltas_cumulatively()
    {
        var user = _trackerUserService.AddUser("traffic_user", "trafficpasskey1234567890abcdef");
        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 6881);

        // First announce: uploaded=1000, downloaded=500
        InvokeHandleAnnounceText(
            $"/announce/{user.Passkey}?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=1000&downloaded=500&left=1000",
            endpoint);

        var current = _trackerUserService.GetByPasskey(user.Passkey);
        Assert.That(current.Uploaded, Is.EqualTo(1000));
        Assert.That(current.Downloaded, Is.EqualTo(500));

        // Second announce: uploaded=1600 (+600), downloaded=700 (+200)
        InvokeHandleAnnounceText(
            $"/announce/{user.Passkey}?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=1600&downloaded=700&left=400",
            endpoint);

        current = _trackerUserService.GetByPasskey(user.Passkey);
        Assert.That(current.Uploaded, Is.EqualTo(1600));
        Assert.That(current.Downloaded, Is.EqualTo(700));

        // Client reset: uploaded=300, downloaded=100 (client restarted torrent session)
        InvokeHandleAnnounceText(
            $"/announce/{user.Passkey}?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=300&downloaded=100&left=300",
            endpoint);

        current = _trackerUserService.GetByPasskey(user.Passkey);
        Assert.That(current.Uploaded, Is.EqualTo(1900));
        Assert.That(current.Downloaded, Is.EqualTo(800));
    }

    [Test]
    public void TrackerUserService_CRUD_operations_work_correctly()
    {
        var service = new TrackerUserService();

        var user1 = service.AddUser("alice");
        Assert.That(user1.Id, Is.GreaterThan(0));
        Assert.That(user1.Username, Is.EqualTo("alice"));
        Assert.That(user1.Passkey, Is.Not.Null.And.Not.Empty);
        Assert.That(user1.IsEnabled, Is.True);
        Assert.That(user1.IsBanned, Is.False);

        var user2 = service.AddUser("bob", "custombobpasskey1234567890123456");
        Assert.That(user2.Passkey, Is.EqualTo("custombobpasskey1234567890123456"));

        Assert.That(service.GetById(user1.Id), Is.SameAs(user1));
        Assert.That(service.GetByPasskey(user1.Passkey), Is.SameAs(user1));
        Assert.That(service.GetByPasskey("nonexistent"), Is.Null);

        var all = service.GetAll();
        Assert.That(all.Count, Is.EqualTo(2));

        user1.Username = "alice_updated";
        user1.Passkey = "newalicepasskey1234567890123456"; // gitleaks:allow
        service.UpdateUser(user1);

        Assert.That(service.GetById(user1.Id).Username, Is.EqualTo("alice_updated"));
        Assert.That(service.GetByPasskey("newalicepasskey1234567890123456"), Is.SameAs(user1));

        var deleted = service.DeleteUser(user1.Id);
        Assert.That(deleted, Is.True);
        Assert.That(service.GetById(user1.Id), Is.Null);
        Assert.That(service.GetByPasskey("newalicepasskey1234567890123456"), Is.Null);
        Assert.That(service.GetAll().Count, Is.EqualTo(1));
    }

    private byte[] InvokeHandleAnnounce(string path, IPEndPoint remoteEndpoint)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "HandleAnnounce",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (byte[])method.Invoke(_trackerServer, new object[] { path, remoteEndpoint });
    }

    private string InvokeHandleAnnounceText(string path, IPEndPoint remoteEndpoint)
        => Encoding.ASCII.GetString(InvokeHandleAnnounce(path, remoteEndpoint));
}
