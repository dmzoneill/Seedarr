using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Update;

public interface IInstallUpdateService
{
    bool IsContainerized { get; }

    UpdateInstallProgress GetProgress();

    Task<bool> InstallUpdateAsync(string version = null, CancellationToken cancellationToken = default);
}
