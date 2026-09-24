using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.WebSeeds;

namespace NzbDrone.Core.Torrents;

public class ParsedTorrent
{
    public string Name { get; set; }
    public string InfoHash { get; set; }
    public string InfoHashV2 { get; set; }
    public int MetaVersion { get; set; } = 1;
    public long TotalSize { get; set; }
    public long ContentSize { get; set; }
    public int PieceCount { get; set; }
    public int PieceLength { get; set; }
    public string Comment { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? CreationDate { get; set; }
    public bool IsPrivate { get; set; }
    public string AnnounceUrl { get; set; }
    public List<List<string>> AnnounceList { get; set; }
    public List<ParsedTorrentFile> Files { get; set; }
    public List<string> HttpSeeds { get; set; } = new();
    public List<string> UrlList { get; set; } = new();
    public List<string> WebSeeds
    {
        get => UrlList;
        set => UrlList = value;
    }

    public Dictionary<string, byte[]> PieceLayers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Pieces
    {
        get => PieceHashes;
        set => PieceHashes = value;
    }

    public byte[] PieceHashes { get; set; }
}

public class ParsedTorrentFile
{
    public string Path { get; set; }
    public long Size { get; set; }
    public bool IsPaddingFile { get; set; }
    public byte[] PiecesRoot { get; set; }

    public string PiecesRootHex
    {
        get => PiecesRoot != null ? Convert.ToHexString(PiecesRoot).ToLowerInvariant() : null;
        set => PiecesRoot = value != null ? Convert.FromHexString(value) : null;
    }
}

public interface ITorrentFileParser
{
    ParsedTorrent Parse(string filePath);
    ParsedTorrent Parse(Stream stream);
    ParsedTorrent Parse(byte[] bytes);
}

public class TorrentFileParser : ITorrentFileParser
{
    private const long MaxTorrentStreamBytes = 10 * 1024 * 1024; // 10 MiB
    private const int MaxPermittedPieces = 500000;
    private const int MaxRecursionDepth = 32;
    private const long MinPieceLength = 16384; // 16 KiB
    private const long MaxPieceLength = 67108864; // 64 MiB

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Logger _logger;

    public TorrentFileParser()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public static string ResolveWebSeedUrl(string baseUrl, string torrentName, string relativeFilePath = null)
    {
        return WebSeedUrlResolver.ResolveWebSeedUrl(baseUrl, torrentName, relativeFilePath);
    }

    public ParsedTorrent Parse(string filePath)
    {
        _logger.Debug("Parsing torrent file: {0}", filePath);
        using var stream = File.OpenRead(filePath);
        return Parse(stream);
    }

    public ParsedTorrent Parse(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (stream.CanSeek && stream.Length > MaxTorrentStreamBytes)
        {
            throw new InvalidTorrentFileException("Torrent file exceeds maximum permitted size of 10 MiB.");
        }

        using var memoryStream = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > MaxTorrentStreamBytes)
            {
                throw new InvalidTorrentFileException("Torrent file exceeds maximum permitted size of 10 MiB.");
            }

            memoryStream.Write(buffer, 0, bytesRead);
        }

        return Parse(memoryStream.ToArray());
    }

    public ParsedTorrent Parse(byte[] bytes)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        if (bytes.Length > MaxTorrentStreamBytes)
        {
            throw new InvalidTorrentFileException("Torrent file exceeds maximum permitted size of 10 MiB.");
        }

        try
        {
            var parser = new BencodeParser();
            var torrent = parser.Parse<BDictionary>(bytes);

            ValidateRecursionDepth(torrent);

            if (!torrent.ContainsKey("info") || torrent["info"] is not BDictionary info)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'info' dictionary.");
            }

            if (!info.ContainsKey("piece length") || info["piece length"] is not BNumber pieceLengthNum)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'piece length'.");
            }

            var pieceLength = pieceLengthNum.Value;
            if (pieceLength <= 0 || pieceLength > int.MaxValue || pieceLength < MinPieceLength || pieceLength > MaxPieceLength || (pieceLength & (pieceLength - 1)) != 0)
            {
                throw new InvalidTorrentFileException($"Invalid piece length: {pieceLength}. Must be a power of two between 16 KiB and 64 MiB.");
            }

            var hasPiecesKey = info.ContainsKey("pieces");
            BString piecesStr = null;
            if (hasPiecesKey)
            {
                if (info["pieces"] is not BString ps)
                {
                    throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'pieces'.");
                }

                piecesStr = ps;
            }

            var hasFileTreeKey = info.ContainsKey("file tree");
            BDictionary fileTree = null;
            if (hasFileTreeKey)
            {
                if (info["file tree"] is not BDictionary ft)
                {
                    throw new InvalidTorrentFileException("Malformed torrent file: 'file tree' is not a dictionary.");
                }

                fileTree = ft;
            }

            var hasMetaVersion2 = info.ContainsKey("meta version") && (info["meta version"] as BNumber)?.Value == 2;

            if (!hasPiecesKey && !hasFileTreeKey)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'pieces'.");
            }

            int metaVersion;
            if (hasPiecesKey && (hasFileTreeKey || hasMetaVersion2))
            {
                metaVersion = 3;
            }
            else if (hasFileTreeKey || hasMetaVersion2)
            {
                metaVersion = 2;
            }
            else
            {
                metaVersion = 1;
            }

            var pieceCount = 0;
            if (piecesStr != null)
            {
                if (piecesStr.Value.Length == 0)
                {
                    throw new InvalidTorrentFileException($"Malformed torrent file: 'pieces' is empty. Piece count 0 exceeds maximum permitted limit of {MaxPermittedPieces}.");
                }

                if (piecesStr.Value.Length % 20 != 0)
                {
                    throw new InvalidTorrentFileException($"Invalid 'pieces' length: byte array length {piecesStr.Value.Length} must be an exact multiple of 20 bytes.");
                }

                pieceCount = piecesStr.Value.Length / 20;
                if (pieceCount > MaxPermittedPieces)
                {
                    throw new InvalidTorrentFileException($"Piece count {pieceCount} exceeds maximum permitted limit of {MaxPermittedPieces}.");
                }
            }

            var torrentName = GetStringWithUtf8Fallback(info, "name") ?? GetStringWithUtf8Fallback(torrent, "name");
            if (torrentName == null)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'name'.");
            }

            torrentName = torrentName.Normalize(NormalizationForm.FormC);

            string announceUrl = null;
            if (torrent.ContainsKey("announce") && torrent["announce"] is BString mainAnnounceStr)
            {
                var s = DecodeBString(mainAnnounceStr)?.Trim();
                if (!string.IsNullOrEmpty(s))
                {
                    announceUrl = s;
                }
            }
            else if (info.ContainsKey("announce") && info["announce"] is BString infoAnnounceStr)
            {
                var s = DecodeBString(infoAnnounceStr)?.Trim();
                if (!string.IsNullOrEmpty(s))
                {
                    announceUrl = s;
                }
            }

            List<List<string>> announceListParsed = null;

            if (torrent.ContainsKey("announce-list") && torrent["announce-list"] is BList announceList)
            {
                announceListParsed = new List<List<string>>();
                ExtractAnnounceList(announceList, announceListParsed);
            }
            else if (info.ContainsKey("announce-list") && info["announce-list"] is BList infoAnnounceList)
            {
                announceListParsed = new List<List<string>>();
                ExtractAnnounceList(infoAnnounceList, announceListParsed);
            }

            if (announceUrl == null && announceListParsed != null && announceListParsed.Count > 0 && announceListParsed[0].Count > 0)
            {
                announceUrl = announceListParsed[0][0];
            }

            var hasRawInfo = TryExtractRawInfoBytes(bytes, out var rawInfoBytes);
            string infoHash = null;
            string infoHashV2 = null;

            if (metaVersion == 1)
            {
                infoHash = hasRawInfo
                    ? InfoHashCalculator.Calculate(rawInfoBytes)
                    : InfoHashCalculator.Calculate(info);
            }
            else if (metaVersion == 3)
            {
                if (hasRawInfo)
                {
                    InfoHashCalculator.Calculate(rawInfoBytes, out infoHash, out infoHashV2);
                }
                else
                {
                    InfoHashCalculator.Calculate(info, out infoHash, out infoHashV2);
                }
            }
            else
            {
                infoHashV2 = hasRawInfo
                    ? InfoHashCalculator.CalculateV2(rawInfoBytes)
                    : InfoHashCalculator.CalculateV2(info);
            }

            var httpSeeds = new List<string>();
            ExtractUrlList(torrent, "httpseeds", httpSeeds);
            ExtractUrlList(info, "httpseeds", httpSeeds);

            var urlList = new List<string>();
            ExtractUrlList(torrent, "url-list", urlList);
            ExtractUrlList(info, "url-list", urlList);

            var result = new ParsedTorrent
            {
                Name = torrentName,
                InfoHash = infoHash,
                InfoHashV2 = infoHashV2,
                MetaVersion = metaVersion,
                PieceLength = (int)pieceLengthNum.Value,
                PieceCount = pieceCount,
                Pieces = piecesStr?.Value.ToArray(),
                PieceHashes = piecesStr?.Value.ToArray(),
                Comment = GetStringWithUtf8Fallback(torrent, "comment"),
                CreatedBy = GetStringWithUtf8Fallback(torrent, "created by"),
                IsPrivate = info.ContainsKey("private") && (info["private"] as BNumber)?.Value == 1,
                AnnounceUrl = announceUrl,
                AnnounceList = announceListParsed,
                HttpSeeds = httpSeeds,
                UrlList = urlList,
                Files = new List<ParsedTorrentFile>()
            };

            if (torrent.ContainsKey("creation date") && torrent["creation date"] is BNumber creationDateNum)
            {
                result.CreationDate = DateTimeOffset.FromUnixTimeSeconds(creationDateNum.Value).UtcDateTime;
            }

            if (fileTree != null)
            {
                var currentPath = new List<string>();
                TraverseFileTree(fileTree, currentPath, result.Files);

                if (result.Files.Count == 0)
                {
                    throw new InvalidTorrentFileException("Malformed torrent file: 'file tree' contains no files.");
                }

                if (metaVersion == 2)
                {
                    var calculatedPieceCount = result.Files.Sum(f => f.Size == 0 ? 0L : (f.Size + pieceLength - 1) / pieceLength);
                    if (calculatedPieceCount > MaxPermittedPieces)
                    {
                        throw new InvalidTorrentFileException($"Piece count {calculatedPieceCount} exceeds maximum permitted limit of {MaxPermittedPieces}.");
                    }

                    result.PieceCount = (int)calculatedPieceCount;
                }
            }
            else if (info.ContainsKey("files") && info["files"] is BList files)
            {
                var rootDirName = SanitizeDirectoryName(result.Name);
                var fileIndex = 0;

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

                    if (fileLengthNum.Value < 0)
                    {
                        throw new InvalidTorrentFileException($"Malformed torrent file: negative file length {fileLengthNum.Value}.");
                    }

                    var pathList = GetPathListWithUtf8Fallback(file);
                    if (pathList == null)
                    {
                        throw new InvalidTorrentFileException("Malformed torrent file: file entry missing or invalid 'path'.");
                    }

                    var sanitizedSegments = SanitizePathSegments(pathList.OfType<BString>(), fileIndex);
                    var relativePath = string.Join("/", sanitizedSegments);
                    if (string.IsNullOrWhiteSpace(relativePath))
                    {
                        relativePath = $"file_{fileIndex}";
                    }

                    var fullRelativePath = string.IsNullOrEmpty(rootDirName)
                        ? relativePath
                        : $"{rootDirName}/{relativePath}";

                    fullRelativePath = fullRelativePath.Normalize(NormalizationForm.FormC);

                    var isPadding = IsPadding(file, fullRelativePath);

                    result.Files.Add(new ParsedTorrentFile
                    {
                        Path = fullRelativePath,
                        Size = fileLengthNum.Value,
                        IsPaddingFile = isPadding
                    });

                    fileIndex++;
                }
            }
            else
            {
                if (!info.ContainsKey("length") || info["length"] is not BNumber lengthNum)
                {
                    throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'length' for single-file torrent.");
                }

                if (lengthNum.Value < 0)
                {
                    throw new InvalidTorrentFileException($"Malformed torrent file: negative file length {lengthNum.Value}.");
                }

                string singleFilePath;
                var pathList = GetPathListWithUtf8Fallback(info) ?? GetPathListWithUtf8Fallback(torrent);
                if (pathList != null)
                {
                    var sanitizedSegments = SanitizePathSegments(pathList.OfType<BString>(), 0);
                    singleFilePath = string.Join("/", sanitizedSegments);
                }
                else
                {
                    singleFilePath = SanitizeSingleFilePath(result.Name, 0);
                }

                if (string.IsNullOrWhiteSpace(singleFilePath))
                {
                    singleFilePath = "file_0";
                }

                result.Files.Add(new ParsedTorrentFile
                {
                    Path = singleFilePath.Normalize(NormalizationForm.FormC),
                    Size = lengthNum.Value,
                    IsPaddingFile = false
                });
            }

            result.TotalSize = result.Files.Sum(f => f.Size);
            result.ContentSize = result.Files.Where(f => !f.IsPaddingFile).Sum(f => f.Size);

            ExtractPieceLayers(torrent, result.PieceLayers);
            if (result.PieceLayers.Count == 0)
            {
                ExtractPieceLayers(info, result.PieceLayers);
            }

            return result;
        }
        catch (Exception ex) when (ex is not InvalidTorrentFileException)
        {
            throw new InvalidTorrentFileException($"Failed to parse torrent file: {ex.Message}", ex);
        }
    }

    private static void TraverseFileTree(BDictionary tree, List<string> currentPath, List<ParsedTorrentFile> files, int depth = 0)
    {
        if (depth > MaxRecursionDepth)
        {
            throw new InvalidTorrentFileException($"Torrent file exceeds maximum recursion depth limit of {MaxRecursionDepth}.");
        }

        if (tree.ContainsKey(""))
        {
            if (tree[""] is not BDictionary fileNode)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: file tree leaf node is not a dictionary.");
            }

            if (!fileNode.ContainsKey("length") || fileNode["length"] is not BNumber lengthNum)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: file tree leaf node missing or invalid 'length'.");
            }

            if (lengthNum.Value < 0)
            {
                throw new InvalidTorrentFileException($"Malformed torrent file: negative file length {lengthNum.Value}.");
            }

            byte[] piecesRoot = null;
            if (fileNode.TryGetValue("pieces root", out var piecesRootObj))
            {
                if (piecesRootObj is not BString piecesRootStr)
                {
                    throw new InvalidTorrentFileException("Malformed torrent file: file tree 'pieces root' is not a byte string.");
                }

                if (piecesRootStr.Value.Length != 32)
                {
                    throw new InvalidTorrentFileException("Malformed torrent file: 'pieces root' must be 32 bytes.");
                }

                piecesRoot = piecesRootStr.Value.ToArray();
            }
            else if (lengthNum.Value > 0)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: file tree missing 'pieces root' for non-empty file.");
            }

            var relativePath = currentPath.Count > 0 ? string.Join("/", currentPath) : null;
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new InvalidTorrentFileException("Malformed torrent file: empty file path in file tree.");
            }

            files.Add(new ParsedTorrentFile
            {
                Path = relativePath.Normalize(NormalizationForm.FormC),
                Size = lengthNum.Value,
                PiecesRoot = piecesRoot,
                IsPaddingFile = IsPadding(fileNode, relativePath)
            });
        }

        foreach (var kvp in tree)
        {
            var rawSegment = DecodeBString(kvp.Key);
            if (string.IsNullOrEmpty(rawSegment))
            {
                continue;
            }

            var parts = rawSegment.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed == "..")
                {
                    throw new InvalidTorrentFileException($"Path traversal attempt detected in file tree: '{rawSegment}'.");
                }
            }

            var segment = rawSegment.Replace('\\', '/').Trim('/', '\\');
            if (string.IsNullOrEmpty(segment) || segment == ".")
            {
                continue;
            }

            if (kvp.Value is BDictionary subDict)
            {
                currentPath.Add(segment.Normalize(NormalizationForm.FormC));
                TraverseFileTree(subDict, currentPath, files, depth + 1);
                currentPath.RemoveAt(currentPath.Count - 1);
            }
            else
            {
                throw new InvalidTorrentFileException("Malformed torrent file: file tree node is not a dictionary.");
            }
        }
    }

    private static void ExtractPieceLayers(BDictionary dict, Dictionary<string, byte[]> pieceLayers)
    {
        if (dict == null || !dict.ContainsKey("piece layers") || dict["piece layers"] is not BDictionary layersDict)
        {
            return;
        }

        foreach (var kvp in layersDict)
        {
            if (kvp.Value is not BString hashesStr)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: 'piece layers' entry value must be a byte string.");
            }

            if (hashesStr.Value.Length % 32 != 0)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: piece layer hashes length must be a multiple of 32 bytes.");
            }

            var keySpan = kvp.Key.Value.Span;
            string hexKey;
            if (keySpan.Length == 32)
            {
                hexKey = Convert.ToHexString(keySpan).ToLowerInvariant();
            }
            else if (keySpan.Length == 64)
            {
                hexKey = (DecodeBString(kvp.Key) ?? Convert.ToHexString(keySpan)).ToLowerInvariant();
            }
            else
            {
                hexKey = Convert.ToHexString(keySpan).ToLowerInvariant();
            }

            pieceLayers[hexKey] = hashesStr.Value.ToArray();
        }
    }

    private static void ExtractAnnounceList(BList announceList, List<List<string>> targetList)
    {
        foreach (var item in announceList)
        {
            if (item is BList tierList)
            {
                var tierUrls = tierList.OfType<BString>()
                    .Select(u => DecodeBString(u)?.Trim())
                    .Where(u => !string.IsNullOrEmpty(u))
                    .ToList();
                if (tierUrls.Count > 0)
                {
                    targetList.Add(tierUrls);
                }
            }
            else if (item is BString singleUrlStr)
            {
                var u = DecodeBString(singleUrlStr)?.Trim();
                if (!string.IsNullOrEmpty(u))
                {
                    targetList.Add(new List<string> { u });
                }
            }
        }
    }

    private static void ExtractUrlList(BDictionary dict, string key, List<string> target)
    {
        if (!dict.ContainsKey(key))
        {
            return;
        }

        if (dict[key] is BString bString)
        {
            var url = DecodeBString(bString)?.Trim();
            if (WebSeedUrlResolver.IsValidWebSeedUrl(url, out var validUrl) && !target.Contains(validUrl, StringComparer.OrdinalIgnoreCase))
            {
                target.Add(validUrl);
            }
        }
        else if (dict[key] is BList bList)
        {
            ExtractUrlsFromList(bList, target);
        }
    }

    private static void ExtractUrlsFromList(BList bList, List<string> target)
    {
        foreach (var item in bList)
        {
            if (item is BString itemStr)
            {
                var url = DecodeBString(itemStr)?.Trim();
                if (WebSeedUrlResolver.IsValidWebSeedUrl(url, out var validUrl) && !target.Contains(validUrl, StringComparer.OrdinalIgnoreCase))
                {
                    target.Add(validUrl);
                }
            }
            else if (item is BList nestedList)
            {
                ExtractUrlsFromList(nestedList, target);
            }
        }
    }

    private static string DecodeBString(BString bString)
    {
        if (bString == null)
        {
            return null;
        }

        try
        {
            return StrictUtf8.GetString(bString.Value.Span);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bString.Value.Span);
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1.GetString(bString.Value.Span);
        }
    }

    private static string GetStringWithUtf8Fallback(BDictionary dict, string primaryKey)
    {
        if (dict == null)
        {
            return null;
        }

        var utf8Key1 = primaryKey + ".utf-8";
        var utf8Key2 = primaryKey + ".utf8";

        if (dict.TryGetValue(utf8Key1, out var val1) && val1 is BString bs1)
        {
            return DecodeBString(bs1);
        }

        if (dict.TryGetValue(utf8Key2, out var val2) && val2 is BString bs2)
        {
            return DecodeBString(bs2);
        }

        if (dict.TryGetValue(primaryKey, out var val) && val is BString bs)
        {
            return DecodeBString(bs);
        }

        return null;
    }

    private static BList GetPathListWithUtf8Fallback(BDictionary dict)
    {
        if (dict == null)
        {
            return null;
        }

        if (dict.TryGetValue("path.utf-8", out var val1))
        {
            if (val1 is BList bl1)
            {
                return bl1;
            }

            if (val1 is BString bs1)
            {
                return new BList { bs1 };
            }
        }

        if (dict.TryGetValue("path.utf8", out var val2))
        {
            if (val2 is BList bl2)
            {
                return bl2;
            }

            if (val2 is BString bs2)
            {
                return new BList { bs2 };
            }
        }

        if (dict.TryGetValue("path", out var val))
        {
            if (val is BList bl)
            {
                return bl;
            }

            if (val is BString bs)
            {
                return new BList { bs };
            }
        }

        if (dict.TryGetValue("name.utf-8", out var nVal1) && nVal1 is BString nbs1)
        {
            return new BList { nbs1 };
        }

        if (dict.TryGetValue("name.utf8", out var nVal2) && nVal2 is BString nbs2)
        {
            return new BList { nbs2 };
        }

        if (dict.TryGetValue("name", out var nVal) && nVal is BString nbs)
        {
            return new BList { nbs };
        }

        return null;
    }

    private static readonly HashSet<char> IllegalFileNameChars = new()
    {
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    };

    private static List<string> SanitizePathSegments(IEnumerable<BString> rawSegments, int fileIndex = 0)
    {
        var sanitizedSegments = new List<string>();

        if (rawSegments != null)
        {
            foreach (var bString in rawSegments)
            {
                var decoded = DecodeBString(bString);
                if (string.IsNullOrWhiteSpace(decoded))
                {
                    continue;
                }

                var parts = decoded.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (TrySanitizeSegment(part, decoded, out var sanitized))
                    {
                        sanitizedSegments.Add(sanitized);
                    }
                }
            }
        }

        if (sanitizedSegments.Count == 0)
        {
            sanitizedSegments.Add($"file_{fileIndex}");
        }

        return sanitizedSegments;
    }

    private static string SanitizeDirectoryName(string dirName)
    {
        if (string.IsNullOrWhiteSpace(dirName))
        {
            return null;
        }

        var parts = dirName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanParts = new List<string>();

        foreach (var part in parts)
        {
            if (TrySanitizeSegment(part, dirName, out var sanitized))
            {
                cleanParts.Add(sanitized);
            }
        }

        return cleanParts.Count > 0 ? string.Join("/", cleanParts) : null;
    }

    private static string SanitizeSingleFilePath(string fileName, int fileIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return $"file_{fileIndex}";
        }

        var parts = fileName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanParts = new List<string>();

        foreach (var part in parts)
        {
            if (TrySanitizeSegment(part, fileName, out var sanitized))
            {
                cleanParts.Add(sanitized);
            }
        }

        return cleanParts.Count > 0 ? string.Join("/", cleanParts) : $"file_{fileIndex}";
    }

    private static bool TrySanitizeSegment(string rawPart, string contextForException, out string sanitized)
    {
        sanitized = null;

        var trimmed = rawPart?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == ".")
        {
            return false;
        }

        if (trimmed == "..")
        {
            throw new InvalidTorrentFileException($"Path traversal attempt detected in torrent file path: '{contextForException}'.");
        }

        // Strip drive letters (e.g., "C:" or "C:file.txt")
        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            trimmed = trimmed.Substring(2).TrimStart('/', '\\').Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == ".")
            {
                return false;
            }

            if (trimmed == "..")
            {
                throw new InvalidTorrentFileException($"Path traversal attempt detected in torrent file path: '{contextForException}'.");
            }
        }

        // Sanitize invalid chars, control chars, and colon (to prevent Windows Alternate Data Streams)
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (!char.IsControl(c) && !IllegalFileNameChars.Contains(c) && !Path.GetInvalidFileNameChars().Contains(c))
            {
                sb.Append(c);
            }
        }

        var clean = sb.ToString().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(clean) || clean == ".")
        {
            return false;
        }

        if (clean == "..")
        {
            throw new InvalidTorrentFileException($"Path traversal attempt detected in torrent file path: '{contextForException}'.");
        }

        if (PathSanitizer.IsWindowsReservedName(clean))
        {
            return false;
        }

        sanitized = clean.Normalize(NormalizationForm.FormC);
        return true;
    }

    private static bool IsPadding(BDictionary fileDict, string path)
    {
        if (fileDict.TryGetValue("attr", out var attrVal) && attrVal is BString attrStr && attrStr.ToString().Contains('p'))
        {
            return true;
        }

        var normalizedPath = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedPath);
        return fileName.StartsWith("_____padding_file_", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(".pad/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/.pad/", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateRecursionDepth(IBObject obj, int currentDepth = 0)
    {
        if (currentDepth > MaxRecursionDepth)
        {
            throw new InvalidTorrentFileException($"Torrent file exceeds maximum recursion depth limit of {MaxRecursionDepth}.");
        }

        if (obj is BDictionary dict)
        {
            foreach (var kvp in dict)
            {
                ValidateRecursionDepth(kvp.Value, currentDepth + 1);
            }
        }
        else if (obj is BList list)
        {
            foreach (var item in list)
            {
                ValidateRecursionDepth(item, currentDepth + 1);
            }
        }
    }

    internal static bool TryExtractRawInfoBytes(ReadOnlySpan<byte> bytes, out ReadOnlySpan<byte> rawInfoBytes)
    {
        rawInfoBytes = default;

        if (bytes.IsEmpty || bytes[0] != (byte)'d')
        {
            return false;
        }

        var index = 1;
        while (index < bytes.Length && bytes[index] != (byte)'e')
        {
            if (bytes[index] < (byte)'0' || bytes[index] > (byte)'9')
            {
                return false;
            }

            var keyLen = 0;
            while (index < bytes.Length && bytes[index] >= (byte)'0' && bytes[index] <= (byte)'9')
            {
                var digit = bytes[index] - (byte)'0';
                if (keyLen > (int.MaxValue - digit) / 10)
                {
                    return false;
                }

                keyLen = (keyLen * 10) + digit;
                index++;
            }

            if (index >= bytes.Length || bytes[index] != (byte)':')
            {
                return false;
            }

            index++; // skip ':'
            if (bytes.Length - index < keyLen)
            {
                return false;
            }

            var key = bytes.Slice(index, keyLen);
            index += keyLen;

            var isInfo = key.SequenceEqual("info"u8);
            if (isInfo)
            {
                if (index >= bytes.Length || bytes[index] != (byte)'d')
                {
                    return false;
                }

                var infoStart = index;
                if (!TrySkipContainer(bytes, ref index))
                {
                    return false;
                }

                rawInfoBytes = bytes.Slice(infoStart, index - infoStart);
                return true;
            }

            if (!TrySkipElement(bytes, ref index))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TrySkipElement(ReadOnlySpan<byte> bytes, ref int index)
    {
        if (index >= bytes.Length)
        {
            return false;
        }

        var b = bytes[index];
        if (b == (byte)'d' || b == (byte)'l')
        {
            return TrySkipContainer(bytes, ref index);
        }

        if (b == (byte)'i')
        {
            index++;
            while (index < bytes.Length && bytes[index] != (byte)'e')
            {
                index++;
            }

            if (index >= bytes.Length || bytes[index] != (byte)'e')
            {
                return false;
            }

            index++;
            return true;
        }

        if (b >= (byte)'0' && b <= (byte)'9')
        {
            var strLen = 0;
            while (index < bytes.Length && bytes[index] >= (byte)'0' && bytes[index] <= (byte)'9')
            {
                var digit = bytes[index] - (byte)'0';
                if (strLen > (int.MaxValue - digit) / 10)
                {
                    return false;
                }

                strLen = (strLen * 10) + digit;
                index++;
            }

            if (index >= bytes.Length || bytes[index] != (byte)':')
            {
                return false;
            }

            index++; // skip ':'
            if (bytes.Length - index < strLen)
            {
                return false;
            }

            index += strLen;
            return true;
        }

        return false;
    }

    private static bool TrySkipContainer(ReadOnlySpan<byte> bytes, ref int index)
    {
        if (index >= bytes.Length || (bytes[index] != (byte)'d' && bytes[index] != (byte)'l'))
        {
            return false;
        }

        var depth = 0;
        while (index < bytes.Length)
        {
            var b = bytes[index];
            if (b == (byte)'d' || b == (byte)'l')
            {
                depth++;
                if (depth > MaxRecursionDepth)
                {
                    return false;
                }

                index++;
            }
            else if (b == (byte)'e')
            {
                depth--;
                index++;
                if (depth == 0)
                {
                    return true;
                }
            }
            else if (b == (byte)'i')
            {
                index++;
                while (index < bytes.Length && bytes[index] != (byte)'e')
                {
                    index++;
                }

                if (index >= bytes.Length || bytes[index] != (byte)'e')
                {
                    return false;
                }

                index++;
            }
            else if (b >= (byte)'0' && b <= (byte)'9')
            {
                var strLen = 0;
                while (index < bytes.Length && bytes[index] >= (byte)'0' && bytes[index] <= (byte)'9')
                {
                    var digit = bytes[index] - (byte)'0';
                    if (strLen > (int.MaxValue - digit) / 10)
                    {
                        return false;
                    }

                    strLen = (strLen * 10) + digit;
                    index++;
                }

                if (index >= bytes.Length || bytes[index] != (byte)':')
                {
                    return false;
                }

                index++; // skip ':'
                if (bytes.Length - index < strLen)
                {
                    return false;
                }

                index += strLen;
            }
            else
            {
                return false;
            }
        }

        return false;
    }
}
