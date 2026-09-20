using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.MediaInspection;

public interface IFFprobeMediaInspector
{
    bool IsAvailable();

    MediaContainerInfo Inspect(string filePath);

    Task<MediaContainerInfo> InspectAsync(string filePath, CancellationToken cancellationToken = default);

    MediaContainerInfo ParseFfprobeJson(string json, string filePath = null);
}
