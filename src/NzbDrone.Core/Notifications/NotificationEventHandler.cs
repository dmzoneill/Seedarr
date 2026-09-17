using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Vpn;
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
    IHandle<TorrentSeedGoalReachedEvent>,
    IHandle<BackupCreatedEvent>,
    IHandle<BackupFailedEvent>,
    IDisposable
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IWebhookDispatcher _webhookDispatcher;
    private readonly ICustomScriptService _customScriptService;
    private readonly IConfigService _configService;
    private readonly IMediaEnrichmentService _mediaEnrichmentService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITorrentFileRepository _torrentFileRepository;
    private readonly IEpisodicParser _episodicParser;
    private readonly SemaphoreSlim _dispatchSemaphore;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public SemaphoreSlim DispatchSemaphore => _dispatchSemaphore;

    public NotificationEventHandler(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService customScriptService,
        IConfigService configService = null,
        IMediaEnrichmentService mediaEnrichmentService = null,
        ITorrentRepository torrentRepository = null,
        ITorrentFileRepository torrentFileRepository = null,
        IEpisodicParser episodicParser = null,
        int maxConcurrentDispatches = 10)
    {
        _notificationRepository = notificationRepository;
        _webhookDispatcher = webhookDispatcher;
        _customScriptService = customScriptService;
        _configService = configService;
        _mediaEnrichmentService = mediaEnrichmentService;
        _torrentRepository = torrentRepository;
        _torrentFileRepository = torrentFileRepository;
        _episodicParser = episodicParser ?? new EpisodicParser();
        _dispatchSemaphore = new SemaphoreSlim(maxConcurrentDispatches, maxConcurrentDispatches);
    }

    public void Handle(TorrentAddedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnGrab, "OnGrab", message.Torrent);
    }

    public void Handle(TorrentDownloadCompletedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        Dispatch(n => n.OnDownloadComplete, "OnDownloadComplete", message.Torrent);
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

        Dispatch(n => n.OnHealthIssue || n.OnManualInteractionRequired, "OnHealthIssue", message.Torrent, message.ErrorMessage);
    }

    public void Handle(TorrentStatusChangedEvent message)
    {
        if (message?.Torrent == null)
        {
            return;
        }

        if (message.NewStatus == TorrentStatus.Error)
        {
            Dispatch(n => n.OnHealthIssue || n.OnManualInteractionRequired, "OnHealthIssue", message.Torrent);
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
                Dispatch(n => n.OnHealthIssue, "OnHealthIssue", torrent, message.Message);
            }

            return;
        }

        var eventType = message.IsResolved ? "OnHealthRestored" : "OnHealthIssue";
        var payload = new
        {
            EventType = eventType,
            Source = message.Source,
            Message = message.Message,
            ErrorMessage = message.Message,
            errorMessage = message.Message,
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

    public void Handle(BackupCreatedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var payload = new
        {
            EventType = "OnBackupComplete",
            FileName = message.FileName ?? string.Empty,
            Path = message.Path ?? string.Empty,
            BackupType = message.Type.ToString(),
            Size = message.Size,
            Message = $"Seedarr backup created: {message.FileName}",
            Timestamp = DateTime.UtcNow,
        };

        DispatchGeneric(n => n.OnBackupComplete, "OnBackupComplete", payload);
    }

    public void Handle(BackupFailedEvent message)
    {
        if (message == null)
        {
            return;
        }

        var errorMsg = message.ErrorMessage ?? message.Exception?.Message ?? "Backup failed";
        var payload = new
        {
            EventType = "OnBackupFailed",
            BackupType = message.Type.ToString(),
            Message = $"Seedarr backup failed: {errorMsg}",
            ErrorMessage = errorMsg,
            errorMessage = errorMsg,
            Timestamp = DateTime.UtcNow,
        };

        DispatchGeneric(n => n.OnBackupFailed || n.OnHealthIssue, "OnBackupFailed", payload);
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

    public void Dispose()
    {
        _dispatchSemaphore?.Dispose();
    }

    internal Task EnqueueDispatch(Func<Task> action, string eventType, string implementation)
    {
        return Task.Run(async () =>
        {
            await _dispatchSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error dispatching {0} notification for {1}", implementation, eventType);
            }
            finally
            {
                _dispatchSemaphore.Release();
            }
        });
    }

    private void DispatchGeneric(Func<NotificationDefinition, bool> predicate, string eventType, object payload, IEnumerable<int> eventTags = null)
    {
        var activeNotifications = _notificationRepository.GetEnabled().Where(predicate).ToList();
        if (activeNotifications.Count == 0)
        {
            return;
        }

        if (eventTags == null && payload != null)
        {
            var prop = payload.GetType().GetProperty("Tags", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? payload.GetType().GetProperty("TagIds", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null && prop.GetValue(payload) is IEnumerable<int> extracted)
            {
                eventTags = extracted;
            }
        }

        foreach (var notif in activeNotifications)
        {
            if (notif.Tags != null && notif.Tags.Count > 0)
            {
                if (eventTags == null || !notif.Tags.Any(t => eventTags.Contains(t)))
                {
                    continue;
                }
            }

            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
                EnqueueDispatch(
                    async () => await _customScriptService.ExecuteScriptAsync(scriptPath, null, eventType, scriptArgs).ConfigureAwait(false),
                    eventType,
                    "CustomScript");
            }
            else if (string.Equals(notif.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
            {
                EnqueueDispatch(
                    async () => await EmailNotificationSender.SendEmailNotificationAsync(notif.Settings, eventType, null, null, payload).ConfigureAwait(false),
                    eventType,
                    "Email");
            }
            else
            {
                var providerPayload = NotificationPayloadBuilder.BuildProviderPayload(notif.Implementation, eventType, null, null, payload, notif.Settings);
                var targetUrl = NotificationPayloadBuilder.ResolveTargetUrl(notif.Implementation, notif.Settings);
                var customHeaders = NotificationPayloadBuilder.ResolveCustomHeaders(notif.Implementation, notif.Settings);
                EnqueueDispatch(
                    async () => await _webhookDispatcher.DispatchAsync(targetUrl, providerPayload, customHeaders).ConfigureAwait(false),
                    eventType,
                    notif.Implementation ?? "Webhook");
            }
        }
    }

    private void Dispatch(Func<NotificationDefinition, bool> predicate, string eventType, Torrent torrent, string errorMessage = null)
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
            errorMessage,
            message = errorMessage,
            torrent = new
            {
                id = torrent.Id,
                name = torrent.Name,
                infoHash = torrent.InfoHash,
                category = torrent.Category ?? torrent.Label,
                state = torrent.Status.ToString(),
                status = torrent.Status.ToString(),
                progress = double.IsFinite(torrent.Progress) ? torrent.Progress : 0.0,
                totalSize = torrent.TotalSize,
                downloaded = torrent.Downloaded,
                uploaded = torrent.Uploaded,
                downloadPath = torrent.SavePath ?? torrent.SourcePath,
                savePath = torrent.SavePath ?? torrent.SourcePath,
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
                errorMessage,
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

            if (notif.Categories != null && notif.Categories.Count > 0)
            {
                var torrentCategory = torrent.Category ?? torrent.Label;
                if (string.IsNullOrWhiteSpace(torrentCategory) ||
                    !notif.Categories.Any(c => string.Equals(c, torrentCategory, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
            {
                var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
                EnqueueDispatch(
                    async () => await _customScriptService.ExecuteScriptAsync(scriptPath, torrent, eventType, scriptArgs).ConfigureAwait(false),
                    eventType,
                    "CustomScript");
            }
            else if (string.Equals(notif.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
            {
                EnqueueDispatch(
                    async () => await EmailNotificationSender.SendEmailNotificationAsync(notif.Settings, eventType, torrent, meta, payload).ConfigureAwait(false),
                    eventType,
                    "Email");
            }
            else
            {
                var providerPayload = NotificationPayloadBuilder.BuildProviderPayload(notif.Implementation, eventType, torrent, meta, payload, notif.Settings);
                var targetUrl = NotificationPayloadBuilder.ResolveTargetUrl(notif.Implementation, notif.Settings);
                var customHeaders = NotificationPayloadBuilder.ResolveCustomHeaders(notif.Implementation, notif.Settings);
                EnqueueDispatch(
                    async () => await _webhookDispatcher.DispatchAsync(targetUrl, providerPayload, customHeaders).ConfigureAwait(false),
                    eventType,
                    notif.Implementation ?? "Webhook");
            }
        }
    }
}
