using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Seedarr.Http.Authentication;

namespace NzbDrone.Host;

public class AppLifetime : IHostedService, IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IDynamicAuthSchemeManager _dynamicAuthManager;
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
    private readonly Logger _logger;
    private readonly HashSet<int> _stalledTorrentIds = new();
    private bool _speedThresholdExceededState;
    private bool _portForwardingFailureEmitted;
    private CancellationTokenSource _cts;
    private Task _watchdogLoopTask;

    public AppLifetime(
        IEventAggregator eventAggregator,
        IDynamicAuthSchemeManager dynamicAuthManager = null,
        ITorrentService torrentService = null,
        IConfigService configService = null,
        IDiskSpaceService diskSpaceService = null,
        IUpnpService upnpService = null,
        IFastResumeService fastResumeService = null,
        ITrackerAnnounceService trackerAnnounceService = null,
        IConnectionManager connectionManager = null,
        IPieceStorage pieceStorage = null,
        IMainDatabase mainDatabase = null,
        IPeerServer peerServer = null)
    {
        _eventAggregator = eventAggregator;
        _dynamicAuthManager = dynamicAuthManager;
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
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_dynamicAuthManager != null)
        {
            try
            {
                await _dynamicAuthManager.InitializeConfiguredProvidersAsync();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error initializing dynamic authentication providers on startup");
            }
        }

        _logger.Info("Seedarr application started");
        _eventAggregator.PublishEvent(new ApplicationStartedEvent());

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

        _cts = new CancellationTokenSource();
        _watchdogLoopTask = Task.Run(() => RunWatchdogLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Seedarr application stopping");
        _eventAggregator.PublishEvent(new ApplicationShutdownRequested());

        if (_cts != null)
        {
            await _cts.CancelAsync();
        }

        if (_watchdogLoopTask != null)
        {
            try
            {
                await Task.WhenAny(_watchdogLoopTask, Task.Delay(2000, cancellationToken));
            }
            catch
            {
            }
        }

        // Phase 1: Halt inbound traffic & announce stopped to trackers
        _logger.Info("Executing phased shutdown phase 1: halting inbound traffic and announcing stopped to trackers");
        try
        {
            _peerServer?.StopListening();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error stopping peer listener on shutdown");
        }

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
                            cancellationToken)).ToArray();

                    var allAnnounces = Task.WhenAll(announceTasks);
                    var timeoutTask = Task.Delay(2500, cancellationToken);
                    try
                    {
                        await Task.WhenAny(allAnnounces, timeoutTask);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error announcing stopped to trackers on shutdown");
            }
        }

        // Phase 2: State & Buffer Serialization
        _logger.Info("Executing phased shutdown phase 2: flushing piece buffers, saving FastResume, and persisting torrent stats");
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

        // Phase 3: Connection Teardown & SQLite Checkpoint
        _logger.Info("Executing phased shutdown phase 3: disconnecting peer connections and executing database checkpoint");
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

        if (_mainDatabase != null)
        {
            try
            {
                _mainDatabase.Checkpoint();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error checkpointing database on shutdown");
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

        // 2. Evaluate Torrents: Stalled state, idle swarms, and speed limits
        if (_torrentService != null)
        {
            try
            {
                var torrents = _torrentService.GetAll() ?? new List<Torrent>();
                var currentTorrentMap = torrents.ToDictionary(t => t.Id);

                // Check previously stalled torrents that are now resolved or removed
                var resolvedIds = new List<int>();
                foreach (var stalledId in _stalledTorrentIds)
                {
                    if (!currentTorrentMap.TryGetValue(stalledId, out var torrent))
                    {
                        resolvedIds.Add(stalledId);
                    }
                    else if (torrent.DownloadSpeed > 0 || torrent.Progress >= 1.0 || torrent.Status != TorrentStatus.Downloading)
                    {
                        resolvedIds.Add(stalledId);
                    }
                }

                foreach (var resolvedId in resolvedIds)
                {
                    _stalledTorrentIds.Remove(resolvedId);
                    var torrent = currentTorrentMap.TryGetValue(resolvedId, out var t) ? t : new Torrent { Id = resolvedId };
                    _eventAggregator.PublishEvent(new TorrentStallResolvedEvent(torrent));
                }

                var activeTorrents = torrents.Where(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding).ToList();
                long totalDownloadSpeed = 0;
                long totalUploadSpeed = 0;

                foreach (var torrent in torrents)
                {
                    totalDownloadSpeed += torrent.DownloadSpeed;
                    totalUploadSpeed += torrent.UploadSpeed;

                    if (torrent.Status == TorrentStatus.Downloading && torrent.DownloadSpeed == 0 && torrent.Progress < 1.0)
                    {
                        var minutesSinceAdd = (DateTime.UtcNow - torrent.DateAdded).TotalMinutes;
                        if (minutesSinceAdd >= 5)
                        {
                            if (_stalledTorrentIds.Add(torrent.Id))
                            {
                                _eventAggregator.PublishEvent(new TorrentStalledEvent(torrent, (int)minutesSinceAdd));
                            }
                        }
                    }
                }

                if (_configService != null)
                {
                    var maxDl = _configService.MaxDownloadSpeedKbps > 0 ? _configService.MaxDownloadSpeedKbps * 1024L : 0;
                    var maxUl = _configService.MaxUploadSpeedKbps > 0 ? _configService.MaxUploadSpeedKbps * 1024L : 0;
                    var isExceeded = (maxDl > 0 && totalDownloadSpeed >= maxDl) || (maxUl > 0 && totalUploadSpeed >= maxUl);

                    if (isExceeded && !_speedThresholdExceededState)
                    {
                        _speedThresholdExceededState = true;
                        _eventAggregator.PublishEvent(new SpeedThresholdExceededEvent(totalDownloadSpeed, totalUploadSpeed, activeTorrents.Count));
                    }
                    else if (!isExceeded && _speedThresholdExceededState)
                    {
                        _speedThresholdExceededState = false;
                        _eventAggregator.PublishEvent(new SpeedThresholdDroppedEvent());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error evaluating torrent state in watchdog");
            }
        }

        // 3. Evaluate Port Forwarding
        if (_upnpService != null && _configService != null && _configService.UpnpEnabled)
        {
            try
            {
                if (!_upnpService.IsAvailable)
                {
                    if (!_portForwardingFailureEmitted)
                    {
                        _portForwardingFailureEmitted = true;
                        _eventAggregator.PublishEvent(new PortForwardingFailedEvent(_configService.ListeningPort, "TCP", "UPnP device unavailable or port mapping failed"));
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
