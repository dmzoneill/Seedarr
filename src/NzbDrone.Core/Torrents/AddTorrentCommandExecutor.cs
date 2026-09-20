using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Torrents;

public class AddTorrentCommandExecutor : IExecute<AddTorrentCommand>
{
    private readonly ITorrentFileParser _parser;
    private readonly ITorrentService _torrentService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public AddTorrentCommandExecutor(
        ITorrentFileParser parser,
        ITorrentService torrentService,
        ITrackerEntryService trackerEntryService,
        ITorrentFileService torrentFileService,
        IConfigService configService)
    {
        _parser = parser;
        _torrentService = torrentService;
        _trackerEntryService = trackerEntryService;
        _torrentFileService = torrentFileService;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(AddTorrentCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.FilePath))
        {
            throw new ArgumentException("FilePath is required for AddTorrentCommand");
        }

        _logger.Info("Adding torrent from file: {0}", command.FilePath);

        var parsed = _parser.Parse(command.FilePath);

        var existing = _torrentService.GetByInfoHash(parsed.InfoHash);
        if (existing != null)
        {
            _logger.Info("Torrent already exists with info hash {0}, merging trackers", parsed.InfoHash);
            var existingTrackers = _trackerEntryService.GetByTorrentId(existing.Id);
            var existingUrls = new HashSet<string>(existingTrackers.Select(t => t.Url), StringComparer.OrdinalIgnoreCase);

            if (parsed.AnnounceList != null && parsed.AnnounceList.Count > 0)
            {
                for (var tier = 0; tier < parsed.AnnounceList.Count; tier++)
                {
                    foreach (var url in parsed.AnnounceList[tier])
                    {
                        if (!string.IsNullOrWhiteSpace(url) && existingUrls.Add(url))
                        {
                            _trackerEntryService.Add(new TrackerEntry
                            {
                                TorrentId = existing.Id,
                                Url = url,
                                Tier = tier,
                                Status = TrackerStatus.Unknown,
                                Enabled = true
                            });
                        }
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(parsed.AnnounceUrl) && existingUrls.Add(parsed.AnnounceUrl))
            {
                _trackerEntryService.Add(new TrackerEntry
                {
                    TorrentId = existing.Id,
                    Url = parsed.AnnounceUrl,
                    Tier = 0,
                    Status = TrackerStatus.Unknown,
                    Enabled = true
                });
            }

            return;
        }

        var torrent = new Torrent
        {
            Name = parsed.Name,
            InfoHash = parsed.InfoHash,
            TotalSize = parsed.TotalSize,
            PieceCount = parsed.PieceCount,
            PieceLength = parsed.PieceLength,
            PieceHashes = parsed.PieceHashes,
            Comment = parsed.Comment,
            CreatedBy = parsed.CreatedBy,
            CreationDate = parsed.CreationDate,
            IsPrivate = parsed.IsPrivate,
            TrackerUrl = parsed.AnnounceUrl,
            SourcePath = command.FilePath,
            DateAdded = DateTime.UtcNow,
            Progress = command.Progress ?? 0.0,
            ForceCompleted = command.ForceCompleted ?? false
        };

        var initialStatus = TorrentStatus.Stopped;
        if (_configService.AutoStart)
        {
            initialStatus = (torrent.Progress >= 1.0 || torrent.ForceCompleted)
                ? TorrentStatus.Seeding
                : TorrentStatus.Downloading;
        }

        torrent.Status = initialStatus;

        if (parsed.Files != null && parsed.Files.Count > 0)
        {
            var pieceLength = parsed.PieceLength > 0 ? (long)parsed.PieceLength : 0L;
            var runningByteOffset = 0L;
            var files = new List<TorrentFile>();

            foreach (var f in parsed.Files)
            {
                var (pieceOffset, pieceCount) = TorrentPieceCalculator.CalculateForFile(runningByteOffset, f.Size, pieceLength);
                if (pieceLength > 0)
                {
                    runningByteOffset += f.Size;
                }

                files.Add(new TorrentFile
                {
                    Path = f.Path,
                    Size = f.Size,
                    PieceOffset = pieceOffset,
                    PieceCount = pieceCount,
                    IsPaddingFile = f.IsPaddingFile
                });
            }

            torrent.Files = files;
        }

        var added = _torrentService.Add(torrent);

        if (_torrentFileService != null && torrent.Files != null && torrent.Files.Count > 0)
        {
            foreach (var file in torrent.Files)
            {
                _torrentFileService.Add(new TorrentFile
                {
                    TorrentId = added.Id,
                    Path = file.Path,
                    Size = file.Size,
                    PieceOffset = file.PieceOffset,
                    PieceCount = file.PieceCount,
                    IsPaddingFile = file.IsPaddingFile
                });
            }
        }

        var urls = new System.Collections.Generic.HashSet<string>();

        if (parsed.AnnounceList?.Count > 0)
        {
            for (var tier = 0; tier < parsed.AnnounceList.Count; tier++)
            {
                foreach (var url in parsed.AnnounceList[tier])
                {
                    if (string.IsNullOrWhiteSpace(url) || !urls.Add(url))
                    {
                        continue;
                    }

                    _trackerEntryService.Add(new TrackerEntry
                    {
                        TorrentId = added.Id,
                        Url = url,
                        Tier = tier,
                        Status = TrackerStatus.Unknown,
                        Enabled = true,
                        AnnounceInterval = _configService.AnnounceIntervalSeconds,
                        MinAnnounceInterval = _configService.MinAnnounceIntervalSeconds
                    });
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(parsed.AnnounceUrl))
        {
            _trackerEntryService.Add(new TrackerEntry
            {
                TorrentId = added.Id,
                Url = parsed.AnnounceUrl,
                Tier = 0,
                Status = TrackerStatus.Unknown,
                Enabled = true,
                AnnounceInterval = _configService.AnnounceIntervalSeconds,
                MinAnnounceInterval = _configService.MinAnnounceIntervalSeconds
            });
        }

        _logger.Info("Added torrent: {0} ({1})", added.Name, added.InfoHash);
    }
}
