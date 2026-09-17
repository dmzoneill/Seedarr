using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;
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
        IUpnpService upnpService = null)
    {
        _eventAggregator = eventAggregator;
        _dynamicAuthManager = dynamicAuthManager;
        _torrentService = torrentService;
        _configService = configService;
        _diskSpaceService = diskSpaceService;
        _upnpService = upnpService;
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
