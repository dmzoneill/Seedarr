using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Packages;

namespace NzbDrone.Core.Torrents.Package;

public interface ITorrentPackageService
{
    Task ExportPackageAsync(
        Stream outputStream,
        IEnumerable<int> torrentIds,
        bool includePayload,
        CancellationToken cancellationToken = default);

    Task<PackageImportResult> ImportPackageAsync(
        Stream archiveStream,
        PackageImportOptions options = null,
        CancellationToken cancellationToken = default);
}
