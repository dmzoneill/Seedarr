using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.SignalR;

public class MessageHub : Hub
{
    private static readonly HashSet<string> Connections = new();
    private readonly IConfigFileProvider _configFileProvider;
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;

    public MessageHub(IConfigFileProvider configFileProvider = null, ITorrentService torrentService = null)
    {
        _configFileProvider = configFileProvider;
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public static bool IsConnected
    {
        get
        {
            lock (Connections)
            {
                return Connections.Count > 0;
            }
        }
    }

    public static void ResetForTesting()
    {
        lock (Connections)
        {
            Connections.Clear();
        }
    }

    public static void AddConnectionForTesting(string connectionId = "test-connection")
    {
        lock (Connections)
        {
            Connections.Add(connectionId);
        }
    }

    public static void RemoveConnectionForTesting(string connectionId = "test-connection")
    {
        lock (Connections)
        {
            Connections.Remove(connectionId);
        }
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var config = _configFileProvider ?? (httpContext?.RequestServices?.GetService(typeof(IConfigFileProvider)) as IConfigFileProvider);

        if (config != null && config.AuthenticationEnabled)
        {
            var isAuth = Context.User?.Identity?.IsAuthenticated == true;
            var masterApiKey = config.ApiKey;

            if (!isAuth && httpContext != null)
            {
                if (!string.IsNullOrWhiteSpace(masterApiKey))
                {
                    if (httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
                    {
                        var authStr = authHeader.ToString().Trim();
                        if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = authStr["Bearer ".Length..].Trim();
                            if (FixedTimeEquals(token, masterApiKey))
                            {
                                isAuth = true;
                            }
                        }
                        else if (authStr.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                        {
                            var param = authStr["Basic ".Length..].Trim();
                            try
                            {
                                var credentialBytes = Convert.FromBase64String(param);
                                var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
                                var username = credentials.Length > 0 ? credentials[0] : string.Empty;
                                var password = credentials.Length > 1 ? credentials[1] : string.Empty;

                                if (FixedTimeEquals(password, masterApiKey) || FixedTimeEquals(username, masterApiKey))
                                {
                                    isAuth = true;
                                }
                            }
                            catch
                            {
                                // Ignore malformed Basic auth header
                            }
                        }
                    }

                    if (!isAuth && httpContext.Request.Headers.TryGetValue("X-Api-Key", out var headerKey) &&
                        FixedTimeEquals(headerKey.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                    else if (!isAuth && httpContext.Request.Headers.TryGetValue("ApiKey", out var customApiKey) &&
                        FixedTimeEquals(customApiKey.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                    else if (!isAuth && httpContext.Request.Query.TryGetValue("access_token", out var queryToken) &&
                        FixedTimeEquals(queryToken.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                    else if (!isAuth && httpContext.Request.Query.TryGetValue("apikey", out var queryApiKey) &&
                        FixedTimeEquals(queryApiKey.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                    else if (!isAuth && httpContext.Request.Query.TryGetValue("api_key", out var queryApiKey2) &&
                        FixedTimeEquals(queryApiKey2.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                    else if (!isAuth && httpContext.Request.Query.TryGetValue("token", out var queryToken2) &&
                        FixedTimeEquals(queryToken2.ToString(), masterApiKey))
                    {
                        isAuth = true;
                    }
                }

                if (!isAuth)
                {
                    try
                    {
                        var defaultAuth = await httpContext.AuthenticateAsync();
                        if (defaultAuth?.Succeeded == true && defaultAuth.Principal?.Identity?.IsAuthenticated == true)
                        {
                            isAuth = true;
                            httpContext.User = defaultAuth.Principal;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Default AuthenticateAsync failed for SignalR connection");
                    }
                }

                if (!isAuth && httpContext.Request.Query.TryGetValue("access_token", out var queryAccessToken) &&
                    !string.IsNullOrWhiteSpace(queryAccessToken))
                {
                    var tokenStr = queryAccessToken.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(masterApiKey) && FixedTimeEquals(tokenStr, masterApiKey))
                    {
                        isAuth = true;
                    }
                    else
                    {
                        var schemeProvider = httpContext.RequestServices?.GetService(typeof(IAuthenticationSchemeProvider)) as IAuthenticationSchemeProvider;
                        if (schemeProvider != null)
                        {
                            var originalAuth = httpContext.Request.Headers["Authorization"].ToString();
                            var hadAuth = httpContext.Request.Headers.ContainsKey("Authorization");
                            httpContext.Request.Headers["Authorization"] = $"Bearer {tokenStr}";

                            try
                            {
                                var schemes = await schemeProvider.GetAllSchemesAsync();
                                foreach (var scheme in schemes)
                                {
                                    if (string.Equals(scheme.Name, "ApiKey", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    try
                                    {
                                        var result = await httpContext.AuthenticateAsync(scheme.Name);
                                        if (result?.Succeeded == true && result.Principal?.Identity?.IsAuthenticated == true)
                                        {
                                            isAuth = true;
                                            httpContext.User = result.Principal;
                                            break;
                                        }
                                    }
                                    catch
                                    {
                                        // Ignore scheme failures
                                    }
                                }
                            }
                            finally
                            {
                                if (hadAuth)
                                {
                                    httpContext.Request.Headers["Authorization"] = originalAuth;
                                }
                                else if (!isAuth)
                                {
                                    httpContext.Request.Headers.Remove("Authorization");
                                }
                            }
                        }
                    }
                }

                if (!isAuth)
                {
                    var schemeProvider = httpContext.RequestServices?.GetService(typeof(IAuthenticationSchemeProvider)) as IAuthenticationSchemeProvider;
                    if (schemeProvider != null)
                    {
                        var schemes = await schemeProvider.GetAllSchemesAsync();
                        foreach (var scheme in schemes)
                        {
                            if (string.Equals(scheme.Name, "ApiKey", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            try
                            {
                                var result = await httpContext.AuthenticateAsync(scheme.Name);
                                if (result?.Succeeded == true && result.Principal?.Identity?.IsAuthenticated == true)
                                {
                                    isAuth = true;
                                    httpContext.User = result.Principal;
                                    break;
                                }
                            }
                            catch
                            {
                                // Ignore scheme failures
                            }
                        }
                    }
                }

                if (!isAuth && Context.User?.Identity?.IsAuthenticated == true)
                {
                    isAuth = true;
                }
            }

            if (!isAuth)
            {
                _logger.Warn("Rejecting unauthenticated SignalR connection: {0}", Context.ConnectionId);
                Context.Abort();
                return;
            }
        }

        lock (Connections)
        {
            Connections.Add(Context.ConnectionId);
        }

        _logger.Debug("SignalR client connected: {0}", Context.ConnectionId);

        var message = new SignalRMessage
        {
            Name = "version",
            Body = new { Version = BuildInfo.Version.ToString() },
        };

        await Clients.Caller.SendAsync("receiveMessage", message);
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        lock (Connections)
        {
            Connections.Remove(Context.ConnectionId);
        }

        _logger.Debug("SignalR client disconnected: {0}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        Span<byte> hashA = stackalloc byte[32];
        Span<byte> hashB = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(a), hashA);
        SHA256.HashData(Encoding.UTF8.GetBytes(b), hashB);
        return CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }

    public Task TrackerUpdated(object payload)
    {
        return Clients.All.SendAsync("trackerUpdated", payload);
    }

    public Task TrackerAnnounced(object payload)
    {
        return Clients.All.SendAsync("trackerAnnounced", payload);
    }

    public async Task SubscribeToTorrent(int torrentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"torrent-{torrentId}");
    }

    public async Task UnsubscribeFromTorrent(int torrentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"torrent-{torrentId}");
    }

    public async Task SubscribeToChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"channel-{channel.ToLowerInvariant()}");
    }

    public async Task UnsubscribeFromChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"channel-{channel.ToLowerInvariant()}");
    }

    public virtual async Task<StateSnapshotResource> RequestStateSnapshot()
    {
        var httpContext = Context.GetHttpContext();
        var torrentService = _torrentService ?? (httpContext?.RequestServices?.GetService(typeof(ITorrentService)) as ITorrentService);

        var torrents = torrentService?.GetAll() ?? new List<Torrent>();

        long totalDownloadSpeed = 0;
        long totalUploadSpeed = 0;
        var summaries = new List<TorrentSnapshotResource>(torrents.Count);

        foreach (var t in torrents)
        {
            totalDownloadSpeed += t.DownloadSpeed;
            totalUploadSpeed += t.UploadSpeed;

            summaries.Add(new TorrentSnapshotResource
            {
                Id = t.Id,
                Name = t.Name,
                Status = t.Status.ToString(),
                Progress = t.Progress,
                DownloadSpeed = t.DownloadSpeed,
                UploadSpeed = t.UploadSpeed,
                Eta = t.Eta,
                Size = t.TotalSize,
                TotalSize = t.TotalSize,
                Active = t.Active
            });
        }

        var snapshot = new StateSnapshotResource
        {
            Torrents = summaries,
            DownloadSpeed = totalDownloadSpeed,
            UploadSpeed = totalUploadSpeed,
            ActiveCount = torrents.FindAll(t => t.Active).Count,
            TotalCount = torrents.Count,
            TimestampUtc = DateTime.UtcNow
        };

        if (Clients?.Caller != null)
        {
            try
            {
                await Clients.Caller.SendAsync("stateSnapshot", snapshot);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send stateSnapshot event to caller");
            }
        }

        return snapshot;
    }
}

public class TrackerSignalREventHandler : IHandle<TrackerAnnounceEvent>, IHandle<TrackerStatusChangedEvent>
{
    private readonly IHubContext<MessageHub> _hubContext;
    private readonly Logger _logger;

    public TrackerSignalREventHandler(IHubContext<MessageHub> hubContext)
    {
        _hubContext = hubContext;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Handle(TrackerAnnounceEvent message)
    {
        if (message == null || _hubContext == null)
        {
            return;
        }

        var status = message.Status != TrackerStatus.Unknown
            ? message.Status.ToString()
            : (message.IsSuccess ? TrackerStatus.Working.ToString() : TrackerStatus.Failed.ToString());

        var payload = new
        {
            torrentId = message.Torrent?.Id ?? 0,
            trackerId = message.TrackerId,
            url = message.TrackerUrl,
            status = status,
            seeders = message.Seeders,
            leechers = message.Leechers,
            responseTimeMs = message.ResponseTimeMs,
            errorMessage = message.ErrorMessage
        };

        BroadcastTrackerPayload(payload);
    }

    public void Handle(TrackerStatusChangedEvent message)
    {
        if (message == null || _hubContext == null)
        {
            return;
        }

        var payload = new
        {
            torrentId = message.Torrent?.Id ?? message.Tracker?.TorrentId ?? 0,
            trackerId = message.Tracker?.Id ?? 0,
            url = message.Tracker?.Url,
            status = message.NewStatus.ToString(),
            seeders = message.Tracker?.Seeders ?? 0,
            leechers = message.Tracker?.Leechers ?? 0,
            responseTimeMs = (long)(message.Tracker?.LastResponseTime ?? 0),
            errorMessage = message.Tracker?.ErrorMessage
        };

        BroadcastTrackerPayload(payload);
    }

    private void BroadcastTrackerPayload(object payload)
    {
        try
        {
            _hubContext.Clients?.All?.SendAsync("trackerUpdated", payload)
                ?.ContinueWith(t => _logger.Warn(t.Exception, "Failed to broadcast trackerUpdated"), TaskContinuationOptions.OnlyOnFaulted);

            _hubContext.Clients?.All?.SendAsync("trackerAnnounced", payload)
                ?.ContinueWith(t => _logger.Warn(t.Exception, "Failed to broadcast trackerAnnounced"), TaskContinuationOptions.OnlyOnFaulted);

            _hubContext.Clients?.All?.SendAsync("TrackerUpdated", payload)
                ?.ContinueWith(t => _logger.Warn(t.Exception, "Failed to broadcast TrackerUpdated"), TaskContinuationOptions.OnlyOnFaulted);

            _hubContext.Clients?.All?.SendAsync("TrackerAnnounced", payload)
                ?.ContinueWith(t => _logger.Warn(t.Exception, "Failed to broadcast TrackerAnnounced"), TaskContinuationOptions.OnlyOnFaulted);

            var message = new SignalRMessage
            {
                Name = "TrackerUpdated",
                Action = ModelAction.Updated,
                Body = payload
            };
            _hubContext.Clients?.All?.SendAsync("receiveMessage", message)
                ?.ContinueWith(t => _logger.Warn(t.Exception, "Failed to broadcast tracker receiveMessage"), TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to broadcast tracker update over SignalR");
        }
    }
}
