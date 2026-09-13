using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Peers;

public interface IChokeManager
{
    void ProcessChoking();
    void ProcessRegularUnchoke();
    void ProcessOptimisticUnchoke();
    void PeerConnected(PeerConnection connection);
    void PeerDisconnected(PeerConnection connection);
    void PeerInterestedChanged(PeerConnection connection);
    void UpdatePeerActivity(PeerConnection connection);
    bool CanUnchoke(PeerConnection connection);
}

public class ChokeManager : BackgroundService, IChokeManager
{
    private const int RegularUnchokeIntervalSeconds = 10;
    private const int OptimisticUnchokeIntervalSeconds = 30;
    private const int SnubbingThresholdSeconds = 60;

    private readonly IConnectionManager _connectionManager;
    private readonly IConfigService _configService;
    private readonly IRandomNumberGenerator _random;
    private readonly Logger _logger;
    private readonly object _lock = new();

    private DateTime _lastRegularUnchoke = DateTime.MinValue;
    private DateTime _lastOptimisticUnchoke = DateTime.MinValue;
    private string _currentOptimisticPeerKey;

    public ChokeManager(
        IConnectionManager connectionManager,
        IConfigService configService,
        IRandomNumberGenerator random = null)
    {
        _connectionManager = connectionManager;
        _configService = configService;
        _random = random ?? new RandomNumberGenerator();
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("ChokeManager service started with 10s regular and 30s optimistic unchoke timers.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                if ((now - _lastRegularUnchoke).TotalSeconds >= RegularUnchokeIntervalSeconds)
                {
                    ProcessRegularUnchoke();
                    _lastRegularUnchoke = now;
                }

                if ((now - _lastOptimisticUnchoke).TotalSeconds >= OptimisticUnchokeIntervalSeconds)
                {
                    ProcessOptimisticUnchoke();
                    _lastOptimisticUnchoke = now;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in ChokeManager rotation cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public void ProcessChoking()
    {
        ProcessRegularUnchoke();
        ProcessOptimisticUnchoke();
    }

    public void ProcessRegularUnchoke()
    {
        lock (_lock)
        {
            var connections = _connectionManager.GetAllConnections();
            if (connections.Count == 0)
            {
                return;
            }

            var maxUploadSlots = _configService.MaxUploadSlots;
            if (maxUploadSlots <= 0)
            {
                // 0 means unlimited: unchoke all interested peers
                foreach (var conn in connections)
                {
                    if (conn.PeerInterested && conn.AmChoking)
                    {
                        Unchoke(conn);
                    }
                }

                return;
            }

            var now = DateTime.UtcNow;

            // Anti-snubbing detection: peers unchoked without requests for > 60s
            foreach (var conn in connections)
            {
                if (!conn.AmChoking)
                {
                    var idleTime = (now - conn.LastRequestReceived).TotalSeconds;
                    if (idleTime > SnubbingThresholdSeconds)
                    {
                        if (!conn.IsSnubbed)
                        {
                            _logger.Debug("Peer {0}:{1} marked as snubbed (idle for {2:F0}s)", conn.RemoteIp, conn.RemotePort, idleTime);
                            conn.IsSnubbed = true;
                        }
                    }
                }
            }

            // Reserve 1 slot for optimistic unchoke if maxUploadSlots > 1
            var regularSlotCount = maxUploadSlots > 1 ? maxUploadSlots - 1 : maxUploadSlots;

            // Group connections by infoHash to balance slots across swarms
            var byTorrent = connections
                .Where(c => !string.IsNullOrEmpty(c.InfoHash))
                .GroupBy(c => c.InfoHash, StringComparer.OrdinalIgnoreCase);

            var candidatePeers = connections
                .Where(c => c.PeerInterested && !c.IsSnubbed)
                .OrderByDescending(c => c.UploadRate + c.DownloadRate)
                .ThenByDescending(c => c.BytesUploaded)
                .ThenBy(c => c.ConnectedAt)
                .ToList();

            var selectedRegular = candidatePeers.Take(regularSlotCount).ToHashSet();

            foreach (var conn in connections)
            {
                var isOptimistic = conn.IsOptimisticUnchoked;
                if (selectedRegular.Contains(conn))
                {
                    conn.IsOptimisticUnchoked = false;
                    if (conn.AmChoking)
                    {
                        Unchoke(conn);
                    }
                }
                else if (!isOptimistic)
                {
                    if (!conn.AmChoking)
                    {
                        Choke(conn);
                    }
                }
            }
        }
    }

    public void ProcessOptimisticUnchoke()
    {
        lock (_lock)
        {
            var connections = _connectionManager.GetAllConnections();
            if (connections.Count == 0)
            {
                return;
            }

            var maxUploadSlots = _configService.MaxUploadSlots;
            if (maxUploadSlots <= 1)
            {
                return;
            }

            // Find all interested peers that are currently choked
            var chokedInterested = connections
                .Where(c => c.PeerInterested && c.AmChoking)
                .ToList();

            // Clear previous optimistic peer flag
            foreach (var conn in connections)
            {
                if (conn.IsOptimisticUnchoked)
                {
                    conn.IsOptimisticUnchoked = false;

                    // If not regular unchoked, choke it
                    Choke(conn);
                }
            }

            if (chokedInterested.Count == 0)
            {
                _currentOptimisticPeerKey = null;
                return;
            }

            // Pick a random choked interested peer for optimistic unchoke
            var chosenIndex = _random.Next(0, chokedInterested.Count);
            var chosen = chokedInterested[chosenIndex];

            chosen.IsOptimisticUnchoked = true;
            _currentOptimisticPeerKey = $"{chosen.RemoteIp}:{chosen.RemotePort}";
            _logger.Debug("Optimistically unchoking peer {0}:{1}", chosen.RemoteIp, chosen.RemotePort);
            Unchoke(chosen);
        }
    }

    public void PeerConnected(PeerConnection connection)
    {
        // Initial state is choking
        connection.AmChoking = true;
    }

    public void PeerDisconnected(PeerConnection connection)
    {
        lock (_lock)
        {
            if (connection.IsOptimisticUnchoked)
            {
                _currentOptimisticPeerKey = null;
            }
        }
    }

    public void PeerInterestedChanged(PeerConnection connection)
    {
        if (connection.PeerInterested)
        {
            if (CanUnchoke(connection))
            {
                Unchoke(connection);
            }
        }
        else
        {
            if (!connection.AmChoking)
            {
                Choke(connection);
            }
        }
    }

    public void UpdatePeerActivity(PeerConnection connection)
    {
        connection.LastRequestReceived = DateTime.UtcNow;
        if (connection.IsSnubbed)
        {
            _logger.Debug("Peer {0}:{1} unsnubbed due to active request", connection.RemoteIp, connection.RemotePort);
            connection.IsSnubbed = false;
        }
    }

    public bool CanUnchoke(PeerConnection connection)
    {
        var maxUploadSlots = _configService.MaxUploadSlots;
        if (maxUploadSlots <= 0)
        {
            return true;
        }

        var unchokedCount = _connectionManager.GetAllConnections().Count(c => !c.AmChoking);
        return unchokedCount < maxUploadSlots;
    }

    private void Unchoke(PeerConnection connection)
    {
        if (connection.AmChoking)
        {
            connection.AmChoking = false;
            try
            {
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                _logger.Trace("Sent UNCHOKE to peer {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send unchoke to {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
        }
    }

    private void Choke(PeerConnection connection)
    {
        if (!connection.AmChoking)
        {
            connection.AmChoking = true;
            try
            {
                connection.SendMessage(new PeerMessage { Type = PeerMessageType.Choke });
                _logger.Trace("Sent CHOKE to peer {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send choke to {0}:{1}", connection.RemoteIp, connection.RemotePort);
            }
        }
    }
}
