using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;

namespace NzbDrone.Core.Torrents;

public interface ITorrentExporter
{
    byte[] ExportTorrent(Torrent torrent, List<TorrentFile> files = null, List<TrackerEntry> trackers = null);
}

public class TorrentExporter : ITorrentExporter
{
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly Logger _logger;

    public TorrentExporter(
        ITorrentFileService torrentFileService = null,
        ITrackerEntryService trackerEntryService = null,
        Logger logger = null)
    {
        _torrentFileService = torrentFileService;
        _trackerEntryService = trackerEntryService;
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public byte[] ExportTorrent(Torrent torrent, List<TorrentFile> files = null, List<TrackerEntry> trackers = null)
    {
        if (torrent == null)
        {
            return Array.Empty<byte>();
        }

        // If torrent.SourcePath exists and is a valid .torrent file on disk, return its bytes.
        if (!string.IsNullOrWhiteSpace(torrent.SourcePath) && File.Exists(torrent.SourcePath))
        {
            try
            {
                var bytes = File.ReadAllBytes(torrent.SourcePath);
                var parser = new BencodeParser();
                var parsedObj = parser.Parse(bytes);
                if (parsedObj is BDictionary rootDict && rootDict.ContainsKey("info"))
                {
                    return bytes;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "SourcePath {0} exists but is not a valid .torrent file; synthesizing metainfo instead.", torrent.SourcePath);
            }
        }

        // Dynamically synthesize canonical BEP 3 bencoded metainfo
        return SynthesizeTorrent(torrent, files, trackers);
    }

    private byte[] SynthesizeTorrent(Torrent torrent, List<TorrentFile> files, List<TrackerEntry> trackers)
    {
        var root = new BDictionary();

        // 1. Resolve trackers
        if (trackers == null && _trackerEntryService != null && torrent.Id > 0)
        {
            try
            {
                trackers = _trackerEntryService.GetByTorrentId(torrent.Id);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to retrieve tracker entries for torrent {0}", torrent.Id);
            }
        }

        var primaryTracker = !string.IsNullOrWhiteSpace(torrent.TrackerUrl)
            ? torrent.TrackerUrl
            : trackers?.OrderBy(t => t.Tier).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Url))?.Url;

        root["announce"] = new BString(primaryTracker ?? string.Empty);

        var announceList = new BList();
        if (trackers != null && trackers.Count > 0)
        {
            var tiers = trackers.Where(t => !string.IsNullOrWhiteSpace(t.Url))
                                .GroupBy(t => t.Tier)
                                .OrderBy(g => g.Key);
            foreach (var tier in tiers)
            {
                var tierList = new BList();
                foreach (var tr in tier)
                {
                    tierList.Add(new BString(tr.Url));
                }

                if (tierList.Count > 0)
                {
                    announceList.Add(tierList);
                }
            }
        }

        if (announceList.Count == 0 && !string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            var singleTier = new BList { new BString(torrent.TrackerUrl) };
            announceList.Add(singleTier);
        }

        root["announce-list"] = announceList;

        // 2. Comment, Created By, Creation Date
        root["comment"] = new BString(torrent.Comment ?? string.Empty);
        root["created by"] = new BString(!string.IsNullOrWhiteSpace(torrent.CreatedBy) ? torrent.CreatedBy : "Seedarr");

        long creationDateUnix;
        if (torrent.CreationDate.HasValue)
        {
            creationDateUnix = new DateTimeOffset(torrent.CreationDate.Value).ToUnixTimeSeconds();
        }
        else if (torrent.DateAdded != default)
        {
            creationDateUnix = new DateTimeOffset(torrent.DateAdded).ToUnixTimeSeconds();
        }
        else
        {
            creationDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        root["creation date"] = new BNumber(creationDateUnix);

        // 3. Info dictionary
        var info = new BDictionary();
        info["name"] = new BString(torrent.Name ?? "torrent");

        var pieceLength = torrent.PieceLength > 0 ? torrent.PieceLength : 262144;
        info["piece length"] = new BNumber(pieceLength);

        if (torrent.IsPrivate)
        {
            info["private"] = new BNumber(1);
        }

        // Resolve files
        if (files == null)
        {
            if (torrent.Files != null && torrent.Files.Count > 0)
            {
                files = torrent.Files;
            }
            else if (_torrentFileService != null && torrent.Id > 0)
            {
                try
                {
                    files = _torrentFileService.GetByTorrentId(torrent.Id);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to retrieve files for torrent {0}", torrent.Id);
                }
            }
        }

        var isMultiFile = files != null && files.Count > 0 &&
            (files.Count > 1 ||
             files[0].Path.Contains('/') ||
             files[0].Path.Contains('\\') ||
             (!string.IsNullOrWhiteSpace(torrent.Name) && !string.Equals(files[0].Path, torrent.Name, StringComparison.OrdinalIgnoreCase)));

        long totalSize = 0;
        if (isMultiFile)
        {
            var filesList = new BList();
            foreach (var file in files)
            {
                if (file == null)
                {
                    continue;
                }

                var fileDict = new BDictionary();
                fileDict["length"] = new BNumber(file.Size);

                var pathList = new BList();
                var rawPath = (file.Path ?? string.Empty).Replace('\\', '/');
                var parts = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 1 && string.Equals(parts[0], torrent.Name, StringComparison.OrdinalIgnoreCase))
                {
                    parts = parts.Skip(1).ToArray();
                }

                if (parts.Length == 0)
                {
                    pathList.Add(new BString(torrent.Name ?? "file"));
                }
                else
                {
                    foreach (var part in parts)
                    {
                        pathList.Add(new BString(part));
                    }
                }

                fileDict["path"] = pathList;
                filesList.Add(fileDict);
                totalSize += file.Size;
            }

            info["files"] = filesList;
            if (torrent.TotalSize > 0)
            {
                totalSize = torrent.TotalSize;
            }
        }
        else
        {
            totalSize = torrent.TotalSize;
            if (totalSize == 0 && files != null && files.Count > 0)
            {
                totalSize = files[0].Size;
            }

            info["length"] = new BNumber(totalSize);
        }

        // 4. Pieces
        var pieceCount = torrent.PieceCount;
        if (pieceCount <= 0)
        {
            pieceCount = totalSize > 0 ? (int)Math.Ceiling((double)totalSize / pieceLength) : 1;
        }

        if (pieceCount < 1)
        {
            pieceCount = 1;
        }

        byte[] piecesBytes;
        if (torrent.PieceHashes != null && torrent.PieceHashes.Length > 0)
        {
            var expectedLen = Math.Max(pieceCount * 20, (int)Math.Ceiling((double)torrent.PieceHashes.Length / 20) * 20);
            if (torrent.PieceHashes.Length < expectedLen)
            {
                piecesBytes = new byte[expectedLen];
                Array.Copy(torrent.PieceHashes, 0, piecesBytes, 0, torrent.PieceHashes.Length);
            }
            else
            {
                piecesBytes = torrent.PieceHashes;
            }
        }
        else
        {
            piecesBytes = new byte[pieceCount * 20];
        }

        info["pieces"] = new BString(piecesBytes);

        root["info"] = info;

        return root.EncodeAsBytes();
    }
}
