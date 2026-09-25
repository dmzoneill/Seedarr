using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Integration.Test;

public sealed class MockArrServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _listenTask;
    private int _nextNotificationId = 1;

    public string Url { get; }

    public List<Dictionary<string, object>> Notifications { get; } = new();

    public List<string> ReceivedRequests { get; } = new();

    public MockArrServer()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();

        Url = $"http://127.0.0.1:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"{Url}/");
        _listener.Start();

        _listenTask = Task.Run(ListenLoop);
    }

    public void AddExistingNotification(string name, string webhookUrl)
    {
        lock (Notifications)
        {
            var id = _nextNotificationId++;
            Notifications.Add(new Dictionary<string, object>
            {
                ["id"] = id,
                ["name"] = name,
                ["implementation"] = "Webhook",
                ["configContract"] = "WebhookSettings",
                ["fields"] = new List<object>
                {
                    new Dictionary<string, object> { ["name"] = "url", ["value"] = webhookUrl },
                    new Dictionary<string, object> { ["name"] = "method", ["value"] = 1 }
                }
            });
        }
    }

    private async Task ListenLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
            catch when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;
        var method = req.HttpMethod;
        var path = req.Url?.AbsolutePath ?? string.Empty;

        lock (ReceivedRequests)
        {
            ReceivedRequests.Add($"{method} {path}");
        }

        var body = string.Empty;
        if (req.HasEntityBody)
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            body = reader.ReadToEnd();
        }

        try
        {
            // Health / System Status
            if (path.EndsWith("/system/status", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(res, HttpStatusCode.OK, new { version = "4.0.0", appName = "Sonarr" });
                return;
            }

            // Notifications
            if (path.EndsWith("/notification", StringComparison.OrdinalIgnoreCase))
            {
                if (method == "GET")
                {
                    lock (Notifications)
                    {
                        WriteJson(res, HttpStatusCode.OK, Notifications);
                    }

                    return;
                }

                if (method == "POST")
                {
                    lock (Notifications)
                    {
                        var notif = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();
                        var id = _nextNotificationId++;
                        notif["id"] = id;
                        Notifications.Add(notif);
                        WriteJson(res, HttpStatusCode.Created, notif);
                    }

                    return;
                }
            }

            if (path.Contains("/notification/", StringComparison.OrdinalIgnoreCase))
            {
                var idStr = path.Substring(path.LastIndexOf('/') + 1);
                if (int.TryParse(idStr, out var id))
                {
                    if (method == "PUT")
                    {
                        lock (Notifications)
                        {
                            var updated = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();
                            updated["id"] = id;
                            var idx = Notifications.FindIndex(n => Convert.ToInt32(n["id"].ToString()) == id);
                            if (idx >= 0)
                            {
                                Notifications[idx] = updated;
                            }
                            else
                            {
                                Notifications.Add(updated);
                            }

                            WriteJson(res, HttpStatusCode.OK, updated);
                        }

                        return;
                    }

                    if (method == "DELETE")
                    {
                        lock (Notifications)
                        {
                            Notifications.RemoveAll(n => Convert.ToInt32(n["id"].ToString()) == id);
                        }

                        res.StatusCode = (int)HttpStatusCode.OK;
                        res.Close();
                        return;
                    }
                }
            }

            // Default fallback
            res.StatusCode = (int)HttpStatusCode.OK;
            res.Close();
        }
        catch
        {
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            res.Close();
        }
    }

    private void WriteJson(HttpListenerResponse res, HttpStatusCode status, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        res.StatusCode = (int)status;
        res.ContentType = "application/json";
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Flush();
        res.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
