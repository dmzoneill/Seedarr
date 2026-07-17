using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;

namespace NzbDrone.Core.Torrents;

public class ParsedTorrent
{
    public string Name { get; set; }
    public string InfoHash { get; set; }
    public long TotalSize { get; set; }
    public int PieceCount { get; set; }
    public int PieceLength { get; set; }
    public string Comment { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? CreationDate { get; set; }
    public bool IsPrivate { get; set; }
    public string AnnounceUrl { get; set; }
    public List<List<string>> AnnounceList { get; set; }
    public List<ParsedTorrentFile> Files { get; set; }
}

public class ParsedTorrentFile
{
    public string Path { get; set; }
    public long Size { get; set; }
}

public interface ITorrentFileParser
{
    ParsedTorrent Parse(string filePath);
    ParsedTorrent Parse(Stream stream);
}

public class TorrentFileParser : ITorrentFileParser
{
    private readonly Logger _logger;

    public TorrentFileParser()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public ParsedTorrent Parse(string filePath)
    {
        _logger.Debug("Parsing torrent file: {0}", filePath);
        using var stream = File.OpenRead(filePath);
        return Parse(stream);
    }

    public ParsedTorrent Parse(Stream stream)
    {
        var parser = new BencodeParser();
        var torrent = parser.Parse<BDictionary>(stream);
        if (!torrent.ContainsKey("info") || torrent["info"] is not BDictionary info)
        {
            throw new InvalidOperationException("Malformed torrent file: missing or invalid 'info' dictionary.");
        }

        var pieceLength = ((BNumber)info["piece length"]).Value;
        var pieces = ((BString)info["pieces"]).Value;
        var pieceCount = pieces.Length / 20;

        var result = new ParsedTorrent
        {
            Name = ((BString)info["name"]).ToString(),
            InfoHash = InfoHashCalculator.Calculate(info),
            PieceLength = (int)pieceLength,
            PieceCount = pieceCount,
            Comment = torrent.ContainsKey("comment") ? ((BString)torrent["comment"]).ToString() : null,
            CreatedBy = torrent.ContainsKey("created by") ? ((BString)torrent["created by"]).ToString() : null,
            IsPrivate = info.ContainsKey("private") && ((BNumber)info["private"]).Value == 1,
            AnnounceUrl = torrent.ContainsKey("announce") ? ((BString)torrent["announce"]).ToString() : null,
            Files = new List<ParsedTorrentFile>()
        };

        if (torrent.ContainsKey("creation date"))
        {
            var unixTime = ((BNumber)torrent["creation date"]).Value;
            result.CreationDate = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
        }

        if (torrent.ContainsKey("announce-list"))
        {
            var announceList = (BList)torrent["announce-list"];
            result.AnnounceList = announceList
                .Select(tier => ((BList)tier).Select(url => ((BString)url).ToString()).ToList())
                .ToList();
        }

        if (info.ContainsKey("files"))
        {
            var files = (BList)info["files"];
            foreach (var file in files.Cast<BDictionary>())
            {
                var fileSize = ((BNumber)file["length"]).Value;
                var pathParts = ((BList)file["path"]).Select(p => ((BString)p).ToString());
                var filePath = string.Join("/", pathParts);

                result.Files.Add(new ParsedTorrentFile
                {
                    Path = filePath,
                    Size = fileSize
                });
            }
        }
        else
        {
            var length = ((BNumber)info["length"]).Value;
            result.Files.Add(new ParsedTorrentFile
            {
                Path = result.Name,
                Size = length
            });
        }

        result.TotalSize = result.Files.Sum(f => f.Size);

        return result;
    }
}
