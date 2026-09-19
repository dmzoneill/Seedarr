using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BencodeNET.Objects;
using BencodeNET.Parsing;

namespace NzbDrone.Core.Torrents;

public interface IFastResumeBencodeSerializer
{
    byte[] Serialize(FastResumeData data);

    FastResumeData Deserialize(byte[] bytes);

    bool IsBencode(byte[] bytes);
}

public class FastResumeBencodeSerializer : IFastResumeBencodeSerializer
{
    public const string DefaultFileFormat = "libtorrent resume file";
    public const int DefaultFileVersion = 1;

    public static byte[] SerializeToBytes(FastResumeData data)
    {
        return new FastResumeBencodeSerializer().Serialize(data);
    }

    public static FastResumeData DeserializeFromBytes(byte[] bytes)
    {
        return new FastResumeBencodeSerializer().Deserialize(bytes);
    }

    public bool IsBencode(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 2)
        {
            return false;
        }

        var start = 0;
        while (start < bytes.Length && char.IsWhiteSpace((char)bytes[start]))
        {
            start++;
        }

        if (start >= bytes.Length)
        {
            return false;
        }

        return bytes[start] == (byte)'d';
    }

    public byte[] Serialize(FastResumeData data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var dict = new BDictionary();

        // 1. file-format & file-version
        dict["file-format"] = new BString(string.IsNullOrWhiteSpace(data.FileFormat) ? DefaultFileFormat : data.FileFormat);
        dict["file-version"] = new BNumber(data.FileVersion > 0 ? data.FileVersion : DefaultFileVersion);

        // 2. info-hash (20-byte binary)
        byte[] infoHashBytes = null;
        if (!string.IsNullOrWhiteSpace(data.InfoHash))
        {
            if (data.InfoHash.Length == 40)
            {
                try
                {
                    infoHashBytes = Convert.FromHexString(data.InfoHash);
                }
                catch
                {
                    infoHashBytes = Encoding.UTF8.GetBytes(data.InfoHash);
                }
            }
            else
            {
                infoHashBytes = Encoding.UTF8.GetBytes(data.InfoHash);
            }
        }

        if (infoHashBytes == null || infoHashBytes.Length == 0)
        {
            infoHashBytes = new byte[20];
        }
        else if (infoHashBytes.Length < 20)
        {
            var padded = new byte[20];
            Array.Copy(infoHashBytes, padded, infoHashBytes.Length);
            infoHashBytes = padded;
        }
        else if (infoHashBytes.Length > 20)
        {
            var truncated = new byte[20];
            Array.Copy(infoHashBytes, truncated, 20);
            infoHashBytes = truncated;
        }

        dict["info-hash"] = new BString(infoHashBytes);

        // 3. pieces (libtorrent v1: 1 byte per piece, 1 if verified, 0 if not)
        if (data.Bitfield != null && data.Bitfield.Length > 0)
        {
            var piecesBytes = new byte[data.Bitfield.Length];
            for (var i = 0; i < data.Bitfield.Length; i++)
            {
                piecesBytes[i] = data.Bitfield[i] ? (byte)1 : (byte)0;
            }

            dict["pieces"] = new BString(piecesBytes);
        }
        else
        {
            dict["pieces"] = new BString(Array.Empty<byte>());
        }

        // 4. unfinished list
        var unfinishedList = new BList();
        if (data.Unfinished != null && data.Unfinished.Count > 0)
        {
            foreach (var uf in data.Unfinished)
            {
                var uDict = new BDictionary
                {
                    ["piece"] = new BNumber(uf.Piece),
                    ["bitmask"] = new BString(uf.Bitmask ?? Array.Empty<byte>())
                };

                if (uf.Adler32.HasValue)
                {
                    uDict["adler32"] = new BNumber(uf.Adler32.Value);
                }

                unfinishedList.Add(uDict);
            }
        }

        dict["unfinished"] = unfinishedList;

        // 5. total_uploaded & total_downloaded & progress & status
        dict["total_uploaded"] = new BNumber(data.Uploaded);
        dict["total_downloaded"] = new BNumber(data.Downloaded);
        dict["progress"] = new BNumber((long)Math.Round(data.Progress * 10000));
        dict["status"] = new BString(data.Status ?? string.Empty);

        // 6. active_time, seeding_time, finished_time
        dict["active_time"] = new BNumber(data.ActiveTime);
        dict["seeding_time"] = new BNumber(data.SeedingTime);
        dict["finished_time"] = new BNumber(data.FinishedTime);

        // 7. file_priority
        var fpList = new BList();
        if (data.FilePriority != null && data.FilePriority.Count > 0)
        {
            foreach (var fp in data.FilePriority)
            {
                fpList.Add((IBObject)new BNumber(fp));
            }
        }
        else if (data.Files != null && data.Files.Count > 0)
        {
            foreach (var file in data.Files)
            {
                fpList.Add((IBObject)new BNumber(1));
            }
        }

        dict["file_priority"] = fpList;

        // 8. piece_priority
        if (data.PiecePriority != null && data.PiecePriority.Length > 0)
        {
            dict["piece_priority"] = new BString(data.PiecePriority);
        }
        else if (data.Bitfield != null && data.Bitfield.Length > 0)
        {
            var pp = new byte[data.Bitfield.Length];
            Array.Fill(pp, (byte)1);
            dict["piece_priority"] = new BString(pp);
        }
        else
        {
            dict["piece_priority"] = new BString(Array.Empty<byte>());
        }

        // 9. sequential_download
        dict["sequential_download"] = new BNumber(data.SequentialDownload ? 1 : 0);

        // 10. save_path
        dict["save_path"] = new BString(data.SavePath ?? string.Empty);

        // 11. allocation
        dict["allocation"] = new BString(string.IsNullOrWhiteSpace(data.Allocation) ? "sparse" : data.Allocation);

        // 12. file sizes, mtime, mapped_files
        var fileSizesList = new BList();
        var mtimeList = new BList();
        var mappedFilesList = new BList();

        if (data.Files != null && data.Files.Count > 0)
        {
            foreach (var file in data.Files)
            {
                fileSizesList.Add((IBObject)new BNumber(file.Length));
                var mtimeSec = file.Mtime.HasValue
                    ? new DateTimeOffset(file.Mtime.Value.ToUniversalTime()).ToUnixTimeSeconds()
                    : 0;
                mtimeList.Add((IBObject)new BNumber(mtimeSec));
                mappedFilesList.Add(new BString(file.Path ?? string.Empty));
            }
        }

        dict["file sizes"] = fileSizesList;
        dict["mtime"] = mtimeList;
        dict["mapped_files"] = mappedFilesList;

        return dict.EncodeAsBytes();
    }

    public FastResumeData Deserialize(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        var parser = new BencodeParser();
        var dict = parser.Parse<BDictionary>(bytes);
        if (dict == null)
        {
            throw new InvalidOperationException("Failed to parse BDictionary from bytes");
        }

        var data = new FastResumeData();

        // 1. file-format & file-version
        if (dict.ContainsKey("file-format") && dict["file-format"] is BString ffStr)
        {
            data.FileFormat = ffStr.ToString(Encoding.UTF8);
        }

        var fileVersion = DefaultFileVersion;
        if (dict.ContainsKey("file-version") && dict["file-version"] is BNumber fvNum)
        {
            fileVersion = (int)fvNum.Value;
            data.FileVersion = fileVersion;
        }

        // 2. info-hash
        if (dict.ContainsKey("info-hash") && dict["info-hash"] is BString ihStr)
        {
            var ihBytes = ihStr.Value.ToArray();
            if (ihBytes.Length == 20)
            {
                data.InfoHash = Convert.ToHexString(ihBytes).ToLowerInvariant();
            }
            else
            {
                data.InfoHash = Encoding.UTF8.GetString(ihBytes);
            }
        }

        // 3. pieces
        if (dict.ContainsKey("pieces") && dict["pieces"] is BString piecesStr)
        {
            var pBytes = piecesStr.Value.ToArray();
            if (pBytes.Length > 0)
            {
                var isV1BytePerPiece = fileVersion <= 1 && pBytes.All(b => b <= 3);
                if (isV1BytePerPiece)
                {
                    var bitfield = new bool[pBytes.Length];
                    for (var i = 0; i < pBytes.Length; i++)
                    {
                        bitfield[i] = (pBytes[i] & 1) != 0;
                    }

                    data.Bitfield = bitfield;
                }
                else
                {
                    var bitfield = new bool[pBytes.Length * 8];
                    for (var i = 0; i < bitfield.Length; i++)
                    {
                        var byteIndex = i / 8;
                        var bitOffset = 7 - (i % 8);
                        bitfield[i] = (pBytes[byteIndex] & (1 << bitOffset)) != 0;
                    }

                    data.Bitfield = bitfield;
                }
            }
            else
            {
                data.Bitfield = Array.Empty<bool>();
            }
        }

        // 4. unfinished
        if (dict.ContainsKey("unfinished") && dict["unfinished"] is BList uList)
        {
            data.Unfinished = new List<FastResumeUnfinishedPiece>();
            foreach (var item in uList)
            {
                if (item is BDictionary uDict)
                {
                    var piece = uDict.ContainsKey("piece") && uDict["piece"] is BNumber pNum ? (int)pNum.Value : 0;
                    var bitmask = uDict.ContainsKey("bitmask") && uDict["bitmask"] is BString bmStr ? bmStr.Value.ToArray() : Array.Empty<byte>();
                    uint? adler = uDict.ContainsKey("adler32") && uDict["adler32"] is BNumber adNum ? (uint)adNum.Value : null;

                    data.Unfinished.Add(new FastResumeUnfinishedPiece
                    {
                        Piece = piece,
                        Bitmask = bitmask,
                        Adler32 = adler
                    });
                }
            }
        }

        // 5. total_uploaded & total_downloaded & progress & status
        if (dict.ContainsKey("total_uploaded") && dict["total_uploaded"] is BNumber upNum)
        {
            data.Uploaded = upNum.Value;
        }

        if (dict.ContainsKey("total_downloaded") && dict["total_downloaded"] is BNumber downNum)
        {
            data.Downloaded = downNum.Value;
        }

        if (dict.ContainsKey("progress") && dict["progress"] is BNumber progNum)
        {
            data.Progress = (double)progNum.Value / 10000.0;
        }

        if (dict.ContainsKey("status") && dict["status"] is BString statusStr)
        {
            data.Status = statusStr.ToString(Encoding.UTF8);
        }

        // 6. active_time, seeding_time, finished_time
        if (dict.ContainsKey("active_time") && dict["active_time"] is BNumber atNum)
        {
            data.ActiveTime = atNum.Value;
        }

        if (dict.ContainsKey("seeding_time") && dict["seeding_time"] is BNumber stNum)
        {
            data.SeedingTime = stNum.Value;
        }

        if (dict.ContainsKey("finished_time") && dict["finished_time"] is BNumber ftNum)
        {
            data.FinishedTime = ftNum.Value;
        }

        // 7. file_priority
        if (dict.ContainsKey("file_priority") && dict["file_priority"] is BList fpList)
        {
            data.FilePriority = new List<int>();
            foreach (var item in fpList)
            {
                if (item is BNumber fpNum)
                {
                    data.FilePriority.Add((int)fpNum.Value);
                }
            }
        }

        // 8. piece_priority
        if (dict.ContainsKey("piece_priority") && dict["piece_priority"] is BString ppStr)
        {
            data.PiecePriority = ppStr.Value.ToArray();
        }

        // 9. sequential_download
        if (dict.ContainsKey("sequential_download") && dict["sequential_download"] is BNumber seqNum)
        {
            data.SequentialDownload = seqNum.Value != 0;
        }

        // 10. save_path
        if (dict.ContainsKey("save_path") && dict["save_path"] is BString spStr)
        {
            data.SavePath = spStr.ToString(Encoding.UTF8);
        }
        else if (dict.ContainsKey("qBt-savePath") && dict["qBt-savePath"] is BString qbtSpStr)
        {
            data.SavePath = qbtSpStr.ToString(Encoding.UTF8);
        }

        // 11. allocation
        if (dict.ContainsKey("allocation") && dict["allocation"] is BString allocStr)
        {
            data.Allocation = allocStr.ToString(Encoding.UTF8);
        }

        // 12. file sizes, mtime, mapped_files
        var fileSizesList = dict.ContainsKey("file sizes") && dict["file sizes"] is BList fsl ? fsl : null;
        var mtimeList = dict.ContainsKey("mtime") && dict["mtime"] is BList mtl ? mtl : null;
        var mappedFilesList = dict.ContainsKey("mapped_files") && dict["mapped_files"] is BList mfl ? mfl : null;

        var count = Math.Max(fileSizesList?.Count ?? 0, Math.Max(mtimeList?.Count ?? 0, mappedFilesList?.Count ?? 0));
        data.Files = new List<FastResumeFileEntry>();

        for (var i = 0; i < count; i++)
        {
            long length = 0;
            if (fileSizesList != null && i < fileSizesList.Count && fileSizesList[i] is BNumber lenNum)
            {
                length = lenNum.Value;
            }

            DateTime? mtime = null;
            if (mtimeList != null && i < mtimeList.Count && mtimeList[i] is BNumber mtNum && mtNum.Value > 0)
            {
                mtime = DateTimeOffset.FromUnixTimeSeconds(mtNum.Value).UtcDateTime;
            }

            string path = null;
            if (mappedFilesList != null && i < mappedFilesList.Count && mappedFilesList[i] is BString pStr)
            {
                path = pStr.ToString(Encoding.UTF8);
            }

            data.Files.Add(new FastResumeFileEntry
            {
                Path = path,
                Length = length,
                Mtime = mtime
            });
        }

        if (data.Files.Count == 1 && string.IsNullOrWhiteSpace(data.Files[0].Path) && dict.ContainsKey("qBt-name") && dict["qBt-name"] is BString qbtName)
        {
            data.Files[0].Path = qbtName.ToString(Encoding.UTF8);
        }

        if (data.Bitfield != null && data.Bitfield.Length > 0)
        {
            var completed = data.Bitfield.Count(b => b);
            data.Progress = (double)completed / data.Bitfield.Length;
            data.Status = data.Progress >= 1.0 ? TorrentStatus.Seeding.ToString() : TorrentStatus.Downloading.ToString();
        }

        data.SavedAt = DateTime.UtcNow;

        return data;
    }
}
