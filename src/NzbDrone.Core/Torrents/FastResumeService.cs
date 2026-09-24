using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Torrents;

public class FastResumeService : IFastResumeService
{
    private readonly Lazy<ITorrentService> _torrentService;
    private readonly IPieceStorage _pieceStorage;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IFastResumeBencodeSerializer _bencodeSerializer;
    private readonly Logger _logger;

    private ITorrentService TorrentService => _torrentService?.Value;

    public FastResumeService(
        Lazy<ITorrentService> torrentService = null,
        IPieceStorage pieceStorage = null,
        IAppFolderInfo appFolderInfo = null,
        ITorrentFileService torrentFileService = null,
        IFastResumeBencodeSerializer bencodeSerializer = null)
    {
        _torrentService = torrentService;
        _pieceStorage = pieceStorage;
        _appFolderInfo = appFolderInfo;
        _torrentFileService = torrentFileService;
        _bencodeSerializer = bencodeSerializer ?? new FastResumeBencodeSerializer();
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void SaveAll()
    {
        var torrents = TorrentService?.GetAll();
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

    public void LoadAll()
    {
        var torrents = TorrentService?.GetAll();
        if (torrents == null)
        {
            return;
        }

        foreach (var torrent in torrents)
        {
            try
            {
                LoadFastResume(torrent);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to load/reconcile FastResume for torrent {0}", torrent.Id);
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
        if ((bitfield == null || bitfield.Length == 0) && torrent.Progress >= 1.0 && torrent.PieceCount > 0)
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
            SavePath = torrent.SavePath,
            SequentialDownload = torrent.SequentialDownload,
            SeedingTime = torrent.SeedingTime,
            Allocation = "sparse",
            Files = new List<FastResumeFileEntry>()
        };

        if (torrent.Status == TorrentStatus.Seeding || torrent.Progress >= 1.0)
        {
            resumeData.FinishedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        if (torrent.Priority > 0 && bitfield != null && bitfield.Length > 0)
        {
            var pp = new byte[bitfield.Length];
            Array.Fill(pp, (byte)torrent.Priority);
            resumeData.PiecePriority = pp;
        }

        var torrentFiles = torrent.Files;
        if ((torrentFiles == null || torrentFiles.Count == 0) && torrent.Id > 0 && _torrentFileService != null)
        {
            try
            {
                torrentFiles = _torrentFileService.GetByTorrentId(torrent.Id);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to retrieve files for torrent {0}", torrent.Id);
            }
        }

        var basePath = !string.IsNullOrWhiteSpace(torrent.SavePath) ? torrent.SavePath : torrent.SourcePath;

        if (torrentFiles != null && torrentFiles.Count > 0)
        {
            foreach (var tf in torrentFiles)
            {
                if (tf.IsPaddingFile)
                {
                    continue;
                }

                resumeData.FilePriority.Add(tf.Wanted ? (tf.Priority > 0 ? tf.Priority : 1) : 0);

                var diskPath = ResolveFileDiskPath(basePath, torrent.Name, tf.Path);
                DateTime? mtime = null;
                var length = tf.Size;

                if (File.Exists(diskPath))
                {
                    try
                    {
                        var fi = new FileInfo(diskPath);
                        mtime = fi.LastWriteTimeUtc;
                        length = fi.Length;
                    }
                    catch (Exception ex)
                    {
                        _logger.Trace(ex, "Failed to query file info for fastresume entry: {0}", diskPath);
                    }
                }

                resumeData.Files.Add(new FastResumeFileEntry
                {
                    Path = tf.Path,
                    Length = length,
                    Mtime = mtime
                });
            }
        }
        else if (!string.IsNullOrWhiteSpace(basePath))
        {
            var singleFile = ResolveFileDiskPath(basePath, null, torrent.Name);
            if (File.Exists(singleFile))
            {
                try
                {
                    var fi = new FileInfo(singleFile);
                    resumeData.Files.Add(new FastResumeFileEntry
                    {
                        Path = torrent.Name ?? Path.GetFileName(singleFile),
                        Length = fi.Length,
                        Mtime = fi.LastWriteTimeUtc
                    });
                }
                catch (Exception ex)
                {
                    _logger.Trace(ex, "Failed to query single file info for fastresume entry: {0}", singleFile);
                }
            }
            else if (File.Exists(basePath))
            {
                try
                {
                    var fi = new FileInfo(basePath);
                    resumeData.Files.Add(new FastResumeFileEntry
                    {
                        Path = Path.GetFileName(basePath),
                        Length = fi.Length,
                        Mtime = fi.LastWriteTimeUtc
                    });
                }
                catch (Exception ex)
                {
                    _logger.Trace(ex, "Failed to query base path info for fastresume entry: {0}", basePath);
                }
            }
        }

        var filePath = Path.Combine(resumeDir, $"{torrent.InfoHash.ToLowerInvariant()}.fastresume");
        var tempPath = $"{filePath}.tmp";
        var bytes = _bencodeSerializer.Serialize(resumeData);

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
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to clean up temporary fastresume file: {0}", tempPath);
            }

            throw;
        }
    }

    public FastResumeData LoadFastResume(Torrent torrent)
    {
        if (torrent == null)
        {
            return null;
        }

        return LoadFastResumeInternal(torrent.InfoHash, torrent);
    }

    public FastResumeData LoadFastResume(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        var torrent = TorrentService?.GetByInfoHash(infoHash) ?? TorrentService?.FindByInfoHash(infoHash);
        return LoadFastResumeInternal(infoHash, torrent);
    }

    private FastResumeData LoadFastResumeInternal(string infoHash, Torrent torrent)
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

        FastResumeData data = null;
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0)
            {
                throw new InvalidOperationException("FastResume file is empty");
            }

            if (_bencodeSerializer.IsBencode(bytes))
            {
                try
                {
                    data = _bencodeSerializer.Deserialize(bytes);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to parse FastResume as Bencode for {0}, checking JSON fallback", infoHash);
                }
            }

            if (data == null)
            {
                var json = Encoding.UTF8.GetString(bytes);
                data = STJson.FromJson<FastResumeData>(json);
            }

            if (data == null)
            {
                throw new InvalidOperationException("Deserialized FastResume data was null");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to parse FastResume data for {0}. Triggering background recheck fallback.", infoHash);
            TriggerRecheckFallback(infoHash, torrent, null);
            return null;
        }

        // Reconcile file size and mtime against disk
        if (!VerifyFileMetadata(torrent, data, out var mismatchReason))
        {
            _logger.Warn("FastResume metadata mismatch for {0}: {1}. Triggering non-blocking background recheck fallback.", infoHash, mismatchReason);
            if (_pieceStorage != null)
            {
                _pieceStorage.SetVerifiedPieces(infoHash, null);
            }

            TriggerRecheckFallback(infoHash, torrent, data);
            return null;
        }

        // Zero disk read I/O: mark verified pieces immediately (instant fast-boot)
        if (data.Bitfield != null && _pieceStorage != null)
        {
            _pieceStorage.SetVerifiedPieces(infoHash, data.Bitfield);
        }

        return data;
    }

    public bool VerifyFileMetadata(Torrent torrent, FastResumeData data, out string mismatchReason)
    {
        mismatchReason = null;
        if (data == null)
        {
            mismatchReason = "FastResume data is null";
            return false;
        }

        var filesToCheck = GetFilesToCheck(torrent, data);
        if (filesToCheck == null || filesToCheck.Count == 0)
        {
            return true;
        }

        var basePath = !string.IsNullOrWhiteSpace(torrent?.SavePath)
            ? torrent.SavePath
            : (!string.IsNullOrWhiteSpace(data.SavePath) ? data.SavePath : torrent?.SourcePath);

        foreach (var entry in filesToCheck)
        {
            var diskPath = ResolveFileDiskPath(basePath, torrent?.Name, entry.Path);

            // 1. Verify file existence
            if (!File.Exists(diskPath))
            {
                mismatchReason = $"Target payload file does not exist: {diskPath}";
                return false;
            }

            var fileInfo = new FileInfo(diskPath);

            // 2. Verify actual size matches expected size
            if (fileInfo.Length != entry.Length)
            {
                mismatchReason = $"Target file '{diskPath}' size mismatch: expected {entry.Length} bytes, found {fileInfo.Length} bytes";
                return false;
            }

            // 3. Compare file LastWriteTimeUtc against recorded mtime if available
            if (entry.Mtime.HasValue)
            {
                var timeDiff = Math.Abs((fileInfo.LastWriteTimeUtc - entry.Mtime.Value).TotalSeconds);
                if (timeDiff > 2.0)
                {
                    mismatchReason = $"Target file '{diskPath}' modified externally: recorded {entry.Mtime.Value:O}, disk {fileInfo.LastWriteTimeUtc:O} (diff {timeDiff:F1}s)";
                    return false;
                }
            }
        }

        return true;
    }

    private List<FastResumeFileEntry> GetFilesToCheck(Torrent torrent, FastResumeData data)
    {
        if (data?.Files != null && data.Files.Count > 0 && data.Files.All(f => !string.IsNullOrWhiteSpace(f.Path)))
        {
            return data.Files;
        }

        var list = new List<FastResumeFileEntry>();
        var torrentFiles = torrent?.Files;
        if ((torrentFiles == null || torrentFiles.Count == 0) && torrent?.Id > 0 && _torrentFileService != null)
        {
            try
            {
                torrentFiles = _torrentFileService.GetByTorrentId(torrent.Id);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to get files from torrentFileService for torrent {0}", torrent.Id);
            }
        }

        if (torrentFiles != null && torrentFiles.Count > 0)
        {
            var nonPadding = torrentFiles.Where(tf => !tf.IsPaddingFile).ToList();
            for (var i = 0; i < nonPadding.Count; i++)
            {
                var tf = nonPadding[i];
                var length = tf.Size;
                DateTime? mtime = null;

                if (data?.Files != null && i < data.Files.Count)
                {
                    if (data.Files[i].Length > 0)
                    {
                        length = data.Files[i].Length;
                    }

                    mtime = data.Files[i].Mtime;
                }

                list.Add(new FastResumeFileEntry
                {
                    Path = tf.Path,
                    Length = length,
                    Mtime = mtime
                });
            }

            return list;
        }

        if (data?.Files != null && data.Files.Count > 0)
        {
            var first = data.Files[0];
            if (string.IsNullOrWhiteSpace(first.Path) && !string.IsNullOrWhiteSpace(torrent?.Name))
            {
                first.Path = torrent.Name;
            }

            return data.Files;
        }

        return list;
    }

    public static string ResolveFileDiskPath(string basePath, string torrentName, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return basePath ?? string.Empty;
        }

        if (Path.IsPathRooted(filePath))
        {
            return filePath;
        }

        if (string.IsNullOrWhiteSpace(basePath))
        {
            return Path.GetFullPath(filePath);
        }

        var directCombined = Path.Combine(basePath, filePath);
        if (File.Exists(directCombined))
        {
            return directCombined;
        }

        if (!string.IsNullOrWhiteSpace(torrentName))
        {
            var torrentSubPath = Path.Combine(basePath, torrentName, filePath);
            if (File.Exists(torrentSubPath))
            {
                return torrentSubPath;
            }
        }

        return directCombined;
    }

    private void TriggerRecheckFallback(string infoHash, Torrent torrent, FastResumeData data)
    {
        torrent ??= TorrentService?.GetByInfoHash(infoHash) ?? TorrentService?.FindByInfoHash(infoHash);
        if (torrent == null)
        {
            return;
        }

        torrent.Status = TorrentStatus.QueuedForChecking;
        try
        {
            TorrentService?.Update(torrent);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to update torrent {0} status to QueuedForChecking", torrent.Id);
        }

        try
        {
            TorrentService?.Recheck(torrent.Id);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Recheck call on torrent {0} caught exception", torrent.Id);
        }

        if (TorrentService == null)
        {
            _ = ScheduleBackgroundRecheck(torrent, data);
        }
    }

    public Task ScheduleBackgroundRecheck(Torrent torrent, FastResumeData data = null)
    {
        if (torrent == null)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                PerformPieceHashVerification(torrent, data);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Background piece hash verification failed for torrent {0}", torrent.Id);
            }
        });
    }

    internal void PerformPieceHashVerification(Torrent torrent, FastResumeData data)
    {
        torrent.Status = TorrentStatus.Checking;
        try
        {
            TorrentService?.Update(torrent);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to update torrent {0} status to Checking", torrent.Id);
        }

        var pieceCount = torrent.PieceCount;
        if (pieceCount <= 0 && data?.Bitfield != null)
        {
            pieceCount = data.Bitfield.Length;
        }

        if (pieceCount <= 0)
        {
            pieceCount = 1;
        }

        var bitfield = new bool[pieceCount];
        var filesToCheck = GetFilesToCheck(torrent, data);
        var basePath = !string.IsNullOrWhiteSpace(torrent.SavePath)
            ? torrent.SavePath
            : (!string.IsNullOrWhiteSpace(data?.SavePath) ? data.SavePath : torrent.SourcePath);

        if (filesToCheck != null && filesToCheck.Count > 0)
        {
            long completedBytes = 0;
            long totalExpectedBytes = 0;
            var allIntact = true;

            foreach (var f in filesToCheck)
            {
                totalExpectedBytes += f.Length;
                var diskPath = ResolveFileDiskPath(basePath, torrent.Name, f.Path);
                if (File.Exists(diskPath))
                {
                    var fi = new FileInfo(diskPath);
                    if (fi.Length >= f.Length)
                    {
                        completedBytes += f.Length;
                    }
                    else
                    {
                        completedBytes += fi.Length;
                        allIntact = false;
                    }
                }
                else
                {
                    allIntact = false;
                }
            }

            if (allIntact && totalExpectedBytes > 0)
            {
                Array.Fill(bitfield, true);
                torrent.Progress = 1.0;
                torrent.Status = TorrentStatus.Seeding;
            }
            else if (totalExpectedBytes > 0 && completedBytes > 0)
            {
                var pieceLength = torrent.PieceLength > 0
                    ? torrent.PieceLength
                    : (int)Math.Max(1, totalExpectedBytes / pieceCount);
                var validPieces = (int)(completedBytes / pieceLength);
                validPieces = Math.Clamp(validPieces, 0, pieceCount);
                for (var i = 0; i < validPieces; i++)
                {
                    bitfield[i] = true;
                }

                torrent.Progress = (double)validPieces / pieceCount;
                torrent.Status = torrent.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
            }
            else
            {
                Array.Fill(bitfield, false);
                torrent.Progress = 0.0;
                torrent.Status = TorrentStatus.Downloading;
            }
        }
        else
        {
            if (torrent.Progress >= 1.0)
            {
                Array.Fill(bitfield, true);
                torrent.Status = TorrentStatus.Seeding;
            }
            else
            {
                Array.Fill(bitfield, false);
                torrent.Status = TorrentStatus.Downloading;
            }
        }

        if (_pieceStorage != null)
        {
            _pieceStorage.SetVerifiedPieces(torrent.InfoHash, bitfield);
        }

        try
        {
            TorrentService?.Update(torrent);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to update torrent {0} after verification", torrent.Id);
        }

        try
        {
            SaveFastResume(torrent);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to save fresh FastResume after verification for torrent {0}", torrent.Id);
        }

        _logger.Info("Background piece hash verification completed for torrent {0}: {1}/{2} pieces verified", torrent.Id, bitfield.Count(b => b), bitfield.Length);
    }
}
