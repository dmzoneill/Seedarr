using System.Collections.Generic;
using NLog;

namespace NzbDrone.Core.Torrents;

public interface ITorrentFileService
{
    List<TorrentFile> GetByTorrentId(int torrentId);
    TorrentFile Add(TorrentFile torrentFile);
    void DeleteByTorrentId(int torrentId);
}

public class TorrentFileService : ITorrentFileService
{
    private readonly ITorrentFileRepository _repository;
    private readonly Logger _logger;

    public TorrentFileService(ITorrentFileRepository repository)
    {
        _repository = repository;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<TorrentFile> GetByTorrentId(int torrentId)
    {
        return _repository.GetByTorrentId(torrentId);
    }

    public TorrentFile Add(TorrentFile torrentFile)
    {
        return _repository.Insert(torrentFile);
    }

    public void DeleteByTorrentId(int torrentId)
    {
        _repository.DeleteByTorrentId(torrentId);
    }
}
