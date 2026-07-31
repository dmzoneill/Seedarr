using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Torrents;

public interface ITrackerEntryService
{
    List<TrackerEntry> All();
    List<TrackerEntry> GetByTorrentId(int torrentId);
    TrackerEntry Add(TrackerEntry trackerEntry);
    TrackerEntry Update(TrackerEntry trackerEntry);
    void Delete(int id);
    void DeleteByTorrentId(int torrentId);
}

public class TrackerEntryService : ITrackerEntryService
{
    private readonly ITrackerEntryRepository _repository;
    private readonly Logger _logger;

    public TrackerEntryService(ITrackerEntryRepository repository)
    {
        _repository = repository;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<TrackerEntry> All()
    {
        return _repository.All().ToList();
    }

    public List<TrackerEntry> GetByTorrentId(int torrentId)
    {
        return _repository.GetByTorrentId(torrentId);
    }

    public TrackerEntry Add(TrackerEntry trackerEntry)
    {
        _logger.Debug("Adding tracker entry: {0} for torrent {1}", RedactUrl(trackerEntry.Url), trackerEntry.TorrentId);
        return _repository.Insert(trackerEntry);
    }

    public TrackerEntry Update(TrackerEntry trackerEntry)
    {
        return _repository.Update(trackerEntry);
    }

    public void Delete(int id)
    {
        _repository.Delete(id);
    }

    private static string RedactUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
        {
            return url;
        }

        return uri.GetComponents(
            UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
    }

    public void DeleteByTorrentId(int torrentId)
    {
        _logger.Debug("Deleting all tracker entries for torrent {0}", torrentId);
        _repository.DeleteByTorrentId(torrentId);
    }
}
