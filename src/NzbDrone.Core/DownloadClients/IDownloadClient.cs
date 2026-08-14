using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.DownloadClients;

public interface IDownloadClient : IProvider
{
    string ClientType { get; }
    List<DownloadClientItem> GetItems();
    byte[] GetTorrentFile(string infoHash);
    bool TestConnection();
}
