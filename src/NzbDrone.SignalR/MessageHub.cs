using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.SignalR;

public class MessageHub : Hub
{
    private static readonly HashSet<string> Connections = new();
    private readonly IConfigFileProvider _configFileProvider;
    private readonly Logger _logger;

    public MessageHub(IConfigFileProvider configFileProvider = null)
    {
        _configFileProvider = configFileProvider;
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

    public override Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var config = _configFileProvider ?? (httpContext?.RequestServices.GetService(typeof(IConfigFileProvider)) as IConfigFileProvider);

        if (config != null && config.AuthenticationEnabled)
        {
            var isAuth = Context.User?.Identity?.IsAuthenticated == true;
            if (!isAuth && httpContext != null)
            {
                var masterApiKey = config.ApiKey;
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
            }

            if (!isAuth)
            {
                _logger.Warn("Rejecting unauthenticated SignalR connection: {0}", Context.ConnectionId);
                Context.Abort();
                return Task.CompletedTask;
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

        return Clients.Caller.SendAsync("receiveMessage", message);
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

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }
}
