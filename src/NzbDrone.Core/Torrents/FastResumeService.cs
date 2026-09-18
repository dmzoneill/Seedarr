using System;
using System.IO;
using System.Text;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Torrents;

public class FastResumeService : IFastResumeService
{
    private readonly ITorrentService _torrentService;
    private readonly IPieceStorage _pieceStorage;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;

    public FastResumeService(
        ITorrentService torrentService,
        IPieceStorage pieceStorage = null,
        IAppFolderInfo appFolderInfo = null)
    {
        _torrentService = torrentService;
        _pieceStorage = pieceStorage;
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void SaveAll()
    {
        var torrents = _torrentService?.GetAll();
        if (torrents == null)
        {
            return;
        }

        foreach (var torrent in torrents)
        {
            try
            {
                SaveFastResume(torrent);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to save FastResume for torrent {0}", torrent.Id);
            }
        }
    }

    public void SaveFastResume(Torrent torrent)
    {
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            return;
        }

        var bitfield = _pieceStorage?.GetVerifiedPieces(torrent.InfoHash);
        if (bitfield == null && torrent.Progress >= 1.0 && torrent.PieceCount > 0)
        {
            bitfield = new bool[torrent.PieceCount];
            Array.Fill(bitfield, true);
        }

        var appDataFolder = _appFolderInfo?.AppDataFolder ?? AppContext.BaseDirectory;
        var resumeDir = Path.Combine(appDataFolder, "fastresume");
        Directory.CreateDirectory(resumeDir);

        var resumeData = new FastResumeData
        {
            InfoHash = torrent.InfoHash,
            Bitfield = bitfield,
            Uploaded = torrent.Uploaded,
            Downloaded = torrent.Downloaded,
            Progress = torrent.Progress,
            Status = torrent.Status.ToString(),
            SavedAt = DateTime.UtcNow,
        };

        var filePath = Path.Combine(resumeDir, $"{torrent.InfoHash.ToLowerInvariant()}.fastresume");
        var tempPath = $"{filePath}.tmp";
        var json = STJson.ToJson(resumeData);
        var bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    public FastResumeData LoadFastResume(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        var appDataFolder = _appFolderInfo?.AppDataFolder ?? AppContext.BaseDirectory;
        var filePath = Path.Combine(appDataFolder, "fastresume", $"{infoHash.ToLowerInvariant()}.fastresume");
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var data = STJson.FromJson<FastResumeData>(json);
            if (data?.Bitfield != null && _pieceStorage != null)
            {
                _pieceStorage.SetVerifiedPieces(infoHash, data.Bitfield);
            }

            return data;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to load FastResume data for {0}", infoHash);
            return null;
        }
    }
}
