using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public class LifecycleScriptEventHandler :
    IHandle<TorrentAddedEvent>,
    IHandle<TorrentDownloadCompletedEvent>,
    IHandle<TorrentSeedGoalReachedEvent>
{
    private readonly ICustomScriptService _customScriptService;
    private readonly IConfigService _configService;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public LifecycleScriptEventHandler(
        ICustomScriptService customScriptService,
        IConfigService configService)
    {
        _customScriptService = customScriptService;
        _configService = configService;
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_configService?.ScriptTorrentAddedFilename))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _customScriptService.ExecuteScriptAsync(_configService.ScriptTorrentAddedFilename, message.Torrent, "OnGrab").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing ScriptTorrentAdded script");
                }
            });
        }
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_configService?.OnDownloadCompleteScript))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _customScriptService.ExecuteScriptAsync(_configService.OnDownloadCompleteScript, message.Torrent, "OnDownloadComplete").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing OnDownloadComplete script");
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(_configService?.ScriptTorrentDoneFilename))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _customScriptService.ExecuteScriptAsync(_configService.ScriptTorrentDoneFilename, message.Torrent, "OnDownloadComplete").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing ScriptTorrentDone script");
                }
            });
        }
    }

    public void Handle(TorrentSeedGoalReachedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_configService?.OnSeedGoalReachedScript))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _customScriptService.ExecuteScriptAsync(_configService.OnSeedGoalReachedScript, message.Torrent, "OnSeedGoalReached").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing OnSeedGoalReached script");
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(_configService?.ScriptTorrentDoneSeedingFilename))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _customScriptService.ExecuteScriptAsync(_configService.ScriptTorrentDoneSeedingFilename, message.Torrent, "OnSeedGoalReached").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing ScriptTorrentDoneSeeding script");
                }
            });
        }
    }
}
