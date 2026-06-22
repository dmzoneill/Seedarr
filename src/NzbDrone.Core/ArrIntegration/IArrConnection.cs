using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ArrIntegration;

public interface IArrConnection : IProvider
{
    string ArrType { get; }
    string Url { get; set; }
    string ApiKey { get; set; }
    List<ArrDownloadRecord> GetDownloadHistory();
    bool TestConnection();
}
