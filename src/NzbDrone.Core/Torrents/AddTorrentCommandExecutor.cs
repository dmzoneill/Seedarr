using System;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Torrents;

public class AddTorrentCommandExecutor : IExecute<AddTorrentCommand>
{
    private readonly ITorrentFileParser _parser;
    private readonly ITorrentService _torrentService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public AddTorrentCommandExecutor(
        ITorrentFileParser parser,
        ITorrentService torrentService,
        ITrackerEntryService trackerEntryService,
        IConfigService configService)
    {
        _parser = parser;
        _torrentService = torrentService;
        _trackerEntryService = trackerEntryService;
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

        if (_torrentService.ExistsByInfoHash(parsed.InfoHash))
        {
            _logger.Info("Torrent already exists with info hash {0}, skipping", parsed.InfoHash);
            return;
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
            SourcePath = command.FilePath,
            DateAdded = DateTime.UtcNow,
            Status = _configService.AutoStart ? TorrentStatus.Seeding : TorrentStatus.Stopped,
            Progress = 0.0
        };

        var added = _torrentService.Add(torrent);

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
