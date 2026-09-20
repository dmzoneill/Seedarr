using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Seeding;

namespace NzbDrone.Core.Torrents;

public interface ITorrentRecheckService
{
    Torrent Recheck(int id, byte[] pieceHashes = null);
    Torrent Recheck(Torrent torrent, byte[] pieceHashes = null);
}

public class TorrentRecheckService : ITorrentRecheckService
{
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IPieceStorage _pieceStorage;
    private readonly IPieceVerificationService _pieceVerificationService;
    private readonly IMultiFilePieceStorage _multiFilePieceStorage;
    private readonly ITorrentStateMachine _stateMachine;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IFastResumeService _fastResumeService;
    private readonly Logger _logger;

    public TorrentRecheckService(
        ITorrentRepository torrentRepository,
        ITorrentFileService torrentFileService,
        IPieceStorage pieceStorage,
        IPieceVerificationService pieceVerificationService,
        IMultiFilePieceStorage multiFilePieceStorage = null,
        ITorrentStateMachine stateMachine = null,
        IEventAggregator eventAggregator = null,
        ITorrentFileParser torrentFileParser = null,
        IFastResumeService fastResumeService = null)
    {
        _torrentRepository = torrentRepository;
        _torrentFileService = torrentFileService;
        _pieceStorage = pieceStorage;
        _pieceVerificationService = pieceVerificationService;
        _multiFilePieceStorage = multiFilePieceStorage ?? new MultiFilePieceStorage();
        _stateMachine = stateMachine;
        _eventAggregator = eventAggregator;
        _torrentFileParser = torrentFileParser ?? new TorrentFileParser();
        _fastResumeService = fastResumeService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public Torrent Recheck(int id, byte[] pieceHashes = null)
    {
        var torrent = _torrentRepository?.Get(id);
        if (torrent == null)
        {
            return null;
        }

        return Recheck(torrent, pieceHashes);
    }

    public Torrent Recheck(Torrent torrent, byte[] pieceHashes = null)
    {
        if (torrent == null)
        {
            return null;
        }

        _logger.Info("Starting hash verification for torrent {0} (Id: {1})", torrent.Name, torrent.Id);

        // 1. Suspend active transfer speeds and active flags before hash verification
        torrent.Active = false;
        torrent.UploadSpeed = 0;
        torrent.DownloadSpeed = 0;

        // 2. Coordinate with TorrentStateMachine / SeedingEngine to enter Checking status
        if (_stateMachine != null)
        {
            _stateMachine.TransitionToChecking(torrent);
        }
        else
        {
            var oldStatus = torrent.Status;
            torrent.Status = TorrentStatus.Checking;
            _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Checking));
        }

        _torrentRepository?.Update(torrent);

        // 3. Resolve files and expected piece hashes
        var files = _torrentFileService?.GetByTorrentId(torrent.Id) ?? torrent.Files;
        if ((files == null || files.Count == 0) && torrent.TotalSize > 0)
        {
            files = new List<TorrentFile>
            {
                new()
                {
                    TorrentId = torrent.Id,
                    Path = torrent.Name ?? "file",
                    Size = torrent.TotalSize
                }
            };
        }

        if (pieceHashes == null && !string.IsNullOrWhiteSpace(torrent.SourcePath) && File.Exists(torrent.SourcePath))
        {
            try
            {
                using var stream = new FileStream(torrent.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var parsed = _torrentFileParser?.Parse(stream);
                if (parsed?.Pieces != null)
                {
                    pieceHashes = parsed.Pieces;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to parse piece hashes from source torrent file for {0}", torrent.Name);
            }
        }

        var pieceCount = torrent.PieceCount;
        if (pieceCount <= 0 && pieceHashes != null)
        {
            pieceCount = pieceHashes.Length / 20;
            torrent.PieceCount = pieceCount;
        }

        if (pieceCount <= 0)
        {
            pieceCount = 1;
        }

        var bitfield = new bool[pieceCount];

        // 4. Perform piece verification sequentially with thread-safe file isolation
        for (var i = 0; i < pieceCount; i++)
        {
            try
            {
                if (pieceHashes != null && pieceHashes.Length >= (i + 1) * 20 && files != null && files.Count > 0 && _pieceVerificationService != null)
                {
                    var expectedHash = new byte[20];
                    Array.Copy(pieceHashes, i * 20, expectedHash, 0, 20);
                    var verified = _pieceVerificationService.VerifyPieceFromStorage(
                        torrent,
                        files,
                        i,
                        expectedHash,
                        _multiFilePieceStorage,
                        torrent.SavePath);

                    bitfield[i] = verified;
                }
                else
                {
                    bitfield[i] = VerifyPiecePresenceFallback(torrent, files, i, pieceCount);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error verifying piece {0} for torrent {1}", i, torrent.Id);
                bitfield[i] = false;
            }
        }

        // 5. Update torrent bitfield and verified pieces in IPieceStorage
        if (_pieceStorage != null && !string.IsNullOrEmpty(torrent.InfoHash))
        {
            _pieceStorage.SetVerifiedPieces(torrent.InfoHash, bitfield);
        }

        var verifiedCount = bitfield.Count(b => b);
        torrent.Progress = pieceCount > 0 ? (double)verifiedCount / pieceCount : 0.0;
        if (verifiedCount == pieceCount)
        {
            torrent.Progress = 1.0;
        }

        // 6. Transition state back to appropriate status (Downloading or Seeding)
        if (_stateMachine != null)
        {
            _stateMachine.TransitionFromChecking(torrent);
        }
        else
        {
            var oldStatus = torrent.Status;
            var newStatus = torrent.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
            torrent.Status = newStatus;
            _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, newStatus));
        }

        torrent.LastActive = DateTime.UtcNow;
        _torrentRepository?.Update(torrent);

        try
        {
            _fastResumeService?.SaveFastResume(torrent);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to save fast resume after recheck for {0}", torrent.InfoHash);
        }

        _logger.Info(
            "Recheck completed for torrent {0} (Id: {1}): {2}/{3} pieces verified, status {4}",
            torrent.Name,
            torrent.Id,
            verifiedCount,
            pieceCount,
            torrent.Status);

        return torrent;
    }

    private static bool VerifyPiecePresenceFallback(Torrent torrent, IList<TorrentFile> files, int pieceIndex, int pieceCount)
    {
        if (files == null || files.Count == 0)
        {
            return torrent.Progress >= 1.0;
        }

        var pieceLength = torrent.PieceLength > 0 ? torrent.PieceLength : (int)Math.Max(1, torrent.TotalSize / pieceCount);
        var pieceStart = (long)pieceIndex * pieceLength;
        var pieceEnd = Math.Min(torrent.TotalSize, pieceStart + pieceLength);

        var basePath = !string.IsNullOrWhiteSpace(torrent.SavePath) ? torrent.SavePath : string.Empty;
        long currentOffset = 0;

        foreach (var file in files)
        {
            var fileEnd = currentOffset + file.Size;
            if (currentOffset < pieceEnd && fileEnd > pieceStart)
            {
                var diskPath = Path.IsPathRooted(file.Path) ? file.Path : Path.Combine(basePath, file.Path);
                if (!File.Exists(diskPath))
                {
                    return false;
                }

                try
                {
                    using var handle = File.OpenHandle(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var fileLength = RandomAccess.GetLength(handle);
                    var neededInFile = Math.Min(file.Size, pieceEnd - currentOffset);
                    if (fileLength < neededInFile)
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            currentOffset = fileEnd;
        }

        return true;
    }
}
