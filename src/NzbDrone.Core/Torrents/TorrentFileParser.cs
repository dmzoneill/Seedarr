using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
}

public class ParsedTorrentFile
{
    public string Path { get; set; }
    public long Size { get; set; }
    public bool IsPaddingFile { get; set; }
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
            if (pieceLength < MinPieceLength || pieceLength > MaxPieceLength || (pieceLength & (pieceLength - 1)) != 0)
            {
                throw new InvalidTorrentFileException($"Invalid piece length: {pieceLength}. Must be a power of two between 16 KiB and 64 MiB.");
            }

            if (!info.ContainsKey("pieces") || info["pieces"] is not BString piecesStr)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'pieces'.");
            }

            var pieceCount = piecesStr.Value.Length / 20;
            if (pieceCount <= 0 || pieceCount > MaxPermittedPieces)
            {
                throw new InvalidTorrentFileException($"Piece count {pieceCount} exceeds maximum permitted limit of {MaxPermittedPieces}.");
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

            var infoHash = TryExtractRawInfoBytes(bytes, out var rawInfoBytes)
                ? InfoHashCalculator.Calculate(rawInfoBytes)
                : InfoHashCalculator.Calculate(info);

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
                PieceLength = (int)pieceLengthNum.Value,
                PieceCount = pieceCount,
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

            if (info.ContainsKey("files") && info["files"] is BList files)
            {
                var rootDirName = result.Name?.Replace('\\', '/').Trim('/', '\\')?.Normalize(NormalizationForm.FormC);

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

                    var pathParts = pathList.OfType<BString>()
                        .Select(p => DecodeBString(p).Replace('\\', '/').Trim('/', '\\'))
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(p => p.Normalize(NormalizationForm.FormC));

                    var relativePath = string.Join("/", pathParts);
                    var fullRelativePath = string.IsNullOrEmpty(rootDirName)
                        ? relativePath
                        : string.IsNullOrEmpty(relativePath)
                            ? rootDirName
                            : $"{rootDirName}/{relativePath}";

                    fullRelativePath = fullRelativePath.Normalize(NormalizationForm.FormC);

                    var isPadding = IsPadding(file, fullRelativePath);

                    result.Files.Add(new ParsedTorrentFile
                    {
                        Path = fullRelativePath,
                        Size = fileLengthNum.Value,
                        IsPaddingFile = isPadding
                    });
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

                var singleFilePath = result.Name;
                var pathList = GetPathListWithUtf8Fallback(info) ?? GetPathListWithUtf8Fallback(torrent);
                if (pathList != null)
                {
                    var pathParts = pathList.OfType<BString>()
                        .Select(p => DecodeBString(p).Replace('\\', '/').Trim('/', '\\'))
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(p => p.Normalize(NormalizationForm.FormC));

                    var resolvedPath = string.Join("/", pathParts);
                    if (!string.IsNullOrWhiteSpace(resolvedPath))
                    {
                        singleFilePath = resolvedPath;
                    }
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
            if (!string.IsNullOrWhiteSpace(url) && !target.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                target.Add(url);
            }
        }
        else if (dict[key] is BList bList)
        {
            foreach (var item in bList.OfType<BString>())
            {
                var url = DecodeBString(item)?.Trim();
                if (!string.IsNullOrWhiteSpace(url) && !target.Contains(url, StringComparer.OrdinalIgnoreCase))
                {
                    target.Add(url);
                }
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
