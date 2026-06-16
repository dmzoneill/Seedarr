using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.TrackerServer;

public class TrackerServer : BackgroundService
{
    private const int TrackerPort = 9696;

    private readonly PeerDatabase _peerDatabase;
    private readonly Logger _logger;

    public TrackerServer(PeerDatabase peerDatabase)
    {
        _peerDatabase = peerDatabase;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, TrackerPort);
        listener.Start();
        _logger.Info("Built-in tracker listening on port {0}", TrackerPort);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(() => HandleRequest(client), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private void HandleRequest(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);

            var requestLine = reader.ReadLine();
            if (requestLine == null)
            {
                return;
            }

            var parts = requestLine.Split(' ');
            if (parts.Length < 2 || parts[0] != "GET")
            {
                return;
            }

            var path = parts[1];
            var remoteEndpoint = (IPEndPoint)client.Client.RemoteEndPoint;

            string responseBody;

            if (path.StartsWith("/announce"))
            {
                responseBody = HandleAnnounce(path, remoteEndpoint);
            }
            else if (path.StartsWith("/scrape"))
            {
                responseBody = HandleScrape(path);
            }
            else
            {
                responseBody = "d14:failure reason13:Invalid requeste";
            }

            var httpResponse = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n{responseBody}";
            var responseBytes = Encoding.ASCII.GetBytes(httpResponse);
            stream.Write(responseBytes, 0, responseBytes.Length);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Tracker request error");
        }
        finally
        {
            client.Dispose();
        }
    }

    private string HandleAnnounce(string path, IPEndPoint remoteEndpoint)
    {
        var queryIndex = path.IndexOf('?');
        if (queryIndex < 0)
        {
            return "d14:failure reason20:Missing query stringe";
        }

        var query = path[(queryIndex + 1)..];
        var parameters = ParseQueryString(query);

        if (!parameters.TryGetValue("info_hash", out var infoHash) ||
            !parameters.TryGetValue("port", out var portStr))
        {
            return "d14:failure reason25:Missing required parameterse";
        }

        var port = int.Parse(portStr);
        var peerIp = remoteEndpoint.Address.ToString();

        parameters.TryGetValue("peer_id", out var peerId);
        parameters.TryGetValue("event", out var eventType);

        if (eventType == "stopped")
        {
            _peerDatabase.RemovePeer(infoHash, peerIp, port);
        }
        else
        {
            _peerDatabase.AddPeer(infoHash, peerIp, port, peerId ?? "");
        }

        var peers = _peerDatabase.GetPeers(infoHash);
        var interval = 1800;
        var compactPeers = BuildCompactPeers(peers, peerIp, port);

        return $"d8:intervali{interval}e5:peers{compactPeers.Length}:{Encoding.Latin1.GetString(compactPeers)}e";
    }

    private string HandleScrape(string path)
    {
        var queryIndex = path.IndexOf('?');
        if (queryIndex < 0)
        {
            return "d14:failure reason20:Missing query stringe";
        }

        var query = path[(queryIndex + 1)..];
        var parameters = ParseQueryString(query);

        if (!parameters.TryGetValue("info_hash", out var infoHash))
        {
            return "d14:failure reason18:Missing info_hashe";
        }

        var stats = _peerDatabase.GetStats(infoHash);
        return $"d5:filesd{infoHash.Length}:{infoHash}d8:completei{stats.Complete}e10:downloadedi{stats.Downloaded}e10:incompletei{stats.Incomplete}eeee";
    }

    private static byte[] BuildCompactPeers(List<TrackerPeerEntry> peers, string excludeIp, int excludePort)
    {
        var filtered = peers.Where(p => p.Ip != excludeIp || p.Port != excludePort).ToList();
        var data = new byte[filtered.Count * 6];
        for (var i = 0; i < filtered.Count; i++)
        {
            var ipParts = filtered[i].Ip.Split('.');
            data[i * 6] = byte.Parse(ipParts[0]);
            data[(i * 6) + 1] = byte.Parse(ipParts[1]);
            data[(i * 6) + 2] = byte.Parse(ipParts[2]);
            data[(i * 6) + 3] = byte.Parse(ipParts[3]);
            data[(i * 6) + 4] = (byte)(filtered[i].Port >> 8);
            data[(i * 6) + 5] = (byte)filtered[i].Port;
        }

        return data;
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&'))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = Uri.UnescapeDataString(pair[..eqIndex]);
                var value = Uri.UnescapeDataString(pair[(eqIndex + 1)..]);
                result[key] = value;
            }
        }

        return result;
    }
}
