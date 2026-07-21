using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;
using NzbDrone.Core.Exceptions;

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
        try
        {
            var parser = new BencodeParser();
            var torrent = parser.Parse<BDictionary>(stream);

            if (!torrent.ContainsKey("info") || torrent["info"] is not BDictionary info)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'info' dictionary.");
            }

            if (!info.ContainsKey("piece length") || info["piece length"] is not BNumber pieceLengthNum)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'piece length'.");
            }

            if (!info.ContainsKey("pieces") || info["pieces"] is not BString piecesStr)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'pieces'.");
            }

            if (!info.ContainsKey("name") || info["name"] is not BString nameStr)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'name'.");
            }

            var pieceCount = piecesStr.Value.Length / 20;

            var result = new ParsedTorrent
            {
                Name = nameStr.ToString(),
                InfoHash = InfoHashCalculator.Calculate(info),
                PieceLength = (int)pieceLengthNum.Value,
                PieceCount = pieceCount,
                Comment = torrent.ContainsKey("comment") ? (torrent["comment"] as BString)?.ToString() : null,
                CreatedBy = torrent.ContainsKey("created by") ? (torrent["created by"] as BString)?.ToString() : null,
                IsPrivate = info.ContainsKey("private") && (info["private"] as BNumber)?.Value == 1,
                AnnounceUrl = torrent.ContainsKey("announce") ? (torrent["announce"] as BString)?.ToString() : null,
                Files = new List<ParsedTorrentFile>()
            };

            if (torrent.ContainsKey("creation date") && torrent["creation date"] is BNumber creationDateNum)
            {
                result.CreationDate = DateTimeOffset.FromUnixTimeSeconds(creationDateNum.Value).UtcDateTime;
            }

            if (torrent.ContainsKey("announce-list") && torrent["announce-list"] is BList announceList)
            {
                result.AnnounceList = announceList
                    .OfType<BList>()
                    .Select(tier => tier.OfType<BString>().Select(url => url.ToString()).ToList())
                    .ToList();
            }

            if (info.ContainsKey("files") && info["files"] is BList files)
            {
                foreach (var fileObj in files)
                {
                    if (fileObj is not BDictionary file)
                    {
                        throw new InvalidTorrentFileException("Malformed torrent file: file list entry is not a dictionary.");
                    }

                    if (!file.ContainsKey("length") || file["length"] is not BNumber fileLengthNum)
                    {
                        throw new InvalidTorrentFileException("Malformed torrent file: file entry missing or invalid 'length'.");
                    }

                    if (!file.ContainsKey("path") || file["path"] is not BList pathList)
                    {
                        throw new InvalidTorrentFileException("Malformed torrent file: file entry missing or invalid 'path'.");
                    }

                    var pathParts = pathList.OfType<BString>().Select(p => p.ToString());

                    result.Files.Add(new ParsedTorrentFile
                    {
                        Path = string.Join("/", pathParts),
                        Size = fileLengthNum.Value
                    });
                }
            }
            else
            {
                if (!info.ContainsKey("length") || info["length"] is not BNumber lengthNum)
                {
                    throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'length' for single-file torrent.");
                }

                result.Files.Add(new ParsedTorrentFile
                {
                    Path = result.Name,
                    Size = lengthNum.Value
                });
            }

            result.TotalSize = result.Files.Sum(f => f.Size);

            return result;
        }
        catch (InvalidTorrentFileException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidTorrentFileException($"Failed to parse torrent file: {ex.Message}", ex);
        }
    }
}
