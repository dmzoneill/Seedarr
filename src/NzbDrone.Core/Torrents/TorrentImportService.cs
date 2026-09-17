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

        ValidateInfoHash(parsed.InfoHash);

        var existing = _torrentService.GetByInfoHash(parsed.InfoHash);
        if (existing != null)
        {
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
                                Enabled = true,
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
                    Enabled = true,
                });
            }

            _eventLogService.Info(existing.Id, "Update", $"Torrent '{existing.Name}' updated with new trackers from file '{fileName}'");
            return existing;
        }

        var torrent = new Torrent
        {
            Name = parsed.Name,
            InfoHash = parsed.InfoHash,
            TotalSize = parsed.TotalSize,
            PieceCount = parsed.PieceCount,
            PieceLength = parsed.PieceLength,
            Comment = parsed.Comment,
            CreatedBy = parsed.CreatedBy,
            CreationDate = parsed.CreationDate,
            IsPrivate = parsed.IsPrivate,
            TrackerUrl = parsed.AnnounceUrl,
            Status = TorrentStatus.Queued,
            DateAdded = DateTime.UtcNow
        };

        var addedTorrent = _torrentService.Add(torrent);

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

        if (parsed.Files != null)
        {
            foreach (var f in parsed.Files)
            {
                _torrentFileService.Add(new TorrentFile
                {
                    TorrentId = addedTorrent.Id,
                    Path = f.Path,
                    Size = f.Size,
                    IsPaddingFile = f.IsPaddingFile
                });
            }
        }

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
                        _trackerEntryService.Add(new TrackerEntry
                        {
                            TorrentId = addedTorrent.Id,
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
            _trackerEntryService.Add(new TrackerEntry
            {
                TorrentId = addedTorrent.Id,
                Url = parsed.AnnounceUrl,
                Tier = 0,
                Enabled = true
            });
        }

        return addedTorrent;
    }

    public Torrent ImportFromMagnet(string magnetLink)
    {
        if (string.IsNullOrWhiteSpace(magnetLink))
        {
            throw new ArgumentException("Magnet link is required", nameof(magnetLink));
        }

        var parsed = MagnetLinkParser.Parse(magnetLink);

        ValidateInfoHash(parsed.InfoHash);

        var existing = _torrentService.GetByInfoHash(parsed.InfoHash);
        if (existing != null)
        {
            var existingTrackers = _trackerEntryService.GetByTorrentId(existing.Id);
            var existingUrls = new HashSet<string>(existingTrackers.Select(t => t.Url), StringComparer.OrdinalIgnoreCase);

            if (parsed.Trackers != null && parsed.Trackers.Length > 0)
            {
                var tier = 0;
                foreach (var url in parsed.Trackers)
                {
                    if (!string.IsNullOrWhiteSpace(url) && existingUrls.Add(url))
                    {
                        _trackerEntryService.Add(new TrackerEntry
                        {
                            TorrentId = existing.Id,
                            Url = url,
                            Tier = tier,
                            Enabled = true
                        });
                    }

                    tier++;
                }
            }

            _eventLogService.Info(existing.Id, "Update", $"Torrent '{existing.Name}' updated with new trackers from magnet link");
            return existing;
        }

        var torrent = new Torrent
        {
            Name = parsed.Name,
            InfoHash = parsed.InfoHash,
            TrackerUrl = parsed.Trackers != null && parsed.Trackers.Length > 0 ? parsed.Trackers[0] : null,
            Status = TorrentStatus.Queued,
            DateAdded = DateTime.UtcNow
        };

        var added = _torrentService.Add(torrent);
        _eventLogService.Info(added.Id, "Add", $"Torrent '{parsed.Name}' added from magnet link");

        if (parsed.Trackers != null)
        {
            var tier = 0;
            var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var url in parsed.Trackers)
            {
                if (!string.IsNullOrWhiteSpace(url) && addedUrls.Add(url))
                {
                    _trackerEntryService.Add(new TrackerEntry
                    {
                        TorrentId = added.Id,
                        Url = url,
                        Tier = tier,
                        Enabled = true
                    });
                }

                tier++;
            }
        }

        return added;
    }

    private void ValidateInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            throw new ArgumentException("Info hash is required", nameof(infoHash));
        }
    }
}
