using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.DownloadClients;

public class DownloadClientTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; }

    public static DownloadClientTestResult Ok(string message = "Connection successful") => new() { Success = true, Message = message };
    public static DownloadClientTestResult Fail(string message) => new() { Success = false, Message = message };
}

public interface IDownloadClient : IProvider
{
    string ClientType { get; }
    List<DownloadClientItem> GetItems();
    byte[] GetTorrentFile(string infoHash);
    bool AddTrackers(string infoHash, IEnumerable<string> trackers);
    bool TestConnection();
    DownloadClientTestResult TestConnectionDetailed();
}
