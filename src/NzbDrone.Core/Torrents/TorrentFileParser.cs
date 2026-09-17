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

        try
        {
            var parseStream = stream.CanSeek ? stream : new BoundedStream(stream, MaxTorrentStreamBytes);
            var parser = new BencodeParser();
            var torrent = parser.Parse<BDictionary>(parseStream);

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

            var torrentName = GetStringWithUtf8Fallback(info, "name");
            if (torrentName == null)
            {
                throw new InvalidTorrentFileException("Malformed torrent file: missing or invalid 'name'.");
            }

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

            var result = new ParsedTorrent
            {
                Name = torrentName,
                InfoHash = InfoHashCalculator.Calculate(info),
                PieceLength = (int)pieceLengthNum.Value,
                PieceCount = pieceCount,
                Comment = GetStringWithUtf8Fallback(torrent, "comment"),
                CreatedBy = GetStringWithUtf8Fallback(torrent, "created by"),
                IsPrivate = info.ContainsKey("private") && (info["private"] as BNumber)?.Value == 1,
                AnnounceUrl = announceUrl,
                AnnounceList = announceListParsed,
                Files = new List<ParsedTorrentFile>()
            };

            if (torrent.ContainsKey("creation date") && torrent["creation date"] is BNumber creationDateNum)
            {
                result.CreationDate = DateTimeOffset.FromUnixTimeSeconds(creationDateNum.Value).UtcDateTime;
            }

            if (info.ContainsKey("files") && info["files"] is BList files)
            {
                var rootDirName = result.Name?.Replace('\\', '/').Trim('/', '\\');

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
                        .Where(p => !string.IsNullOrWhiteSpace(p));

                    var relativePath = string.Join("/", pathParts);
                    var fullRelativePath = string.IsNullOrEmpty(rootDirName)
                        ? relativePath
                        : string.IsNullOrEmpty(relativePath)
                            ? rootDirName
                            : $"{rootDirName}/{relativePath}";

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

                result.Files.Add(new ParsedTorrentFile
                {
                    Path = result.Name,
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

    private sealed class BoundedStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private long _bytesRead;

        public BoundedStream(Stream inner, long maxBytes)
        {
            _inner = inner;
            _maxBytes = maxBytes;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            _bytesRead += read;
            if (_bytesRead > _maxBytes)
            {
                throw new InvalidTorrentFileException("Torrent file exceeds maximum permitted size of 10 MiB.");
            }

            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer);
            _bytesRead += read;
            if (_bytesRead > _maxBytes)
            {
                throw new InvalidTorrentFileException("Torrent file exceeds maximum permitted size of 10 MiB.");
            }

            return read;
        }

        public override int ReadByte()
        {
            var b = _inner.ReadByte();
            if (b != -1)
            {
                _bytesRead++;
                if (_bytesRead > _maxBytes)
                {
                    throw new InvalidTorrentFileException("Torrent file exceeds maximum permitted size of 10 MiB.");
                }
            }

            return b;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => _inner.Flush();
    }
}
