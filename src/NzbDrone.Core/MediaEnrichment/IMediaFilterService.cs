using System.Collections.Generic;

namespace NzbDrone.Core.MediaEnrichment;

public interface IMediaFilterService
{
    bool IsSampleFile(string filePath, long fileSize = 0);

    bool IsClutterFile(string filePath);

    bool IsMediaFile(string filePath);

    string SelectPrimaryMediaFile(string path);

    IEnumerable<string> FilterMediaFiles(IEnumerable<string> filePaths);
}
