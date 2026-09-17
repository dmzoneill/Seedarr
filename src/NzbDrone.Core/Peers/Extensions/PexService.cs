using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers.Extensions;

public class PexService : BackgroundService, IPexService
{
    private readonly IConnectionManager _connectionManager;
    private readonly IPeerExchange _peerExchange;
    private readonly ITorrentService _torrentService;
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public PexService(
        IConnectionManager connectionManager,
        IPeerExchange peerExchange,
        ITorrentService torrentService,
        IConfigService configService = null)
    {
        _connectionManager = connectionManager;
        _peerExchange = peerExchange;
        _torrentService = torrentService;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void BroadcastTick() => BroadcastPex();

    public void BroadcastPexTick() => BroadcastPex();

    public void BroadcastPex()
    {
        if (_configService != null && (!_configService.EnablePex || !_configService.ExtensionUtPex))
        {
            return;
        }

        var swarms = GetActiveSwarms();

        foreach (var swarm in swarms)
        {
            if (swarm.IsPrivate)
            {
                continue;
            }

            ProcessSwarmPex(swarm.Connections);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _configService?.PexInterval ?? 60;
        if (interval <= 0)
        {
            interval = 60;
        }

        _logger.Info("PexService started with {0}s interval.", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);

                if (_configService == null || (_configService.EnablePex && _configService.ExtensionUtPex))
                {
                    BroadcastPex();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in PexService broadcast cycle");
            }
        }
    }

    private List<SwarmInfo> GetActiveSwarms()
    {
        var swarms = new List<SwarmInfo>();
        var infoHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var torrents = _torrentService?.GetAll() ?? new List<Torrent>();
        foreach (var t in torrents)
        {
            if (!string.IsNullOrWhiteSpace(t.InfoHash))
            {
                infoHashes.Add(t.InfoHash);
            }
        }

        var allConnections = _connectionManager?.GetAllConnections() ?? new List<PeerConnection>();
        foreach (var c in allConnections)
        {
            if (!string.IsNullOrWhiteSpace(c.InfoHash))
            {
                infoHashes.Add(c.InfoHash);
            }
        }

        foreach (var infoHash in infoHashes)
        {
            var torrent = torrents.FirstOrDefault(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))
                ?? _torrentService?.GetByInfoHash(infoHash)
                ?? _torrentService?.FindByInfoHash(infoHash);

            var connections = _connectionManager?.GetConnections(infoHash);
            if (connections == null || connections.Count == 0)
            {
                connections = allConnections
                    .Where(c => string.Equals(c.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var isPrivate = torrent?.IsPrivate == true ||
                connections.Any(c => c.MatchedTorrent?.IsPrivate == true);

            swarms.Add(new SwarmInfo(infoHash, isPrivate, connections));
        }

        return swarms;
    }

    private void ProcessSwarmPex(List<PeerConnection> connections)
    {
        if (connections == null || connections.Count == 0)
        {
            return;
        }

        var activeConnections = connections
            .Where(c => c.IsConnected && !string.IsNullOrWhiteSpace(c.RemoteIp) && c.RemotePort > 0)
            .ToList();

        var maxBatch = _configService?.PexMaxPeersPerMessage ?? 50;
        if (maxBatch <= 0)
        {
            maxBatch = 50;
        }

        var pexPeers = activeConnections
            .Where(c => c.RemoteExtensions.TryGetValue("ut_pex", out var extId) && extId > 0)
            .ToList();

        foreach (var recipient in pexPeers)
        {
            var recipientKey = $"{recipient.RemoteIp}:{recipient.RemotePort}";

            var currentSwarmPeers = activeConnections
                .Where(c => c != recipient &&
                            !(string.Equals(c.RemoteIp, recipient.RemoteIp, StringComparison.OrdinalIgnoreCase) &&
                              c.RemotePort == recipient.RemotePort))
                .ToList();

            var currentSwarmEndpoints = new Dictionary<string, PeerConnection>(StringComparer.OrdinalIgnoreCase);
            foreach (var peer in currentSwarmPeers)
            {
                var key = $"{peer.RemoteIp}:{peer.RemotePort}";
                currentSwarmEndpoints.TryAdd(key, peer);
            }

            var addedPeers = currentSwarmEndpoints.Values
                .Where(c => !recipient.PexTracker.PreviouslySentPeers.Contains($"{c.RemoteIp}:{c.RemotePort}"))
                .Take(maxBatch)
                .ToList();

            var droppedKeys = recipient.PexTracker.PreviouslySentPeers
                .Where(endpoint => !currentSwarmEndpoints.ContainsKey(endpoint) &&
                                   !string.Equals(endpoint, recipientKey, StringComparison.OrdinalIgnoreCase))
                .Take(maxBatch)
                .ToList();

            if (addedPeers.Count == 0 && droppedKeys.Count == 0)
            {
                continue;
            }

            var addedInfo = addedPeers.Select(c =>
            {
                byte flags = 0;
                if (c.IsEncrypted)
                {
                    flags |= 0x01;
                }

                if (c.IsSeed)
                {
                    flags |= 0x02;
                }

                return new PeerInfo
                {
                    Ip = c.RemoteIp,
                    Port = c.RemotePort,
                    Flags = flags
                };
            }).ToList();

            var droppedInfo = new List<PeerInfo>();
            foreach (var key in droppedKeys)
            {
                var lastColon = key.LastIndexOf(':');
                if (lastColon > 0 && int.TryParse(key[(lastColon + 1)..], out var port))
                {
                    var ip = key[..lastColon].Trim('[', ']');
                    droppedInfo.Add(new PeerInfo
                    {
                        Ip = ip,
                        Port = port
                    });
                }
            }

            var pexMessage = _peerExchange.BuildPexMessage(addedInfo, droppedInfo, false);
            if (pexMessage != null && pexMessage.Length > 0)
            {
                try
                {
                    recipient.SendExtendedMessage("ut_pex", pexMessage);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to send ut_pex message to {0}:{1}", recipient.RemoteIp, recipient.RemotePort);
                }
            }

            foreach (var peer in addedPeers)
            {
                recipient.PexTracker.PreviouslySentPeers.Add($"{peer.RemoteIp}:{peer.RemotePort}");
            }

            foreach (var key in droppedKeys)
            {
                recipient.PexTracker.PreviouslySentPeers.Remove(key);
            }

            recipient.PexTracker.LastPexSent = DateTime.UtcNow;
        }
    }

    private sealed class SwarmInfo
    {
        public string InfoHash { get; }
        public bool IsPrivate { get; }
        public List<PeerConnection> Connections { get; }

        public SwarmInfo(string infoHash, bool isPrivate, List<PeerConnection> connections)
        {
            InfoHash = infoHash;
            IsPrivate = isPrivate;
            Connections = connections;
        }
    }
}
