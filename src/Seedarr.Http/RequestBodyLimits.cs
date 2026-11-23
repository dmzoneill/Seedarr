namespace Seedarr.Http;

public static class RequestBodyLimits
{
    public const long Default = 64L * 1024 * 1024;

    // Each .torrent file in a multipart upload may be up to 10 MiB (the parser cap).
    // MultipartBodyLengthLimit is enforced per form section, so this is a per-file limit.
    public const long TorrentUploadPerFile = 10L * 1024 * 1024;

    // The whole multi-file upload request may carry several torrents, so it gets the global default.
    public const long TorrentUploadRequest = Default;

    public const long PackageImport = 256L * 1024 * 1024;
}
