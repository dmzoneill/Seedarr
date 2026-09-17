using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers.Encryption;

public interface IMseSkeyRegistry
{
    int Count { get; }
    void RegisterTorrent(Torrent torrent);
    void UnregisterTorrent(string infoHash);
    void Initialize(IEnumerable<Torrent> torrents);
    bool TryMatchTorrent(byte[] skeyHash, out Torrent matchedTorrent);
}

public class MseSkeyRegistry : IMseSkeyRegistry,
    IHandle<TorrentAddedEvent>,
    IHandle<TorrentUpdatedEvent>,
    IHandle<TorrentDeletedEvent>
{
    private static readonly byte[] Req2Prefix = Encoding.ASCII.GetBytes("req2");

    private readonly ConcurrentDictionary<string, Torrent> _skeyHashToTorrent = new(StringComparer.OrdinalIgnoreCase);
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;

    public int Count => _skeyHashToTorrent.Count;

    public MseSkeyRegistry()
        : this(null)
    {
    }

    public MseSkeyRegistry(ITorrentService torrentService)
    {
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();

        if (_torrentService != null)
        {
            try
            {
                var torrents = _torrentService.GetAll();
                if (torrents != null)
                {
                    Initialize(torrents);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize MSE SKEY registry from torrent service");
            }
        }
    }

    public void RegisterTorrent(Torrent torrent)
    {
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            return;
        }

        try
        {
            var infoHashBytes = Convert.FromHexString(torrent.InfoHash.Trim());
            var skeyHash = MseKeyDerivation.DeriveKey(infoHashBytes, Req2Prefix);
            _skeyHashToTorrent[Convert.ToHexString(skeyHash)] = torrent;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to register torrent {0} into MSE SKEY registry", torrent.InfoHash);
        }
    }

    public void UnregisterTorrent(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        try
        {
            var infoHashBytes = Convert.FromHexString(infoHash.Trim());
            var skeyHash = MseKeyDerivation.DeriveKey(infoHashBytes, Req2Prefix);
            _skeyHashToTorrent.TryRemove(Convert.ToHexString(skeyHash), out _);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to unregister torrent {0} from MSE SKEY registry", infoHash);
        }
    }

    public void Initialize(IEnumerable<Torrent> torrents)
    {
        _skeyHashToTorrent.Clear();

        if (torrents == null)
        {
            return;
        }

        foreach (var torrent in torrents)
        {
            RegisterTorrent(torrent);
        }
    }

    public bool TryMatchTorrent(byte[] skeyHash, out Torrent matchedTorrent)
    {
        if (skeyHash == null || skeyHash.Length == 0)
        {
            matchedTorrent = null;
            return false;
        }

        if (_skeyHashToTorrent.IsEmpty && _torrentService != null)
        {
            try
            {
                var torrents = _torrentService.GetAll();
                if (torrents != null)
                {
                    foreach (var torrent in torrents)
                    {
                        RegisterTorrent(torrent);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to load torrents from torrent service");
            }
        }

        var key = Convert.ToHexString(skeyHash);
        return _skeyHashToTorrent.TryGetValue(key, out matchedTorrent);
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent != null)
        {
            RegisterTorrent(message.Torrent);
        }
    }

    public void Handle(TorrentUpdatedEvent message)
    {
        if (message?.Torrent != null)
        {
            RegisterTorrent(message.Torrent);
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var infoHash = message.Torrent?.InfoHash;
        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            UnregisterTorrent(infoHash);
        }
        else if (message.TorrentId > 0)
        {
            foreach (var kvp in _skeyHashToTorrent)
            {
                if (kvp.Value?.Id == message.TorrentId)
                {
                    _skeyHashToTorrent.TryRemove(kvp.Key, out _);
                    break;
                }
            }
        }
    }
}
