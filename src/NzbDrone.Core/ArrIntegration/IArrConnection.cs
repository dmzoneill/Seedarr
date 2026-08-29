using System.Collections.Generic;
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
    string ArrType { get; }
    string Url { get; set; }
    string ApiKey { get; set; }
    List<ArrDownloadRecord> GetDownloadHistory();
    MediaMetadata GetMediaDetails(int mediaId);
    bool TestConnection();
    ArrTestResult TestConnectionDetailed();
}
