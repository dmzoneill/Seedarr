using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ArrIntegration;

public interface IArrConnection : IProvider
{
    string ArrType { get; }
    List<ArrDownloadRecord> GetDownloadHistory();
    bool TestConnection();
}
