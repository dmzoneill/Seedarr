using System;
using System.Security.Cryptography;
using System.Text;
using BencodeNET.Objects;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers.Extensions;

public interface ISyntheticMetadataGenerator
{
    byte[] GenerateMetadataBytes(Torrent torrent);
}

public class SyntheticMetadataGenerator : ISyntheticMetadataGenerator
{
    public byte[] GenerateMetadataBytes(Torrent torrent)
    {
        if (torrent == null)
        {
            return Array.Empty<byte>();
        }

        var dict = new BDictionary();

        var pieceLength = torrent.PieceLength > 0 ? torrent.PieceLength : (1024 * 1024); // default 1 MiB
        dict["piece length"] = new BNumber(pieceLength);
        dict["name"] = new BString(torrent.Name ?? "torrent");

        var isMultiFile = torrent.Files != null && torrent.Files.Count > 0;

        long totalSize = 0;
        if (isMultiFile)
        {
            var filesList = new BList();
            foreach (var file in torrent.Files)
            {
                if (file == null)
                {
                    continue;
                }

                var fileDict = new BDictionary();
                fileDict["length"] = new BNumber(file.Size);

                var pathList = new BList();
                var parts = (file.Path ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
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

            dict["files"] = filesList;
            if (torrent.TotalSize > 0)
            {
                totalSize = torrent.TotalSize;
            }
        }
        else
        {
            totalSize = torrent.TotalSize;
            dict["length"] = new BNumber(totalSize);
        }

        if (torrent.IsPrivate)
        {
            dict["private"] = new BNumber(1);
        }

        var pieceCount = torrent.PieceCount > 0
            ? torrent.PieceCount
            : (totalSize > 0 ? (int)Math.Ceiling((double)totalSize / pieceLength) : 1);

        if (pieceCount < 1)
        {
            pieceCount = 1;
        }

        byte[] infoHashBytes;
        if (!string.IsNullOrWhiteSpace(torrent.InfoHash) && torrent.InfoHash.Length == 40)
        {
            try
            {
                infoHashBytes = Convert.FromHexString(torrent.InfoHash);
            }
            catch
            {
                infoHashBytes = Encoding.UTF8.GetBytes(torrent.InfoHash);
            }
        }
        else if (!string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            infoHashBytes = Encoding.UTF8.GetBytes(torrent.InfoHash);
        }
        else
        {
            infoHashBytes = new byte[20];
        }

        var piecesBytes = new byte[pieceCount * 20];
        for (var i = 0; i < pieceCount; i++)
        {
            var pieceIndexBytes = BitConverter.GetBytes(i);
            var hash = HMACSHA1.HashData(infoHashBytes, pieceIndexBytes);
            Array.Copy(hash, 0, piecesBytes, i * 20, 20);
        }

        dict["pieces"] = new BString(piecesBytes);

        return dict.EncodeAsBytes();
    }
}
