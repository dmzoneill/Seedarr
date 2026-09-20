#pragma warning disable SA1117
#pragma warning disable IDE0007

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Terminal;

namespace Seedarr.Http.Terminal;

public class TerminalHub : Hub
{
    private readonly ITerminalService _terminalService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly Logger _logger;

    public TerminalHub(ITerminalService terminalService, IConfigFileProvider configFileProvider = null)
    {
        _terminalService = terminalService;
        _configFileProvider = configFileProvider;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var config = _configFileProvider ?? (httpContext?.RequestServices?.GetService(typeof(IConfigFileProvider)) as IConfigFileProvider);

        if (config != null && !config.TerminalAccessEnabled)
        {
            _logger.Warn("Rejecting terminal connection because TerminalAccessEnabled is false: {0}", Context.ConnectionId);
            Context.Abort();
            return;
        }

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
                        _logger.Debug(ex, "Default AuthenticateAsync failed for Terminal SignalR connection");
                    }
                }

                if (!isAuth)
                {
                    _logger.Warn("Rejecting unauthenticated Terminal SignalR connection: {0}", Context.ConnectionId);
                    Context.Abort();
                    return;
                }
            }
        }

        await base.OnConnectedAsync();
    }

    public async Task StartSession(int cols, int rows)
    {
        var connectionId = Context.ConnectionId;
        _logger.Info("Starting terminal session for connection {0} ({1}x{2})", connectionId, cols, rows);

        await _terminalService.StartSessionAsync(
            connectionId,
            cols,
            rows,
            async (output) =>
            {
                await Clients.Caller.SendAsync("ReceiveOutput", output);
            },
            () =>
            {
                _logger.Debug("Terminal session exited for connection {0}", connectionId);
            },
            Context.ConnectionAborted);
    }

    public async Task WriteInput(string data)
    {
        await _terminalService.WriteInputAsync(Context.ConnectionId, data);
    }

    public void Resize(int cols, int rows)
    {
        _terminalService.Resize(Context.ConnectionId, cols, rows);
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var connectionId = Context.ConnectionId;
        _logger.Info("Client disconnected, terminating terminal session for {0}", connectionId);
        await _terminalService.CloseSessionAsync(connectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        Span<byte> hashA = stackalloc byte[32];
        Span<byte> hashB = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(left), hashA);
        SHA256.HashData(Encoding.UTF8.GetBytes(right), hashB);
        return CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }
}
