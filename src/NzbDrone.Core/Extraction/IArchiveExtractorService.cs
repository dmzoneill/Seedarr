using System.Threading.Tasks;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Extraction;

public interface IArchiveExtractorService
{
    Task<ArchiveExtractionResult> ExtractTorrentArchiveAsync(Torrent torrent, string destination = null, bool deleteArchive = false);

    bool IsPrimaryArchive(string filePath);
}
