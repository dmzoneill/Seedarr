using System.Collections.Generic;
using NzbDrone.Core.Categories;

namespace NzbDrone.Core.Torrents;

public interface IQueueService
{
    bool CanDownload(Torrent torrent, IEnumerable<Torrent> activeTorrents, int? globalMaxActiveDownloads = null);
    List<Torrent> EvaluateDownloadQueue(IEnumerable<Torrent> queuedTorrents, IEnumerable<Torrent> activeTorrents, int? globalMaxActiveDownloads = null);
    List<Torrent> EvaluateDownloadQueue(IEnumerable<Torrent> allTorrents, int? globalMaxActiveDownloads = null);
}

public class QueueService : IQueueService
{
    private readonly ICategoryService _categoryService;

    public QueueService(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    public bool CanDownload(Torrent torrent, IEnumerable<Torrent> activeTorrents, int? globalMaxActiveDownloads = null)
    {
        return _categoryService.CanDownload(torrent, activeTorrents, globalMaxActiveDownloads);
    }

    public List<Torrent> EvaluateDownloadQueue(IEnumerable<Torrent> queuedTorrents, IEnumerable<Torrent> activeTorrents, int? globalMaxActiveDownloads = null)
    {
        return _categoryService.EvaluateDownloadQueue(queuedTorrents, activeTorrents, globalMaxActiveDownloads);
    }

    public List<Torrent> EvaluateDownloadQueue(IEnumerable<Torrent> allTorrents, int? globalMaxActiveDownloads = null)
    {
        return _categoryService.EvaluateDownloadQueue(allTorrents, globalMaxActiveDownloads);
    }
}
