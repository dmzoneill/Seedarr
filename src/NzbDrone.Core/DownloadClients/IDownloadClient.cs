using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    List<string> GetTrackers(string infoHash);
    bool AddTrackers(string infoHash, IEnumerable<string> trackers);
    bool Reannounce(string infoHash);
    bool PauseTorrent(string infoHash);
    bool ResumeTorrent(string infoHash);
    bool DeleteTorrent(string infoHash, bool deleteData = false);
    bool TestConnection();
    DownloadClientTestResult TestConnectionDetailed();
    Task<DownloadClientSpeedLimits> GetSpeedLimitsAsync(CancellationToken cancellationToken = default);
    Task SetSpeedLimitsAsync(long? uploadBps, long? downloadBps, CancellationToken cancellationToken = default);
    Task SetTorrentLimitsAsync(string infoHash, long? uploadBps, long? downloadBps, CancellationToken cancellationToken = default);
}
