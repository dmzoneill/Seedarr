using System;
using System.Collections.Generic;
using System.Text;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers.Encryption;

[TestFixture]
public class MseSkeyRegistryTest
{
    private const string InfoHashA = "0102030405060708091011121314151617181920";
    private const string InfoHashB = "A1A2A3A4A5A6A7A8A9A0B1B2B3B4B5B6B7B8B9B0";
    private const string InfoHashC = "0F1E2D3C4B5A69788796A5B4C3D2E1F001020304";

    private static byte[] ComputeExpectedSkeyHash(string infoHash)
    {
        var infoHashBytes = Convert.FromHexString(infoHash);
        return MseKeyDerivation.DeriveKey(infoHashBytes, Encoding.ASCII.GetBytes("req2"));
    }

    [Test]
    public void RegisterTorrent_should_precompute_req2_skey_hash_accurately()
    {
        var registry = new MseSkeyRegistry();
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test Torrent A",
            InfoHash = InfoHashA
        };

        registry.RegisterTorrent(torrent);

        var expectedSkeyHash = ComputeExpectedSkeyHash(InfoHashA);
        var matched = registry.TryMatchTorrent(expectedSkeyHash, out var matchedTorrent);

        Assert.That(matched, Is.True);
        Assert.That(matchedTorrent, Is.SameAs(torrent));
        Assert.That(registry.Count, Is.EqualTo(1));
    }

    [Test]
    public void TryMatchTorrent_should_return_correct_torrent_in_O1()
    {
        var registry = new MseSkeyRegistry();
        var torrentA = new Torrent { Id = 1, Name = "Torrent A", InfoHash = InfoHashA };
        var torrentB = new Torrent { Id = 2, Name = "Torrent B", InfoHash = InfoHashB };
        var torrentC = new Torrent { Id = 3, Name = "Torrent C", InfoHash = InfoHashC };

        registry.RegisterTorrent(torrentA);
        registry.RegisterTorrent(torrentB);
        registry.RegisterTorrent(torrentC);

        Assert.That(registry.Count, Is.EqualTo(3));

        var skeyHashB = ComputeExpectedSkeyHash(InfoHashB);
        var matched = registry.TryMatchTorrent(skeyHashB, out var matchedTorrent);

        Assert.That(matched, Is.True);
        Assert.That(matchedTorrent, Is.SameAs(torrentB));
        Assert.That(matchedTorrent.InfoHash, Is.EqualTo(InfoHashB));
    }

    [Test]
    public void TryMatchTorrent_should_return_false_for_unknown_hash()
    {
        var registry = new MseSkeyRegistry();
        var torrent = new Torrent { Id = 1, Name = "Torrent A", InfoHash = InfoHashA };
        registry.RegisterTorrent(torrent);

        var unknownHash = new byte[20];
        Array.Fill<byte>(unknownHash, 0xFF);

        var matched = registry.TryMatchTorrent(unknownHash, out var matchedTorrent);

        Assert.That(matched, Is.False);
        Assert.That(matchedTorrent, Is.Null);
    }

    [Test]
    public void TryMatchTorrent_should_return_false_for_null_or_empty_hash()
    {
        var registry = new MseSkeyRegistry();

        Assert.That(registry.TryMatchTorrent(null, out var torrent1), Is.False);
        Assert.That(torrent1, Is.Null);

        Assert.That(registry.TryMatchTorrent(Array.Empty<byte>(), out var torrent2), Is.False);
        Assert.That(torrent2, Is.Null);
    }

    [Test]
    public void UnregisterTorrent_should_remove_torrent_from_registry()
    {
        var registry = new MseSkeyRegistry();
        var torrent = new Torrent { Id = 1, Name = "Torrent A", InfoHash = InfoHashA };
        registry.RegisterTorrent(torrent);

        var skeyHash = ComputeExpectedSkeyHash(InfoHashA);
        Assert.That(registry.TryMatchTorrent(skeyHash, out _), Is.True);

        registry.UnregisterTorrent(InfoHashA);

        Assert.That(registry.TryMatchTorrent(skeyHash, out var matchedTorrent), Is.False);
        Assert.That(matchedTorrent, Is.Null);
        Assert.That(registry.Count, Is.EqualTo(0));
    }

    [Test]
    public void Initialize_should_populate_registry_with_all_torrents()
    {
        var registry = new MseSkeyRegistry();
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Name = "Torrent A", InfoHash = InfoHashA },
            new() { Id = 2, Name = "Torrent B", InfoHash = InfoHashB }
        };

        registry.Initialize(torrents);

        Assert.That(registry.Count, Is.EqualTo(2));
        Assert.That(registry.TryMatchTorrent(ComputeExpectedSkeyHash(InfoHashA), out var matchedA), Is.True);
        Assert.That(matchedA.Id, Is.EqualTo(1));
        Assert.That(registry.TryMatchTorrent(ComputeExpectedSkeyHash(InfoHashB), out var matchedB), Is.True);
        Assert.That(matchedB.Id, Is.EqualTo(2));
    }

    [Test]
    public void Initialize_should_clear_previous_torrents()
    {
        var registry = new MseSkeyRegistry();
        registry.RegisterTorrent(new Torrent { Id = 1, Name = "Torrent A", InfoHash = InfoHashA });

        var newTorrents = new List<Torrent>
        {
            new() { Id = 2, Name = "Torrent B", InfoHash = InfoHashB }
        };

        registry.Initialize(newTorrents);

        Assert.That(registry.Count, Is.EqualTo(1));
        Assert.That(registry.TryMatchTorrent(ComputeExpectedSkeyHash(InfoHashA), out _), Is.False);
        Assert.That(registry.TryMatchTorrent(ComputeExpectedSkeyHash(InfoHashB), out _), Is.True);
    }

    [Test]
    public void Handle_TorrentAddedEvent_should_register_torrent()
    {
        var registry = new MseSkeyRegistry();
        var torrent = new Torrent { Id = 1, Name = "Torrent A", InfoHash = InfoHashA };

        registry.Handle(new TorrentAddedEvent(torrent));

        Assert.That(registry.TryMatchTorrent(ComputeExpectedSkeyHash(InfoHashA), out var matched), Is.True);
        Assert.That(matched, Is.SameAs(torrent));
    }

    [Test]
    public void Handle_TorrentUpdatedEvent_should_update_torrent()
    {
        var registry = new MseSkeyRegistry();
        var initialTorrent = new Torrent { Id = 1, Name = "Original Name", InfoHash = InfoHashA };
        registry.RegisterTorrent(initialTorrent);

        var updatedTorrent = new Torrent { Id = 1, Name = "Updated Name", InfoHash = InfoHashA };
        registry.Handle(new TorrentUpdatedEvent(updatedTorrent));

        Assert.That(registry.TryMatchTorrent(ComputeExpectedSkeyHash(InfoHashA), out var matched), Is.True);
        Assert.That(matched.Name, Is.EqualTo("Updated Name"));
    }

    [Test]
    public void Handle_TorrentDeletedEvent_by_info_hash_should_unregister_torrent()
    {
        var registry = new MseSkeyRegistry();
        var torrent = new Torrent { Id = 1, Name = "Torrent A", InfoHash = InfoHashA };
        registry.RegisterTorrent(torrent);

        registry.Handle(new TorrentDeletedEvent(torrent.Id, torrent));

        Assert.That(registry.TryMatchTorrent(ComputeExpectedSkeyHash(InfoHashA), out _), Is.False);
    }

    [Test]
    public void Handle_TorrentDeletedEvent_by_id_only_should_unregister_torrent()
    {
        var registry = new MseSkeyRegistry();
        var torrent = new Torrent { Id = 42, Name = "Torrent A", InfoHash = InfoHashA };
        registry.RegisterTorrent(torrent);

        registry.Handle(new TorrentDeletedEvent(42, null));

        Assert.That(registry.TryMatchTorrent(ComputeExpectedSkeyHash(InfoHashA), out _), Is.False);
    }

    [Test]
    public void Constructor_with_torrent_service_should_initialize_entries()
    {
        var torrentService = Substitute.For<ITorrentService>();
        var torrents = new List<Torrent>
        {
            new() { Id = 1, InfoHash = InfoHashA },
            new() { Id = 2, InfoHash = InfoHashB }
        };
        torrentService.GetAll().Returns(torrents);

        var registry = new MseSkeyRegistry(torrentService);

        Assert.That(registry.Count, Is.EqualTo(2));
        Assert.That(registry.TryMatchTorrent(ComputeExpectedSkeyHash(InfoHashA), out _), Is.True);
        Assert.That(registry.TryMatchTorrent(ComputeExpectedSkeyHash(InfoHashB), out _), Is.True);
    }
}
