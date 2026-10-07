namespace NzbDrone.Core.Blocklist;

public enum BlocklistArchiveFormat
{
    PlainText,
    GZip,
    Deflate,
    Brotli,
    Zip
}
