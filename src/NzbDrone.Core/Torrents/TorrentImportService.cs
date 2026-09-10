using System;
using System.IO;
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
                    Size = f.Size
                });
            }
        }

        if (parsed.AnnounceList != null)
        {
            var tier = 0;
            foreach (var tierUrls in parsed.AnnounceList)
            {
                foreach (var url in tierUrls)
                {
                    _trackerEntryService.Add(new TrackerEntry
                    {
                        TorrentId = addedTorrent.Id,
                        Url = url,
                        Tier = tier,
                        Enabled = true
                    });
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
            foreach (var url in parsed.Trackers)
            {
                _trackerEntryService.Add(new TrackerEntry
                {
                    TorrentId = added.Id,
                    Url = url,
                    Tier = tier++,
                    Enabled = true
                });
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

        if (_torrentService.ExistsByInfoHash(infoHash))
        {
            throw new InvalidOperationException("Torrent with this info hash already exists");
        }
    }
}
