using System.Collections.Generic;

namespace NzbDrone.Core.Update;

public interface IUpdatePackageProvider
{
    string CurrentPlatform { get; }
    string UpdateMechanism { get; }
    bool IsDocker { get; }
    UpdatePackage ResolvePackage(IEnumerable<ReleaseAsset> assets);
    UpdatePackage ResolvePackage(IEnumerable<ReleaseAsset> assets, string platform);
    bool IsReleaseApplicable(bool isPrerelease, string channel);
}
