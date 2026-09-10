using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public class NotificationEventHandler :
    IHandle<TorrentAddedEvent>,
    IHandle<TorrentDownloadCompletedEvent>,
    IHandle<TorrentDeletedEvent>,
    IHandle<TorrentStatusChangedEvent>,
    IHandle<MediaEnrichedEvent>,
    IHandle<ArchiveExtractionCompletedEvent>,
    IHandle<ArchiveExtractionFailedEvent>,
    IHandle<VpnKillSwitchTriggeredEvent>,
    IHandle<ApplicationUpdatedEvent>,
    IHandle<HealthIssueEvent>,
    IHandle<TorrentSeedGoalReachedEvent>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IWebhookDispatcher _webhookDispatcher;
    private readonly ICustomScriptService _customScriptService;
    private readonly IConfigService _configService;
    private readonly IMediaEnrichmentService _mediaEnrichmentService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITorrentFileRepository _torrentFileRepository;
    private readonly IEpisodicParser _episodicParser;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public NotificationEventHandler(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService customScriptService,
        IConfigService configService = null,
        IMediaEnrichmentService mediaEnrichmentService = null,
        ITorrentRepository torrentRepository = null,
        ITorrentFileRepository torrentFileRepository = null,
        IEpisodicParser episodicParser = null)
    {
        _notificationRepository = notificationRepository;
        _webhookDispatcher = webhookDispatcher;
        _customScriptService = customScriptService;
        _configService = configService;
        _mediaEnrichmentService = mediaEnrichmentService;
        _torrentRepository = torrentRepository;
        _torrentFileRepository = torrentFileRepository;
        _episodicParser = episodicParser ?? new EpisodicParser();
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnGrab, "OnGrab", message.Torrent);

        var scriptTorrentAdded = _configService?.GetValue("ScriptTorrentAddedFilename", string.Empty);
        if (!string.IsNullOrWhiteSpace(scriptTorrentAdded))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _customScriptService.ExecuteScriptAsync(scriptTorrentAdded, message.Torrent, "OnGrab").ConfigureAwait(false);
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

        Dispatch(n => n.OnDownloadComplete, "OnDownloadComplete", message.Torrent);

        var onDownloadCompleteScript = _configService?.GetValue("OnDownloadCompleteScript", string.Empty);
        if (!string.IsNullOrWhiteSpace(onDownloadCompleteScript))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _customScriptService.ExecuteScriptAsync(onDownloadCompleteScript, message.Torrent, "OnDownloadComplete").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing OnDownloadComplete script");
                }
            });
        }

        var scriptTorrentDone = _configService?.GetValue("ScriptTorrentDoneFilename", string.Empty);
        if (!string.IsNullOrWhiteSpace(scriptTorrentDone))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _customScriptService.ExecuteScriptAsync(scriptTorrentDone, message.Torrent, "OnDownloadComplete").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing ScriptTorrentDone script");
                }
            });
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnTorrentDeleted, "OnTorrentDeleted", message.Torrent);
    }

    public void Handle(MediaEnrichedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var torrent = _torrentRepository?.Get(message.TorrentId);
        if (torrent != null)
        {
            Dispatch(n => n.OnMediaInspected, "OnMediaInspected", torrent);
        }
    }

    public void Handle(ArchiveExtractionCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnExtractComplete, "OnExtractComplete", message.Torrent);
    }

    public void Handle(ArchiveExtractionFailedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnHealthIssue, "OnHealthIssue", message.Torrent);
        Dispatch(n => n.OnManualInteractionRequired, "OnManualInteractionRequired", message.Torrent);
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (message.NewStatus == TorrentStatus.Error)
        {
            Dispatch(n => n.OnHealthIssue, "OnHealthIssue", message.Torrent);
            Dispatch(n => n.OnManualInteractionRequired, "OnManualInteractionRequired", message.Torrent);
        }
        else if (message.OldStatus == TorrentStatus.Error && message.NewStatus != TorrentStatus.Error)
        {
            Dispatch(n => n.OnHealthRestored, "OnHealthRestored", message.Torrent);
        }
    }

    public void Handle(TorrentSeedGoalReachedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnSeedGoalReached, "OnSeedGoalReached", message.Torrent);

        var onSeedGoalReachedScript = _configService?.GetValue("OnSeedGoalReachedScript", string.Empty);
        if (!string.IsNullOrWhiteSpace(onSeedGoalReachedScript))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _customScriptService.ExecuteScriptAsync(onSeedGoalReachedScript, message.Torrent, "OnSeedGoalReached").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing OnSeedGoalReached script");
                }
            });
        }

        var scriptTorrentDoneSeeding = _configService?.GetValue("ScriptTorrentDoneSeedingFilename", string.Empty);
        if (!string.IsNullOrWhiteSpace(scriptTorrentDoneSeeding))
        {
            Task.Run(async () =>
            {
                try
                {
                    await _customScriptService.ExecuteScriptAsync(scriptTorrentDoneSeeding, message.Torrent, "OnSeedGoalReached").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing ScriptTorrentDoneSeeding script");
                }
            });
        }
    }

    public void Handle(HealthIssueEvent message)
    {
        if (message == null)
        {
            return;
        }

        var torrent = message.Torrent ?? (message.TorrentId > 0 ? _torrentRepository?.Get(message.TorrentId) : null);
        if (torrent != null)
        {
            if (message.IsResolved)
            {
                Dispatch(n => n.OnHealthRestored, "OnHealthRestored", torrent);
            }
            else
            {
                Dispatch(n => n.OnHealthIssue, "OnHealthIssue", torrent);
            }

            return;
        }

        var eventType = message.IsResolved ? "OnHealthRestored" : "OnHealthIssue";
        var payload = new
        {
            EventType = eventType,
            Source = message.Source,
            Message = message.Message,
            IsResolved = message.IsResolved,
            Timestamp = DateTime.UtcNow,
        };

        DispatchGeneric(n => message.IsResolved ? n.OnHealthRestored : n.OnHealthIssue, eventType, payload);
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        var payload = new
        {
            EventType = "OnHealthIssue",
            Message = "VPN Kill Switch triggered: VPN interface disconnected. BitTorrent traffic halted.",
            Timestamp = DateTime.UtcNow,
        };

        DispatchGeneric(n => n.OnHealthIssue, "OnHealthIssue", payload);
    }

    public void Handle(ApplicationUpdatedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var payload = new
        {
            EventType = "OnApplicationUpdate",
            PreviousVersion = message.PreviousVersion ?? string.Empty,
            NewVersion = message.NewVersion ?? string.Empty,
            Message = $"Seedarr updated to version {message.NewVersion}",
            Timestamp = DateTime.UtcNow,
        };

        DispatchGeneric(n => n.OnApplicationUpdate, "OnApplicationUpdate", payload);
    }

    public static string ResolveTargetUrl(string implementation, string settings)
    {
        return NotificationPayloadBuilder.ResolveTargetUrl(implementation, settings);
    }

    public static string ResolveCustomHeaders(string settings)
    {
        return NotificationPayloadBuilder.ResolveCustomHeaders(null, settings);
    }

    public static string ResolveCustomHeaders(string implementation, string settings)
    {
        return NotificationPayloadBuilder.ResolveCustomHeaders(implementation, settings);
    }

    public static string EscapeMarkdown(string text)
    {
        return EpisodicParser.EscapeMarkdownStatic(text);
    }

    public static void SendEmailNotification(
        string settings,
        string eventType,
        Torrent torrent,
        dynamic meta,
        object genericPayload,
        Action<System.Net.Mail.SmtpClient, System.Net.Mail.MailMessage> smtpSender = null)
    {
        EmailNotificationSender.SendEmailNotification(settings, eventType, torrent, (object)meta, genericPayload, smtpSender);
    }

    internal static object BuildProviderPayload(string implementation, string eventType, Torrent torrent, dynamic meta, object genericPayload, string settings = null)
    {
        return NotificationPayloadBuilder.BuildProviderPayload(implementation, eventType, torrent, (object)meta, genericPayload, settings);
    }

    private void DispatchGeneric(Func<NotificationDefinition, bool> predicate, string eventType, object payload)
    {
        var activeNotifications = _notificationRepository.GetEnabled().Where(predicate).ToList();
        if (activeNotifications.Count == 0)
        {
            return;
        }

        foreach (var notif in activeNotifications)
        {
            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await _customScriptService.ExecuteScriptAsync(scriptPath, null, eventType, scriptArgs).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error executing custom script for {0}", eventType);
                    }
                });
            }
            else if (string.Equals(notif.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() =>
                {
                    try
                    {
                        EmailNotificationSender.SendEmailNotification(notif.Settings, eventType, null, null, payload);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error sending email notification for {0}", eventType);
                    }
                });
            }
            else
            {
                var providerPayload = NotificationPayloadBuilder.BuildProviderPayload(notif.Implementation, eventType, null, null, payload, notif.Settings);
                var targetUrl = NotificationPayloadBuilder.ResolveTargetUrl(notif.Implementation, notif.Settings);
                var customHeaders = NotificationPayloadBuilder.ResolveCustomHeaders(notif.Implementation, notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await _webhookDispatcher.DispatchAsync(targetUrl, providerPayload, customHeaders).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error dispatching webhook notification for {0}", eventType);
                    }
                });
            }
        }
    }

    private void Dispatch(Func<NotificationDefinition, bool> predicate, string eventType, Torrent torrent)
    {
        var activeNotifications = _notificationRepository.GetEnabled().Where(predicate).ToList();
        if (activeNotifications.Count == 0)
        {
            return;
        }

        var meta = _mediaEnrichmentService?.GetMetadata(torrent.Id);
        var files = _torrentFileRepository?.GetByTorrentId(torrent.Id)?.Select(f => new
        {
            path = f.Path,
            size = f.Size,
            progress = 1.0,
        }).ToList();

        var (seasonNum, epNum, epTitle) = _episodicParser.ExtractEpisodicInfo(torrent.Name);
        var (container, resolution, videoCodec, hdrFormat, audioCodec, audioChannels, audioLanguage, subtitleLanguages) = _episodicParser.ExtractStreamSpecs(meta?.MediaInfoJson);

        var downloadTimeSeconds = 0L;

        var payload = new
        {
            eventType,
            instanceName = "Seedarr",
            applicationVersion = "1.0.0",
            timestamp = DateTime.UtcNow.ToString("o"),
            torrent = new
            {
                id = torrent.Id,
                name = torrent.Name,
                infoHash = torrent.InfoHash,
                category = torrent.Label,
                state = torrent.Status.ToString(),
                status = torrent.Status.ToString(),
                progress = double.IsFinite(torrent.Progress) ? torrent.Progress : 0.0,
                totalSize = torrent.TotalSize,
                downloaded = torrent.Downloaded,
                uploaded = torrent.Uploaded,
                downloadPath = torrent.SourcePath,
                savePath = torrent.SourcePath,
                downloadSpeed = torrent.DownloadSpeed,
                uploadSpeed = torrent.UploadSpeed,
                eta = torrent.Eta,
                etaString = _episodicParser.FormatEta(torrent.Eta),
                seeders = torrent.Seeders,
                leechers = torrent.Leechers,
                ratio = double.IsFinite(torrent.Ratio) ? torrent.Ratio : 0.0,
                dateAdded = torrent.DateAdded != default ? torrent.DateAdded.ToString("o") : null,
                dateCompleted = (string)null,
                downloadTimeSeconds,
                seedingTimeSeconds = torrent.SeedingTime,
                tags = torrent.TagIds ?? new List<int>(),
            },
            media = new
            {
                arrType = meta?.ArrType,
                arrMediaId = meta?.ArrMediaId ?? 0,
                title = meta?.Title ?? torrent.Name,
                year = meta?.Year ?? 0,
                seasonNumber = seasonNum,
                episodeNumber = epNum,
                episodeTitle = epTitle,
                overview = meta?.Overview,
                posterUrl = meta?.PosterUrl,
                fanartUrl = meta?.BackdropUrl,
                backdropUrl = meta?.BackdropUrl,
                rating = (meta?.Rating != null && double.IsFinite((double)meta.Rating)) ? (double)meta.Rating : 0.0,
                imdbId = meta?.ImdbId,
                tmdbId = meta?.TmdbId,
                tvdbId = meta?.TvdbId,
            },
            streamSpecs = new
            {
                container,
                containerFormat = container,
                resolution,
                videoCodec,
                hdrFormat,
                audioCodec,
                audioChannels,
                audioLanguage,
                subtitleLanguages,
            },
            files,
        };

        foreach (var notif in activeNotifications)
        {
            if (notif.Tags != null && notif.Tags.Count > 0)
            {
                if (torrent.TagIds == null || !notif.Tags.Any(t => torrent.TagIds.Contains(t)))
                {
                    continue;
                }
            }

            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await _customScriptService.ExecuteScriptAsync(scriptPath, torrent, eventType, scriptArgs).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error executing custom script for {0}", eventType);
                    }
                });
            }
            else if (string.Equals(notif.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
            {
                Task.Run(() =>
                {
                    try
                    {
                        EmailNotificationSender.SendEmailNotification(notif.Settings, eventType, torrent, meta, payload);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error sending email notification for {0}", eventType);
                    }
                });
            }
            else
            {
                var providerPayload = NotificationPayloadBuilder.BuildProviderPayload(notif.Implementation, eventType, torrent, meta, payload, notif.Settings);
                var targetUrl = NotificationPayloadBuilder.ResolveTargetUrl(notif.Implementation, notif.Settings);
                var customHeaders = NotificationPayloadBuilder.ResolveCustomHeaders(notif.Implementation, notif.Settings);
                Task.Run(async () =>
                {
                    try
                    {
                        await _webhookDispatcher.DispatchAsync(targetUrl, providerPayload, customHeaders).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error dispatching webhook notification for {0}", eventType);
                    }
                });
            }
        }
    }
}
