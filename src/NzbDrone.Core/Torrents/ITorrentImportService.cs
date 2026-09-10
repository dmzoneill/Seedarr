using System.IO;

namespace NzbDrone.Core.Torrents;

public interface ITorrentImportService
{
    Torrent ImportFromFile(Stream stream, string fileName);
    Torrent ImportFromMagnet(string magnetLink);
}
