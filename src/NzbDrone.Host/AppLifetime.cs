using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
namespace NzbDrone.Host;

public class AppLifetime : IHostedService, IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentService _torrentService;
    private readonly IConfigService _configService;
    private readonly IDiskSpaceService _diskSpaceService;
    private readonly IUpnpService _upnpService;
    private readonly IFastResumeService _fastResumeService;
    private readonly ITrackerAnnounceService _trackerAnnounceService;
    private readonly IConnectionManager _connectionManager;
    private readonly IPieceStorage _pieceStorage;
    private readonly IMainDatabase _mainDatabase;
    private readonly IPeerServer _peerServer;
    private readonly IDatabaseMaintenanceService _databaseMaintenanceService;
    private readonly Logger _logger;
    private bool _portForwardingFailureEmitted;
    private volatile bool _shuttingDown;
    private CancellationTokenSource _cts;
    private Task _watchdogLoopTask;

    public AppLifetime(
        IEventAggregator eventAggregator,
        ITorrentService torrentService = null,
        IConfigService configService = null,
        IDiskSpaceService diskSpaceService = null,
        IUpnpService upnpService = null,
        IFastResumeService fastResumeService = null,
        ITrackerAnnounceService trackerAnnounceService = null,
        IConnectionManager connectionManager = null,
        IPieceStorage pieceStorage = null,
        IMainDatabase mainDatabase = null,
        IPeerServer peerServer = null,
        IDatabaseMaintenanceService databaseMaintenanceService = null)
    {
        _eventAggregator = eventAggregator;
        _torrentService = torrentService;
        _configService = configService;
        _diskSpaceService = diskSpaceService;
        _upnpService = upnpService;
        _fastResumeService = fastResumeService;
        _trackerAnnounceService = trackerAnnounceService;
        _connectionManager = connectionManager;
        _pieceStorage = pieceStorage;
        _mainDatabase = mainDatabase;
        _peerServer = peerServer;
        _databaseMaintenanceService = databaseMaintenanceService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.Info("Seedarr application started");

        if (_fastResumeService != null)
        {
            try
            {
                _fastResumeService.LoadAll();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error loading/reconciling FastResume data on startup");
            }
        }

        _eventAggregator.PublishEvent(new ApplicationStartedEvent());

        cancellationToken.ThrowIfCancellationRequested();

        _cts = new CancellationTokenSource();
        _watchdogLoopTask = Task.Run(() => RunWatchdogLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Seedarr application stopping");
        _shuttingDown = true;

        try
        {
            _peerServer?.StopListening();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error stopping peer listener on shutdown");
        }

        _eventAggregator.PublishEvent(new ApplicationShutdownRequested());

        if (_cts != null)
        {
            await _cts.CancelAsync();
        }

        if (_watchdogLoopTask != null)
        {
            try
            {
                // Use CancellationToken.None so a pre-cancelled host stop token does not skip watchdog drain (#944).
                var watchdogJoin = Task.WhenAny(_watchdogLoopTask, Task.Delay(2000, CancellationToken.None));
                var completed = await watchdogJoin;
                if (completed != _watchdogLoopTask && !_watchdogLoopTask.IsCompleted)
                {
                    _logger.Debug(
                        "Watchdog loop exceeded {0}ms bounded wait; awaiting completion before later shutdown phases",
                        2000);
                    await _watchdogLoopTask;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Watchdog loop task cancellation error during shutdown");
            }
        }

        // Phase 1: Announce stopped to trackers (inbound listener already halted)
        _logger.Info("Executing phased shutdown phase 1: announcing stopped to trackers");

        if (_trackerAnnounceService != null && _torrentService != null)
        {
            try
            {
                var torrents = _torrentService.GetAll() ?? new List<Torrent>();
                var activeTorrents = torrents.Where(t =>
                    t.Status == TorrentStatus.Downloading ||
                    t.Status == TorrentStatus.Seeding ||
                    t.Active).ToList();

                if (activeTorrents.Count > 0)
                {
                    // Use CancellationToken.None so a pre-cancelled host stop token does not skip stopped announces (#953).
                    var announceTasks = activeTorrents.Select(torrent =>
                        Task.Run(
                            () =>
                            {
                                try
                                {
                                    _trackerAnnounceService.AnnounceTorrent(torrent, force: true, eventType: AnnounceEvent.Stopped);
                                }
                                catch (Exception ex)
                                {
                                    _logger.Debug(ex, "Failed to send stopped tracker announce for torrent {0}", torrent.Id);
                                }
                            },
                            CancellationToken.None)).ToArray();

                    var allAnnounces = Task.WhenAll(announceTasks);
                    var timeoutTask = Task.Delay(2500, CancellationToken.None);
                    try
                    {
                        var completed = await Task.WhenAny(allAnnounces, timeoutTask);
                        if (completed != allAnnounces && !allAnnounces.IsCompleted)
                        {
                            _logger.Debug(
                                "Tracker stopped announces exceeded {0}ms bounded wait; awaiting remaining tasks before later shutdown phases",
                                2500);
                            await allAnnounces;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Tracker stopped announces awaiting timed out or failed during shutdown");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error announcing stopped to trackers on shutdown");
            }
        }

        // Phase 2: State & Buffer Serialization
        _logger.Info("Executing phased shutdown phase 2: flushing piece buffers and saving FastResume");
        if (_pieceStorage != null)
        {
            try
            {
                _pieceStorage.Flush();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error flushing piece storage buffers on shutdown");
            }
        }

        if (_fastResumeService != null)
        {
            try
            {
                _fastResumeService.SaveAll();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error saving FastResume data on shutdown");
            }
        }

        // Phase 3: Connection teardown, torrent stat persistence, and SQLite checkpoint
        _logger.Info("Executing phased shutdown phase 3: disconnecting peer connections, persisting torrent stats, and executing database checkpoint");
        if (_connectionManager != null)
        {
            try
            {
                await _connectionManager.DisconnectAllAsync();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error disconnecting peer connections on shutdown");
            }
        }

        if (_torrentService != null)
        {
            try
            {
                var torrents = _torrentService.GetAll();
                if (torrents != null && torrents.Count > 0)
                {
                    _torrentService.UpdateMany(torrents);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error persisting torrent statistics to database on shutdown");
            }
        }

        if (_mainDatabase != null)
        {
            try
            {
                _mainDatabase.Optimize();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error optimizing database on shutdown");
            }

            try
            {
                SqliteConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error clearing SQLite connection pool prior to checkpoint");
            }

            try
            {
                var checkpointResult = _mainDatabase.Checkpoint(WalCheckpointMode.Truncate);
                if (checkpointResult != null && !checkpointResult.Success)
                {
                    _logger.Warn(
                        "Shutdown WAL checkpoint did not fully complete (Busy: {0}, Log: {1}, Checkpointed: {2})",
                        checkpointResult.Busy,
                        checkpointResult.WalLogPages,
                        checkpointResult.WalCheckpointedPages);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error checkpointing database on shutdown");
            }

            try
            {
                SqliteConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error clearing SQLite connection pool on shutdown");
            }
        }
        else if (_databaseMaintenanceService != null)
        {
            try
            {
                SqliteConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error clearing SQLite connection pool prior to checkpoint");
            }

            try
            {
                _databaseMaintenanceService.CheckpointWal(WalCheckpointMode.Truncate);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error checkpointing database via maintenance service on shutdown");
            }

            try
            {
                SqliteConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error clearing SQLite connection pool on shutdown");
            }
        }
        else
        {
            try
            {
                SqliteConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error clearing SQLite connection pool on shutdown");
            }
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }

    private async Task RunWatchdogLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                EvaluateWatchdogMetrics();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error in automation watchdog background check");
            }
        }
    }

    internal void EvaluateWatchdogMetrics()
    {
        if (_shuttingDown)
        {
            return;
        }

        // 1. Evaluate Disk Space
        if (_diskSpaceService != null)
        {
            try
            {
                // Rely on DiskSpaceService stateful tracking and edge-triggered event transitions
                _diskSpaceService.CheckDiskSpaceThresholds();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error evaluating disk space in watchdog");
            }
        }

        // Torrent stall and speed-limit automation events are published only by SeedingEngine.

        // 2. Evaluate Port Forwarding
        if (_upnpService != null && _configService != null && _configService.UpnpEnabled)
        {
            try
            {
                var peerPort = _configService.ListeningPort;
                var mappings = _upnpService.GetMappings() ?? new List<PortMapping>();
                var peerMapping = mappings.FirstOrDefault(m => m.InternalPort == peerPort && string.Equals(m.Protocol, "TCP", StringComparison.OrdinalIgnoreCase));
                var portForwardFailed = !_upnpService.IsAvailable || peerMapping == null || !peerMapping.IsActive;

                if (portForwardFailed)
                {
                    if (!_portForwardingFailureEmitted)
                    {
                        _portForwardingFailureEmitted = true;
                        var detail = !string.IsNullOrEmpty(peerMapping?.ErrorMessage)
                            ? peerMapping.ErrorMessage
                            : !_upnpService.IsAvailable
                                ? "UPnP device unavailable or port mapping failed"
                                : "No compatible UPnP or NAT-PMP gateway discovered or port redirection was rejected.";
                        _eventAggregator.PublishEvent(new PortForwardingFailedEvent(peerPort, "TCP", detail));
                    }
                }
                else
                {
                    _portForwardingFailureEmitted = false;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error evaluating port forwarding in watchdog");
            }
        }
    }
}
