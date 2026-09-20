#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Automation;

public class AutomationService : IAutomationService
{
    public const int MaxPersistedLogCharacters = 32768;
    public const int MaxExecutionDepth = 3;

    private static readonly AsyncLocal<int> _executionDepth = new();
    private static readonly AsyncLocal<HashSet<string>?> _activeCallChain = new();

    public static int CurrentExecutionDepth => _executionDepth.Value;

    private readonly IAutomationScriptRepository _scriptRepository;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITagService _tagService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IManageCommandQueue? _commandQueue;
    private readonly ICustomScriptService? _customScriptService;
    private readonly INotificationRepository? _notificationRepository;
    private readonly IWebhookDispatcher? _webhookDispatcher;
    private readonly IArchiveExtractorService? _archiveExtractorService;
    private readonly ITrackerBoostService? _trackerBoostService;
    private readonly ITrackerAnnounceService? _trackerAnnounceService;
    private readonly IConnectionManager? _connectionManager;
    private readonly IConfigService? _configService;
    private readonly ITorrentService? _torrentService;
    private readonly Logger _logger;
    private readonly JintScriptRunner _jintRunner;
    private readonly YamlScriptRunner _yamlRunner;

    public AutomationService(
        IAutomationScriptRepository scriptRepository,
        ITorrentRepository torrentRepository,
        ITagService tagService,
        IEventAggregator eventAggregator,
        IManageCommandQueue? commandQueue = null,
        IConfigFileProvider? configFileProvider = null,
        ICustomScriptService? customScriptService = null,
        INotificationRepository? notificationRepository = null,
        IWebhookDispatcher? webhookDispatcher = null,
        IArchiveExtractorService? archiveExtractorService = null,
        ITrackerBoostService? trackerBoostService = null,
        ITrackerAnnounceService? trackerAnnounceService = null,
        IConnectionManager? connectionManager = null,
        IConfigService? configService = null,
        ITorrentService? torrentService = null)
    {
        _scriptRepository = scriptRepository;
        _torrentRepository = torrentRepository;
        _tagService = tagService;
        _eventAggregator = eventAggregator;
        _commandQueue = commandQueue;
        _customScriptService = customScriptService;
        _notificationRepository = notificationRepository;
        _webhookDispatcher = webhookDispatcher;
        _archiveExtractorService = archiveExtractorService;
        _trackerBoostService = trackerBoostService;
        _trackerAnnounceService = trackerAnnounceService;
        _connectionManager = connectionManager;
        _configService = configService;
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
        _jintRunner = new JintScriptRunner(commandQueue, configFileProvider, configService);
        _yamlRunner = new YamlScriptRunner(commandQueue);
    }

    public List<AutomationScript> GetAll()
    {
        return _scriptRepository.All().ToList();
    }

    public AutomationScript Get(int id)
    {
        return _scriptRepository.Get(id);
    }

    public AutomationScript Add(AutomationScript script)
    {
        script.CreatedAt = DateTime.UtcNow;
        var added = _scriptRepository.Insert(script);
        _eventAggregator.PublishEvent(new ModelEvent<AutomationScript>(added, ModelAction.Created));
        return added;
    }

    public AutomationScript Update(AutomationScript script)
    {
        var updated = _scriptRepository.Update(script);
        _eventAggregator.PublishEvent(new ModelEvent<AutomationScript>(updated, ModelAction.Updated));
        return updated;
    }

    public void Delete(int id)
    {
        var existing = _scriptRepository.Get(id);
        if (existing != null)
        {
            _scriptRepository.Delete(id);
            _eventAggregator.PublishEvent(new ModelEvent<AutomationScript>(existing, ModelAction.Deleted));
        }
    }

    public AutomationExecutionResult ExecuteScript(int scriptId, int? torrentId = null, Dictionary<string, object>? customInputs = null)
    {
        var script = _scriptRepository.Get(scriptId);
        if (script == null)
        {
            return new AutomationExecutionResult
            {
                Success = false,
                Error = $"Script with ID {scriptId} not found.",
            };
        }

        Torrent? torrent = null;
        if (torrentId.HasValue && torrentId.Value > 0)
        {
            torrent = _torrentRepository.Get(torrentId.Value);
        }

        return ExecuteScript(script, torrent, customInputs);
    }

    public AutomationExecutionResult ExecuteScript(AutomationScript script, Torrent? torrent = null, Dictionary<string, object>? customInputs = null)
    {
        var currentDepth = _executionDepth.Value;
        if (currentDepth >= MaxExecutionDepth)
        {
            _logger.Warn("Automation execution recursion depth limit ({0}) exceeded for script '{1}'. Aborting execution to prevent infinite cascade.", MaxExecutionDepth, script.Name);
            return new AutomationExecutionResult
            {
                Success = false,
                Error = $"Recursion depth limit ({MaxExecutionDepth}) exceeded for script '{script.Name}'.",
                OutputLog = $"[WARN] Recursion depth limit ({MaxExecutionDepth}) exceeded. Execution aborted.",
            };
        }

        var parentChain = _activeCallChain.Value;
        var chain = parentChain != null
            ? new HashSet<string>(parentChain, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var scriptKey = script.Id > 0 ? script.Id.ToString() : (script.Name ?? "unnamed");
        var callKey = torrent != null ? $"{scriptKey}:torrent_{torrent.Id}" : $"{scriptKey}:global";

        if (chain.Contains(callKey))
        {
            _logger.Warn(
                "Reentrancy detected: Script '{0}' (ID: {1}) is already executing for {2} in active call chain. Aborting execution.",
                script.Name,
                script.Id,
                torrent != null ? $"torrent {torrent.Id}" : "global entity");

            return new AutomationExecutionResult
            {
                Success = false,
                Error = $"Reentrancy detected: Script '{script.Name}' is already executing in active call chain.",
                OutputLog = $"[WARN] Reentrancy detected for script '{script.Name}'. Execution aborted.",
            };
        }

        chain.Add(callKey);
        _activeCallChain.Value = chain;
        _executionDepth.Value = currentDepth + 1;

        try
        {
            _logger.Info("Executing automation script '{0}' (Trigger: {1}, Language: {2})", script.Name, script.Trigger, script.Language);

            var torrentTags = new List<string>();
            if (torrent != null && torrent.TagIds != null && torrent.TagIds.Count > 0 && _tagService != null)
            {
                var allTags = _tagService.GetAll();
                if (allTags != null)
                {
                    var tagMap = allTags.ToDictionary(t => t.Id, t => t.Label);
                    foreach (var tid in torrent.TagIds)
                    {
                        if (tagMap.TryGetValue(tid, out var label))
                        {
                            torrentTags.Add(label);
                        }
                    }
                }
            }

            IScriptRunner runner = script.Language == AutomationLanguage.Yaml ? _yamlRunner : _jintRunner;
            var result = runner.Execute(script, torrent, torrentTags, customInputs);

            // Apply mutations if torrent is present and execution was successful
            if (result.Success && torrent != null)
            {
                ApplyTorrentMutations(torrent, result);
            }

            // Side-effects: Servarr Sync
            if (result.Success && result.ArrSyncsToSend.Count > 0 && _commandQueue != null)
            {
                foreach (var sync in result.ArrSyncsToSend)
                {
                    _commandQueue.PushRaw("SyncArr", "{}", CommandTrigger.Manual);
                }
            }

            // Side-effects: Custom scripts to run
            if (result.Success && result.ScriptsToRun.Count > 0 && _customScriptService != null)
            {
                foreach (var scriptToRun in result.ScriptsToRun)
                {
                    var argsStr = scriptToRun.Arguments != null && scriptToRun.Arguments.Count > 0
                        ? string.Join(" ", scriptToRun.Arguments.Select(SanitizeShellArgument))
                        : null;
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _customScriptService.ExecuteScriptAsync(scriptToRun.Path, torrent, "Automation", argsStr).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to execute custom script from automation: {0}", scriptToRun.Path);
                        }
                    });
                }
            }

            // Side-effects: Archive extraction
            if (result.Success && result.ShouldExtractArchive && torrent != null && _archiveExtractorService != null)
            {
                var dest = result.ExtractDestination;
                var deleteArchive = result.DeleteArchiveOnExtract;
                Task.Run(async () =>
                {
                    try
                    {
                        await _archiveExtractorService.ExtractTorrentArchiveAsync(torrent, dest, deleteArchive).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to execute archive extraction from automation script for torrent '{0}'", torrent.Name);
                    }
                });
            }
            else if (result.Success && result.ShouldExtractArchive && _archiveExtractorService == null)
            {
                _logger.Warn("Archive extraction requested for torrent '{0}', but IArchiveExtractorService is not available", torrent?.Name);
            }

            // Side-effects: Reannounce
            if (result.Success && result.ShouldReannounce && torrent != null)
            {
                if (_trackerAnnounceService != null)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            _trackerAnnounceService.AnnounceTorrent(torrent, force: true);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to reannounce torrent '{0}'", torrent.Name);
                        }
                    });
                }

                if (_trackerBoostService != null && !string.IsNullOrWhiteSpace(torrent.InfoHash))
                {
                    try
                    {
                        _trackerBoostService.ReannounceDownloadClients(torrent.InfoHash);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to reannounce download clients for torrent '{0}'", torrent.Name);
                    }
                }
            }

            if (result.Success && result.ShouldReannounceAll)
            {
                Task.Run(() =>
                {
                    try
                    {
                        var allTorrents = _torrentRepository.All();
                        foreach (var t in allTorrents)
                        {
                            _trackerAnnounceService?.AnnounceTorrent(t, force: true);
                            if (!string.IsNullOrWhiteSpace(t.InfoHash))
                            {
                                _trackerBoostService?.ReannounceDownloadClients(t.InfoHash);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to reannounce all torrents");
                    }
                });
            }

            // Side-effects: Tracker Boost
            if (result.Success && result.ShouldBoostTracker && torrent != null && _trackerBoostService != null)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await _trackerBoostService.BoostTorrentAsync(torrent.Id).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to boost tracker for torrent '{0}'", torrent.Name);
                    }
                });
            }
            else if (result.Success && result.ShouldBoostTracker && _trackerBoostService == null)
            {
                _logger.Warn("Tracker boost requested for torrent '{0}', but ITrackerBoostService is not available", torrent?.Name);
            }

            // Side-effects: Ban Peers
            if (result.Success && result.PeersToBan.Count > 0)
            {
                foreach (var peerIp in result.PeersToBan)
                {
                    if (!string.IsNullOrWhiteSpace(peerIp))
                    {
                        var trimmedIp = peerIp.Trim();
                        _logger.Info("Banning peer IP '{0}' for torrent '{1}' from automation script", trimmedIp, torrent?.Name);

                        if (_connectionManager != null)
                        {
                            var matchingConnections = _connectionManager.GetAllConnections()
                                ?.Where(c => !string.IsNullOrWhiteSpace(c.RemoteIp) && string.Equals(c.RemoteIp.Trim(), trimmedIp, StringComparison.OrdinalIgnoreCase))
                                .ToList() ?? new List<PeerConnection>();

                            _connectionManager.BanPeer(trimmedIp);

                            foreach (var conn in matchingConnections)
                            {
                                _connectionManager.Remove(conn);
                            }
                        }

                        _eventAggregator.PublishEvent(new PeerBannedEvent(trimmedIp, "Banned by automation script", torrent?.InfoHash ?? string.Empty));
                    }
                }
            }

            // Side-effects: Clean unwanted files
            if (result.Success && result.CleanFilePatterns.Count > 0 && torrent != null && !string.IsNullOrWhiteSpace(torrent.SavePath))
            {
                var savePath = torrent.SavePath;
                var patterns = new List<string>(result.CleanFilePatterns);
                Task.Run(() =>
                {
                    try
                    {
                        if (!Directory.Exists(savePath))
                        {
                            return;
                        }

                        foreach (var pattern in patterns)
                        {
                            if (string.IsNullOrWhiteSpace(pattern))
                            {
                                continue;
                            }

                            var cleanPattern = pattern.Trim();
                            var searchPattern = cleanPattern.Contains('*') || cleanPattern.Contains('?')
                                ? cleanPattern
                                : (cleanPattern.StartsWith('.') ? $"*{cleanPattern}" : $"*.{cleanPattern}");

                            var matchedFiles = Directory.GetFiles(savePath, searchPattern, SearchOption.AllDirectories);
                            foreach (var file in matchedFiles)
                            {
                                try
                                {
                                    File.Delete(file);
                                    _logger.Info("Cleaned unwanted file '{0}' matching pattern '{1}' for torrent '{2}'", file, cleanPattern, torrent.Name);
                                }
                                catch (Exception ex)
                                {
                                    _logger.Warn(ex, "Failed to delete cleaned file '{0}'", file);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to clean unwanted files for torrent '{0}'", torrent.Name);
                    }
                });
            }

            // Side-effects: Notifications to send
            if (result.Success && result.NotificationsToSend.Count > 0 && _notificationRepository != null && _webhookDispatcher != null)
            {
                var activeNotifications = _notificationRepository.GetEnabled();
                foreach (var notif in result.NotificationsToSend)
                {
                    var matching = string.IsNullOrWhiteSpace(notif.Provider)
                        ? activeNotifications
                        : activeNotifications.Where(n =>
                            string.Equals(n.Implementation, notif.Provider, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(n.Name, notif.Provider, StringComparison.OrdinalIgnoreCase)).ToList();

                    foreach (var n in matching)
                    {
                        var targetUrl = NotificationPayloadBuilder.ResolveTargetUrl(n.Implementation, n.Settings);
                        var customHeaders = NotificationPayloadBuilder.ResolveCustomHeaders(n.Implementation, n.Settings);
                        var payload = NotificationPayloadBuilder.BuildProviderPayload(n.Implementation, "Automation", torrent, null, new { title = notif.Title, message = notif.Message }, n.Settings);

                        Task.Run(async () =>
                        {
                            try
                            {
                                await _webhookDispatcher.DispatchAsync(targetUrl, payload, customHeaders).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to dispatch automation notification via {0}", n.Implementation);
                            }
                        });
                    }
                }
            }

            // Side-effects: Direct Webhooks
            if (result.Success && _webhookDispatcher != null)
            {
                if (result.DiscordWebhooksToSend.Count > 0)
                {
                    foreach (var d in result.DiscordWebhooksToSend)
                    {
                        if (string.IsNullOrWhiteSpace(d.Url))
                        {
                            continue;
                        }

                        int? colorInt = null;
                        if (!string.IsNullOrWhiteSpace(d.Color))
                        {
                            var hex = d.Color.Trim().TrimStart('#');
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedHex))
                            {
                                colorInt = parsedHex;
                            }
                            else if (int.TryParse(d.Color, out var parsedInt))
                            {
                                colorInt = parsedInt;
                            }
                        }

                        var embed = new Dictionary<string, object?>
                        {
                            ["title"] = string.IsNullOrWhiteSpace(d.Title) ? null : d.Title,
                            ["description"] = string.IsNullOrWhiteSpace(d.Description) ? null : d.Description,
                            ["timestamp"] = DateTime.UtcNow.ToString("o")
                        };

                        if (colorInt.HasValue)
                        {
                            embed["color"] = colorInt.Value;
                        }

                        if (d.Fields != null && d.Fields.Count > 0)
                        {
                            embed["fields"] = d.Fields.Select(f => new { name = f.Key, value = f.Value, @inline = true }).ToArray();
                        }

                        var payload = new Dictionary<string, object?>
                        {
                            ["username"] = "Seedarr",
                            ["embeds"] = new[] { embed }
                        };

                        Task.Run(async () =>
                        {
                            try
                            {
                                await _webhookDispatcher.DispatchAsync(d.Url, payload).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to dispatch Discord webhook to {0}", d.Url);
                            }
                        });
                    }
                }

                if (result.TelegramMessagesToSend.Count > 0)
                {
                    foreach (var tg in result.TelegramMessagesToSend)
                    {
                        if (string.IsNullOrWhiteSpace(tg.Token) || string.IsNullOrWhiteSpace(tg.ChatId))
                        {
                            continue;
                        }

                        var url = $"https://api.telegram.org/bot{tg.Token}/sendMessage";
                        var payload = new Dictionary<string, object?>
                        {
                            ["chat_id"] = tg.ChatId,
                            ["text"] = tg.Message,
                            ["parse_mode"] = string.IsNullOrWhiteSpace(tg.ParseMode) ? "Markdown" : tg.ParseMode
                        };

                        Task.Run(async () =>
                        {
                            try
                            {
                                await _webhookDispatcher.DispatchAsync(url, payload).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to dispatch Telegram message to chat {0}", tg.ChatId);
                            }
                        });
                    }
                }

                if (result.NtfyMessagesToSend.Count > 0)
                {
                    foreach (var nf in result.NtfyMessagesToSend)
                    {
                        if (string.IsNullOrWhiteSpace(nf.Topic))
                        {
                            continue;
                        }

                        var server = string.IsNullOrWhiteSpace(nf.Server) ? "https://ntfy.sh" : nf.Server.TrimEnd('/');
                        var url = $"{server}/{nf.Topic}";
                        var payload = new Dictionary<string, object?>
                        {
                            ["topic"] = nf.Topic,
                            ["message"] = nf.Message,
                        };

                        if (!string.IsNullOrWhiteSpace(nf.Title))
                        {
                            payload["title"] = nf.Title;
                        }

                        if (!string.IsNullOrWhiteSpace(nf.Priority))
                        {
                            if (int.TryParse(nf.Priority, out var pInt))
                            {
                                payload["priority"] = pInt;
                            }
                            else
                            {
                                payload["priority"] = nf.Priority;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(nf.Tags))
                        {
                            payload["tags"] = nf.Tags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        }

                        if (!string.IsNullOrWhiteSpace(nf.Click))
                        {
                            payload["click"] = nf.Click;
                        }

                        Task.Run(async () =>
                        {
                            try
                            {
                                await _webhookDispatcher.DispatchAsync(url, payload).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to dispatch Ntfy message to topic {0}", nf.Topic);
                            }
                        });
                    }
                }

                if (result.PushoverMessagesToSend.Count > 0)
                {
                    foreach (var po in result.PushoverMessagesToSend)
                    {
                        if (string.IsNullOrWhiteSpace(po.Token) || string.IsNullOrWhiteSpace(po.User))
                        {
                            continue;
                        }

                        var url = "https://api.pushover.net/1/messages.json";
                        var payload = new Dictionary<string, object?>
                        {
                            ["token"] = po.Token,
                            ["user"] = po.User,
                            ["message"] = po.Message
                        };

                        if (!string.IsNullOrWhiteSpace(po.Priority) && int.TryParse(po.Priority, out var prioInt))
                        {
                            payload["priority"] = prioInt;
                        }

                        if (!string.IsNullOrWhiteSpace(po.Sound))
                        {
                            payload["sound"] = po.Sound;
                        }

                        Task.Run(async () =>
                        {
                            try
                            {
                                await _webhookDispatcher.DispatchAsync(url, payload).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to dispatch Pushover message for user {0}", po.User);
                            }
                        });
                    }
                }
            }

            // Record execution stats on script
            script.LastExecutedAt = DateTime.UtcNow;
            script.LastExecutionStatus = result.Success ? "Success" : "Failed";
            script.LastExecutionLog = TruncateExecutionLog(result.OutputLog ?? result.Error);

            if (script.Id > 0)
            {
                try
                {
                    _scriptRepository.Update(script);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to update last execution stats for script {0}", script.Name);
                }
            }

            return result;
        }
        finally
        {
            _executionDepth.Value = currentDepth;
            _activeCallChain.Value = parentChain;
        }
    }

    public AutomationExecutionResult TestScript(AutomationScript script, int? torrentId = null, Dictionary<string, object>? customInputs = null)
    {
        Torrent? torrent = null;
        if (torrentId.HasValue && torrentId.Value > 0)
        {
            torrent = _torrentRepository.Get(torrentId.Value);
        }
        else
        {
            torrent = _torrentRepository.All().FirstOrDefault();
        }

        var torrentTags = new List<string>();
        if (torrent != null && torrent.TagIds != null && torrent.TagIds.Count > 0)
        {
            var allTags = _tagService.GetAll();
            var tagMap = allTags.ToDictionary(t => t.Id, t => t.Label);
            foreach (var tid in torrent.TagIds)
            {
                if (tagMap.TryGetValue(tid, out var label))
                {
                    torrentTags.Add(label);
                }
            }
        }
        else if (torrent == null)
        {
            torrent = new Torrent
            {
                Id = 1,
                Name = "Simulated.Linux.Ubuntu.24.04.LTS.iso",
                InfoHash = "0123456789abcdef0123456789abcdef01234567",
                TotalSize = 4_294_967_296L,
                Ratio = 2.45,
                Progress = 1.0f,
                Status = TorrentStatus.Seeding,
                Category = "Linux",
                TrackerUrl = "https://torrent.ubuntu.com/announce",
                DownloadSpeed = 0,
                UploadSpeed = 5_242_880,
                Seeders = 125,
                Leechers = 14,
                SavePath = "/downloads/completed/Linux",
                Downloaded = 4_294_967_296L,
                Uploaded = 10_522_670_875L,
                SeedingTime = 172800,
                IsPrivate = false,
            };
            torrentTags.Add("Simulated");
            torrentTags.Add("Verified");
        }

        IScriptRunner runner = script.Language == AutomationLanguage.Yaml ? _yamlRunner : _jintRunner;
        return runner.Execute(script, torrent, torrentTags, customInputs);
    }

    private void ApplyTorrentMutations(Torrent torrent, AutomationExecutionResult result)
    {
        var changed = false;
        var tagsModified = false;

        // Tags to add
        if (result.TagsToAdd.Count > 0)
        {
            var tagIdsToAdd = _tagService.SyncTagsFromLabels(result.TagsToAdd);
            torrent.TagIds ??= new List<int>();
            foreach (var tagId in tagIdsToAdd)
            {
                if (!torrent.TagIds.Contains(tagId))
                {
                    torrent.TagIds.Add(tagId);
                    changed = true;
                    tagsModified = true;
                }
            }
        }

        // Tags to remove
        if (result.TagsToRemove.Count > 0 && torrent.TagIds != null && torrent.TagIds.Count > 0 && _tagService != null)
        {
            var allTags = _tagService.GetAll();
            if (allTags != null)
            {
                var tagLookup = allTags.ToDictionary(t => t.Label, t => t.Id, StringComparer.OrdinalIgnoreCase);

                foreach (var tagLabel in result.TagsToRemove)
                {
                    if (tagLookup.TryGetValue(tagLabel, out var tagId))
                    {
                        if (torrent.TagIds.Remove(tagId))
                        {
                            changed = true;
                            tagsModified = true;
                        }
                    }
                }
            }
        }

        if (tagsModified && _tagService != null && torrent.TagIds != null)
        {
            torrent.Label = string.Join(", ", _tagService.GetLabelsForTagIds(torrent.TagIds));
        }

        // Category change
        if (!string.IsNullOrWhiteSpace(result.NewCategory) && !string.Equals(torrent.Category, result.NewCategory, StringComparison.OrdinalIgnoreCase))
        {
            torrent.Category = result.NewCategory;
            changed = true;
        }

        // Save Path
        if (!string.IsNullOrWhiteSpace(result.NewSavePath) && !string.Equals(torrent.SavePath, result.NewSavePath, StringComparison.OrdinalIgnoreCase))
        {
            torrent.SavePath = result.NewSavePath;
            changed = true;
        }

        // Upload limit
        if (result.NewUploadLimitKbps.HasValue && torrent.UploadLimit != result.NewUploadLimitKbps.Value)
        {
            torrent.UploadLimit = result.NewUploadLimitKbps.Value;
            changed = true;
        }

        // Download limit
        if (result.NewDownloadLimitKbps.HasValue && torrent.DownloadLimit != result.NewDownloadLimitKbps.Value)
        {
            torrent.DownloadLimit = result.NewDownloadLimitKbps.Value;
            changed = true;
        }

        // Priority
        if (result.NewPriority.HasValue && torrent.Priority != result.NewPriority.Value)
        {
            torrent.Priority = result.NewPriority.Value;
            changed = true;
        }

        // Sequential download
        if (result.NewSequentialDownload.HasValue && torrent.SequentialDownload != result.NewSequentialDownload.Value)
        {
            torrent.SequentialDownload = result.NewSequentialDownload.Value;
            changed = true;
        }

        // Super seeding
        if (result.NewSuperSeeding.HasValue && torrent.SuperSeeding != result.NewSuperSeeding.Value)
        {
            torrent.SuperSeeding = result.NewSuperSeeding.Value;
            changed = true;
        }

        // Tracker remove
        if (result.TrackersToRemove.Count > 0 && !string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            foreach (var trackerToRemove in result.TrackersToRemove)
            {
                if (!string.IsNullOrWhiteSpace(trackerToRemove) &&
                    (string.Equals(torrent.TrackerUrl, trackerToRemove, StringComparison.OrdinalIgnoreCase) ||
                    torrent.TrackerUrl.Contains(trackerToRemove, StringComparison.OrdinalIgnoreCase)))
                {
                    torrent.TrackerUrl = string.Empty;
                    changed = true;
                    break;
                }
            }
        }

        // Tracker replace
        if (result.TrackersToReplace.Count > 0 && !string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            foreach (var kvp in result.TrackersToReplace)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && torrent.TrackerUrl.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    torrent.TrackerUrl = torrent.TrackerUrl.Replace(kvp.Key, kvp.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    changed = true;
                }
            }
        }

        // Tracker add
        if (result.TrackersToAdd.Count > 0 && string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            torrent.TrackerUrl = result.TrackersToAdd[0];
            changed = true;
        }

        // Status mutations (recheck, pause, resume, remove)
        if (result.ShouldRemove)
        {
            _logger.Info("Automation script requested removal of torrent '{0}' (deleteData={1})", torrent.Name, result.DeleteDataOnRemove);
            if (_torrentService != null)
            {
                _torrentService.Delete(torrent.Id, result.DeleteDataOnRemove);
            }
            else
            {
                _torrentRepository.Delete(torrent.Id);
                _eventAggregator.PublishEvent(new TorrentDeletedEvent(torrent.Id, torrent));
                _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Deleted));
            }

            return;
        }

        if (result.ShouldRecheck)
        {
            var oldStatus = torrent.Status;
            torrent.Status = TorrentStatus.Checking;
            torrent.Progress = 0;
            _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Checking));
            changed = true;
        }
        else if (result.ShouldPause)
        {
            var oldStatus = torrent.Status;
            torrent.Status = TorrentStatus.Paused;
            _eventAggregator.PublishEvent(new TorrentPausedEvent(torrent));
            if (oldStatus != TorrentStatus.Paused)
            {
                _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Paused));
                changed = true;
            }
        }
        else if (result.ShouldResume && torrent.Status == TorrentStatus.Paused)
        {
            var oldStatus = torrent.Status;
            torrent.Status = (torrent.Progress >= 1.0 || torrent.Progress >= 0.999 || torrent.ForceCompleted)
                ? TorrentStatus.Seeding
                : TorrentStatus.Downloading;
            _eventAggregator.PublishEvent(new TorrentStartedEvent(torrent));
            _eventAggregator.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, torrent.Status));
            changed = true;
        }

        if (changed)
        {
            _torrentRepository.Update(torrent);
            _eventAggregator.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));
        }
    }

    public static string? TruncateExecutionLog(string? log, int maxChars = MaxPersistedLogCharacters)
    {
        if (string.IsNullOrEmpty(log) || log.Length <= maxChars)
        {
            return log;
        }

        const string notice = "\n\n... [OUTPUT TRUNCATED FOR DATABASE STORAGE] ...\n\n";
        if (maxChars <= notice.Length)
        {
            return log.Substring(0, maxChars);
        }

        var available = maxChars - notice.Length;
        var headLength = available / 2;
        var tailLength = available - headLength;

        return string.Concat(
            log.AsSpan(0, headLength),
            notice,
            log.AsSpan(log.Length - tailLength, tailLength));
    }

    private static string SanitizeShellArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        var clean = arg.Replace("\0", string.Empty).Replace("\r", string.Empty).Replace("\n", " ");
        if (clean.Contains(' ') || clean.Contains('\t') || clean.Contains('"'))
        {
            return $"\"{clean.Replace("\"", "\\\"")}\"";
        }

        return clean;
    }
}
