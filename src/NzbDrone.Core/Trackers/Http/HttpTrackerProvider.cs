using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;
using NzbDrone.Core.Http;
using Polly;

namespace NzbDrone.Core.Trackers.Http;

public class HttpTrackerProvider : ITrackerProvider
{
    private static readonly HttpClient Client = new();
    private static readonly ResiliencePipeline Policy = ResiliencePolicies.GetTrackerPolicy();

    private readonly Logger _logger;

    public string Name => "HTTP";

    public HttpTrackerProvider()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public TrackerAnnounceResponse Announce(TrackerAnnounceRequest request)
    {
        try
        {
            var url = BuildAnnounceUrl(request);
            _logger.Debug("HTTP announce: {0}", url);

            var responseBytes = Policy.Execute(ct => Client.GetByteArrayAsync(url, ct).GetAwaiter().GetResult());
            var parser = new BencodeParser();
            var dict = parser.Parse<BDictionary>(responseBytes);

            if (dict.ContainsKey("failure reason"))
            {
                return new TrackerAnnounceResponse
                {
                    Success = false,
                    FailureReason = ((BString)dict["failure reason"]).ToString()
                };
            }

            var response = new TrackerAnnounceResponse
            {
                Success = true,
                Interval = dict.ContainsKey("interval") ? (int)((BNumber)dict["interval"]).Value : 1800,
                MinInterval = dict.ContainsKey("min interval") ? (int)((BNumber)dict["min interval"]).Value : 900,
                Complete = dict.ContainsKey("complete") ? (int)((BNumber)dict["complete"]).Value : 0,
                Incomplete = dict.ContainsKey("incomplete") ? (int)((BNumber)dict["incomplete"]).Value : 0,
                Peers = new List<TrackerPeer>()
            };

            if (dict.ContainsKey("warning message"))
            {
                response.WarningMessage = ((BString)dict["warning message"]).ToString();
            }

            if (dict.ContainsKey("peers"))
            {
                var peers = dict["peers"];
                if (peers is BList peerList)
                {
                    foreach (var peer in peerList.Cast<BDictionary>())
                    {
                        response.Peers.Add(new TrackerPeer
                        {
                            Ip = ((BString)peer["ip"]).ToString(),
                            Port = (int)((BNumber)peer["port"]).Value,
                            PeerId = peer.ContainsKey("peer id") ? ((BString)peer["peer id"]).ToString() : null
                        });
                    }
                }
                else if (peers is BString compactPeers)
                {
                    var data = compactPeers.Value;
                    for (var i = 0; i + 5 < data.Length; i += 6)
                    {
                        var span = data.Span;
                        var ip = $"{span[i]}.{span[i + 1]}.{span[i + 2]}.{span[i + 3]}";
                        var port = (span[i + 4] << 8) | span[i + 5];
                        response.Peers.Add(new TrackerPeer { Ip = ip, Port = port });
                    }
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HTTP announce failed for {0}", request.TrackerUrl);
            return new TrackerAnnounceResponse
            {
                Success = false,
                FailureReason = ex.Message
            };
        }
    }

    public TrackerScrapeResponse Scrape(string infoHash, string trackerUrl)
    {
        try
        {
            var scrapeUrl = trackerUrl.Replace("/announce", "/scrape");
            var hashBytes = Convert.FromHexString(infoHash);
            var escapedHash = string.Join("", hashBytes.Select(b => $"%{b:X2}"));
            scrapeUrl += $"?info_hash={escapedHash}";

            _logger.Debug("HTTP scrape: {0}", scrapeUrl);

            var responseBytes = Policy.Execute(ct => Client.GetByteArrayAsync(scrapeUrl, ct).GetAwaiter().GetResult());
            var parser = new BencodeParser();
            var dict = parser.Parse<BDictionary>(responseBytes);

            if (dict.ContainsKey("failure reason"))
            {
                return new TrackerScrapeResponse
                {
                    Success = false,
                    FailureReason = ((BString)dict["failure reason"]).ToString()
                };
            }

            if (dict.ContainsKey("files"))
            {
                var files = (BDictionary)dict["files"];
                var first = files.Values.FirstOrDefault() as BDictionary;
                if (first != null)
                {
                    return new TrackerScrapeResponse
                    {
                        Success = true,
                        Complete = first.ContainsKey("complete") ? (int)((BNumber)first["complete"]).Value : 0,
                        Incomplete = first.ContainsKey("incomplete") ? (int)((BNumber)first["incomplete"]).Value : 0,
                        Downloaded = first.ContainsKey("downloaded") ? (int)((BNumber)first["downloaded"]).Value : 0
                    };
                }
            }

            return new TrackerScrapeResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HTTP scrape failed for {0}", trackerUrl);
            return new TrackerScrapeResponse
            {
                Success = false,
                FailureReason = ex.Message
            };
        }
    }

    private static string BuildAnnounceUrl(TrackerAnnounceRequest request)
    {
        var hashBytes = Convert.FromHexString(request.InfoHash);
        var escapedHash = string.Join("", hashBytes.Select(b => $"%{b:X2}"));
        var escapedPeerId = HttpUtility.UrlEncode(request.PeerId);

        return $"{request.TrackerUrl}" +
               $"?info_hash={escapedHash}" +
               $"&peer_id={escapedPeerId}" +
               $"&port={request.Port}" +
               $"&uploaded={request.Uploaded}" +
               $"&downloaded={request.Downloaded}" +
               $"&left={request.Left}" +
               $"&compact={(request.Compact ? 1 : 0)}" +
               $"&numwant={request.NumWant}" +
               (string.IsNullOrEmpty(request.Event) ? "" : $"&event={request.Event}");
    }
}
