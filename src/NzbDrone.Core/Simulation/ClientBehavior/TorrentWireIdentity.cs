using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;

namespace NzbDrone.Core.Simulation.ClientBehavior;

public sealed class TorrentWireIdentity
{
    public IClientProfile Profile { get; init; }
    public string PeerId { get; init; }
    public string AnnounceKey { get; init; }
    public string UserAgent { get; init; }
    public TorrentClientSession Session { get; init; }
}

public static class TorrentWireIdentityResolver
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static TorrentWireIdentity Resolve(
        string infoHash,
        bool isPrivate,
        string torrentClientProfile,
        IConfigService config,
        IClientBehaviorSimulator simulator,
        IClientProfileFactory factory,
        ConcurrentDictionary<string, (string PeerId, string AnnounceKey)> ephemeralIdentities)
    {
        IClientProfile profile = null;
        TorrentClientSession session = null;
        var pinProfile = false;
        var canStoreSession = simulator != null && !string.IsNullOrWhiteSpace(infoHash);
        var anonymous = config != null && config.AnonymousMode;
        var engineEnabled = config != null && config.ClientBehaviorEngineEnabled;

        if (!string.IsNullOrWhiteSpace(torrentClientProfile))
        {
            profile = MatchProfile(torrentClientProfile, factory);
            pinProfile = profile != null;
        }

        if (!pinProfile && profile == null && canStoreSession && engineEnabled && !anonymous)
        {
            session = simulator.GetOrCreateSession(infoHash, isPrivate);
            profile = session?.Profile ?? simulator.GetProfileForTorrent(infoHash, isPrivate);
        }

        if (!pinProfile && profile == null && !string.IsNullOrWhiteSpace(config?.BitTorrentUserAgent))
        {
            profile = MatchUserAgent(config.BitTorrentUserAgent, factory);
            pinProfile = profile != null;
        }

        if (!pinProfile && profile == null && canStoreSession && !anonymous)
        {
            session = simulator.GetOrCreateSession(infoHash, isPrivate);
            profile = session?.Profile ?? simulator.GetProfileForTorrent(infoHash, isPrivate);
        }

        if (profile == null)
        {
            profile = MatchProfile(config?.PrimaryClient, factory) ?? new QBittorrentProfile();
            pinProfile = true;
        }

        if (pinProfile && canStoreSession)
        {
            session = simulator.GetOrCreateSession(infoHash, isPrivate, profile);
            profile = session?.Profile ?? profile;
        }

        string peerId;
        string announceKey;
        if (session != null && !string.IsNullOrEmpty(session.PeerId))
        {
            peerId = session.PeerId;
            announceKey = session.AnnounceKey;
            if (string.IsNullOrEmpty(announceKey))
            {
                announceKey = NewAnnounceKey();
                session.AnnounceKey = announceKey;
            }
        }
        else
        {
            var cache = ephemeralIdentities ?? new ConcurrentDictionary<string, (string PeerId, string AnnounceKey)>(StringComparer.OrdinalIgnoreCase);
            var cacheKey = (infoHash ?? string.Empty) + "\n" + (profile?.Name ?? string.Empty);
            var created = cache.GetOrAdd(cacheKey, _ => (profile.GeneratePeerId(), NewAnnounceKey()));
            peerId = created.PeerId;
            announceKey = created.AnnounceKey;
        }

        var userAgent = session?.Profile?.UserAgent
            ?? profile?.UserAgent
            ?? (!string.IsNullOrWhiteSpace(config?.BitTorrentUserAgent) ? config.BitTorrentUserAgent : "qBittorrent/4.4.2");

        return new TorrentWireIdentity
        {
            Profile = profile,
            PeerId = peerId,
            AnnounceKey = announceKey,
            UserAgent = userAgent,
            Session = session
        };
    }

    public static IClientProfile MatchProfile(string clientName, IClientProfileFactory factory)
    {
        if (string.IsNullOrWhiteSpace(clientName))
        {
            return null;
        }

        if (factory != null)
        {
            try
            {
                var available = factory.GetAvailableProviders();
                if (available != null)
                {
                    var name = clientName.Trim().ToLowerInvariant();
                    var match = available.FirstOrDefault(p =>
                        p.Name.Equals(clientName, StringComparison.OrdinalIgnoreCase) ||
                        p.GetType().Name.StartsWith(clientName, StringComparison.OrdinalIgnoreCase) ||
                        (p.Name != null && p.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
                    if (match != null)
                    {
                        return match;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to get available providers from ClientProfileFactory");
            }
        }

        var token = clientName.Trim().ToLowerInvariant();
        if (token.Contains("transmission"))
        {
            return new TransmissionProfile();
        }

        if (token.Contains("deluge"))
        {
            return new DelugeProfile();
        }

        if (token.Contains("utorrent") || token.Contains("µtorrent"))
        {
            return new UTorrentProfile();
        }

        if (token.Contains("biglybt"))
        {
            return new BiglyBTProfile();
        }

        if (token.Contains("qbittorrent"))
        {
            return new QBittorrentProfile();
        }

        return null;
    }

    public static IClientProfile MatchUserAgent(string userAgent, IClientProfileFactory factory)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var ua = userAgent.ToLowerInvariant();
        if (ua.Contains("transmission"))
        {
            return MatchProfile("Transmission", factory) ?? new TransmissionProfile();
        }

        if (ua.Contains("deluge"))
        {
            return MatchProfile("Deluge", factory) ?? new DelugeProfile();
        }

        if (ua.Contains("utorrent") || ua.Contains("µtorrent"))
        {
            return MatchProfile("uTorrent", factory) ?? new UTorrentProfile();
        }

        if (ua.Contains("biglybt"))
        {
            return MatchProfile("BiglyBT", factory) ?? new BiglyBTProfile();
        }

        if (ua.Contains("qbittorrent"))
        {
            return MatchProfile("qBittorrent", factory) ?? new QBittorrentProfile();
        }

        return null;
    }

    private static string NewAnnounceKey()
    {
        return RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue).ToString("X8", CultureInfo.InvariantCulture);
    }
}
