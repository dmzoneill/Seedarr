namespace Seedarr.Http;

public static class RequestBodyLimits
{
    public const long Default = 64L * 1024 * 1024;
    public const long TorrentUpload = 10L * 1024 * 1024;
    public const long PackageImport = 256L * 1024 * 1024;
}
