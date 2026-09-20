using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Torrents;

public class TorrentImportService : ITorrentImportService
{
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly Logger _logger;

    public TorrentImportService(
        ITorrentFileParser torrentFileParser,
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITrackerEntryService trackerEntryService,
        ITorrentEventLogService eventLogService)
    {
        _torrentFileParser = torrentFileParser;
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _trackerEntryService = trackerEntryService;
        _eventLogService = eventLogService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public Torrent ImportFromFile(Stream stream, string fileName)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var parsed = _torrentFileParser.Parse(stream);
        if (parsed == null)
        {
            throw new InvalidOperationException("Failed to parse torrent file");
        }

        var primaryHash = parsed.InfoHash ?? parsed.InfoHashV2;
        ValidateInfoHash(primaryHash);

        var existing = _torrentService.GetByInfoHash(primaryHash);
        if (existing != null)
        {
            var existingTrackers = _trackerEntryService.GetByTorrentId(existing.Id);
            var existingUrls = new HashSet<string>(existingTrackers.Select(t => t.Url), StringComparer.OrdinalIgnoreCase);
            var trackersToMerge = new List<TrackerEntry>();

            if (parsed.AnnounceList != null && parsed.AnnounceList.Count > 0)
            {
                for (var tier = 0; tier < parsed.AnnounceList.Count; tier++)
                {
                    foreach (var url in parsed.AnnounceList[tier])
                    {
                        if (!string.IsNullOrWhiteSpace(url) && existingUrls.Add(url))
                        {
                            trackersToMerge.Add(new TrackerEntry
                            {
                                TorrentId = existing.Id,
                                Url = url,
                                Tier = tier,
                                Enabled = true,
                            });
                        }
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(parsed.AnnounceUrl) && existingUrls.Add(parsed.AnnounceUrl))
            {
                trackersToMerge.Add(new TrackerEntry
                {
                    TorrentId = existing.Id,
                    Url = parsed.AnnounceUrl,
                    Tier = 0,
                    Enabled = true,
                });
            }

            if (trackersToMerge.Count > 0)
            {
                _trackerEntryService.AddMany(trackersToMerge);
            }

            _eventLogService.Info(existing.Id, "Update", $"Torrent '{existing.Name}' updated with new trackers from file '{fileName}'");
            return existing;
        }

        var torrent = new Torrent
        {
            Name = parsed.Name,
            InfoHash = parsed.InfoHash ?? parsed.InfoHashV2,
            InfoHashV2 = parsed.InfoHashV2,
            TotalSize = parsed.TotalSize,
            PieceCount = parsed.PieceCount,
            PieceLength = parsed.PieceLength,
            PieceHashes = parsed.PieceHashes,
            Comment = parsed.Comment,
            CreatedBy = parsed.CreatedBy,
            CreationDate = parsed.CreationDate,
            IsPrivate = parsed.IsPrivate,
            TrackerUrl = parsed.AnnounceUrl,
            Status = TorrentStatus.Queued,
            DateAdded = DateTime.UtcNow
        };

        var filesToAdd = new List<TorrentFile>();
        if (parsed.Files != null && parsed.Files.Count > 0)
        {
            var pieceLength = parsed.PieceLength > 0 ? (long)parsed.PieceLength : 0L;
            var runningByteOffset = 0L;

            foreach (var f in parsed.Files)
            {
                var (pieceOffset, pieceCount) = TorrentPieceCalculator.CalculateForFile(runningByteOffset, f.Size, pieceLength);
                if (pieceLength > 0)
                {
                    runningByteOffset += f.Size;
                }

                filesToAdd.Add(new TorrentFile
                {
                    Path = f.Path,
                    Size = f.Size,
                    PieceOffset = pieceOffset,
                    PieceCount = pieceCount,
                    IsPaddingFile = f.IsPaddingFile
                });
            }
        }

        var trackersToAdd = new List<TrackerEntry>();
        if (parsed.AnnounceList != null)
        {
            var tier = 0;
            var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tierUrls in parsed.AnnounceList)
            {
                foreach (var url in tierUrls)
                {
                    if (!string.IsNullOrWhiteSpace(url) && addedUrls.Add(url))
                    {
                        trackersToAdd.Add(new TrackerEntry
                        {
                            Url = url,
                            Tier = tier,
                            Enabled = true
                        });
                    }
                }

                tier++;
            }
        }
        else if (!string.IsNullOrEmpty(parsed.AnnounceUrl))
        {
            trackersToAdd.Add(new TrackerEntry
            {
                Url = parsed.AnnounceUrl,
                Tier = 0,
                Enabled = true
            });
        }

        Torrent addedTorrent = null;
        try
        {
            addedTorrent = _torrentService.Add(torrent);

            var streamLength = 0L;
            try
            {
                if (stream.CanSeek)
                {
                    streamLength = stream.Length;
                }
            }
            catch
            {
                // ignored
            }

            _eventLogService.Info(addedTorrent.Id, "Add", $"Torrent '{parsed.Name}' added from file '{fileName}' ({streamLength} bytes)");

            if (filesToAdd.Count > 0)
            {
                foreach (var f in filesToAdd)
                {
                    f.TorrentId = addedTorrent.Id;
                }

                _torrentFileService.AddMany(filesToAdd);
            }

            if (trackersToAdd.Count > 0)
            {
                foreach (var t in trackersToAdd)
                {
                    t.TorrentId = addedTorrent.Id;
                }

                _trackerEntryService.AddMany(trackersToAdd);
            }

            return addedTorrent;
        }
        catch
        {
            if (addedTorrent != null)
            {
                try
                {
                    _torrentService.Delete(addedTorrent.Id, false);
                }
                catch (Exception cleanupEx)
                {
                    _logger.Warn(cleanupEx, "Failed to clean up torrent {0} after file import error", addedTorrent.Id);
                }
            }

            throw;
        }
    }

    public Torrent ImportFromMagnet(string magnetLink)
    {
        if (string.IsNullOrWhiteSpace(magnetLink))
        {
            throw new ArgumentException("Magnet link is required", nameof(magnetLink));
        }

        var parsed = MagnetLinkParser.Parse(magnetLink);

        var primaryHash = parsed.InfoHash ?? parsed.InfoHashV2;
        ValidateInfoHash(primaryHash);

        var existing = _torrentService.GetByInfoHash(primaryHash);
        if (existing != null)
        {
            var existingTrackers = _trackerEntryService.GetByTorrentId(existing.Id);
            var existingUrls = new HashSet<string>(existingTrackers.Select(t => t.Url), StringComparer.OrdinalIgnoreCase);

            if (parsed.Trackers != null && parsed.Trackers.Length > 0)
            {
                var trackersToMerge = new List<TrackerEntry>();
                var tier = 0;
                foreach (var url in parsed.Trackers)
                {
                    if (!string.IsNullOrWhiteSpace(url) && existingUrls.Add(url))
                    {
                        trackersToMerge.Add(new TrackerEntry
                        {
                            TorrentId = existing.Id,
                            Url = url,
                            Tier = tier,
                            Enabled = true
                        });
                    }

                    tier++;
                }

                if (trackersToMerge.Count > 0)
                {
                    _trackerEntryService.AddMany(trackersToMerge);
                }
            }

            _eventLogService.Info(existing.Id, "Update", $"Torrent '{existing.Name}' updated with new trackers from magnet link");
            return existing;
        }

        var torrent = new Torrent
        {
            Name = parsed.Name,
            InfoHash = parsed.InfoHash ?? parsed.InfoHashV2,
            InfoHashV2 = parsed.InfoHashV2,
            TrackerUrl = parsed.Trackers != null && parsed.Trackers.Length > 0 ? parsed.Trackers[0] : null,
            Status = TorrentStatus.Queued,
            DateAdded = DateTime.UtcNow
        };

        var trackersToAddForNewTorrent = new List<TrackerEntry>();
        if (parsed.Trackers != null && parsed.Trackers.Length > 0)
        {
            var tier = 0;
            var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var url in parsed.Trackers)
            {
                if (!string.IsNullOrWhiteSpace(url) && addedUrls.Add(url))
                {
                    trackersToAddForNewTorrent.Add(new TrackerEntry
                    {
                        Url = url,
                        Tier = tier,
                        Enabled = true
                    });
                }

                tier++;
            }
        }

        Torrent added = null;
        try
        {
            added = _torrentService.Add(torrent);
            _eventLogService.Info(added.Id, "Add", $"Torrent '{parsed.Name}' added from magnet link");

            if (trackersToAddForNewTorrent.Count > 0)
            {
                foreach (var tracker in trackersToAddForNewTorrent)
                {
                    tracker.TorrentId = added.Id;
                }

                _trackerEntryService.AddMany(trackersToAddForNewTorrent);
            }

            return added;
        }
        catch
        {
            if (added != null)
            {
                try
                {
                    _torrentService.Delete(added.Id, false);
                }
                catch (Exception cleanupEx)
                {
                    _logger.Warn(cleanupEx, "Failed to clean up torrent {0} after magnet import error", added.Id);
                }
            }

            throw;
        }
    }

    private void ValidateInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            throw new ArgumentException("Info hash is required", nameof(infoHash));
        }
    }
}
