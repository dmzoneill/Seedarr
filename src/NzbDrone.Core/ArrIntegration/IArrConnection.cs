using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ArrIntegration;

public class ArrTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; }

    public static ArrTestResult Ok(string message = "Connection successful") => new() { Success = true, Message = message };
    public static ArrTestResult Fail(string message) => new() { Success = false, Message = message };
}

public interface IArrConnection : IProvider
{
    int ConnectionId { get; set; }
    string ArrType { get; }
    string Url { get; set; }
    string ApiKey { get; set; }
    bool AcceptInvalidCertificates { get; set; }
    List<ArrDownloadRecord> GetDownloadHistory(int pageSize = 250);
    Task<List<ArrDownloadRecord>> GetDownloadHistoryAsync(CancellationToken cancellationToken = default);
    Task<List<ArrDownloadRecord>> GetDownloadHistoryAsync(int pageSize, CancellationToken cancellationToken = default);
    MediaMetadata GetMediaDetails(int mediaId);
    Task<MediaMetadata> GetMediaDetailsAsync(int mediaId, CancellationToken cancellationToken = default);
    MediaMetadata LookupMedia(string title);
    Task<MediaMetadata> LookupMediaAsync(string title, CancellationToken cancellationToken = default);
    bool TestConnection();
    ArrTestResult TestConnectionDetailed();
}
