using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Packages;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Torrents.Package;

public class TorrentPackageService : ITorrentPackageService
{
    private readonly ITorrentService _torrentService;
    private readonly IPackageExportService _packageExportService;
    private readonly IPackageImportService _packageImportService;
    private readonly Logger _logger;

    public TorrentPackageService(
        ITorrentService torrentService,
        IPackageExportService packageExportService = null,
        IPackageImportService packageImportService = null,
        ITorrentFileService torrentFileService = null,
        IFastResumeService fastResumeService = null,
        IFastResumeBencodeSerializer bencodeSerializer = null,
        ITagService tagService = null,
        ITrackerEntryService trackerEntryService = null,
        IAppFolderInfo appFolderInfo = null,
        ISyntheticMetadataGenerator syntheticMetadataGenerator = null,
        ITorrentImportService torrentImportService = null,
        IDiskProvider diskProvider = null)
    {
        _torrentService = torrentService ?? throw new ArgumentNullException(nameof(torrentService));

        _packageExportService = packageExportService ?? new PackageExportService(
            torrentService,
            torrentFileService,
            fastResumeService,
            bencodeSerializer,
            tagService,
            appFolderInfo,
            syntheticMetadataGenerator,
            trackerEntryService);

        _packageImportService = packageImportService ?? new PackageImportService(
            torrentService,
            torrentImportService,
            fastResumeService,
            appFolderInfo,
            diskProvider,
            trackerEntryService);

        _logger = LogManager.GetCurrentClassLogger();
    }

    public Task ExportPackageAsync(
        Stream outputStream,
        IEnumerable<int> torrentIds,
        bool includePayload,
        CancellationToken cancellationToken = default)
    {
        _logger.Info("Starting torrent package export for torrents: {0} (includePayload: {1})",
            string.Join(", ", torrentIds ?? Array.Empty<int>()), includePayload);

        return _packageExportService.ExportPackageAsync(outputStream, torrentIds, includePayload, cancellationToken);
    }

    public Task ExportPackageAsync(
        Stream outputStream,
        int torrentId,
        bool includePayload,
        CancellationToken cancellationToken = default)
    {
        return ExportPackageAsync(outputStream, new[] { torrentId }, includePayload, cancellationToken);
    }

    public Task<PackageImportResult> ImportPackageAsync(
        Stream archiveStream,
        PackageImportOptions options = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Info("Starting torrent package import (skipDuplicates: {0})",
            options?.SkipDuplicates ?? true);

        return _packageImportService.ImportPackageAsync(archiveStream, options, cancellationToken);
    }
}
