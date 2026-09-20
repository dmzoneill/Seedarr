namespace NzbDrone.Core.Blocklist;

public class BlocklistArchiveStreamOptions
{
    public const int DefaultBufferSize = 64 * 1024; // 64 KB working buffer
    public const long DefaultMaxUncompressedBytes = 512L * 1024 * 1024; // 512 MB quota limit
    public const int DefaultMaxRuleLines = 2_000_000; // 2 million rule lines

    public int BufferSize { get; set; } = DefaultBufferSize;
    public long MaxUncompressedBytes { get; set; } = DefaultMaxUncompressedBytes;
    public int MaxRuleLines { get; set; } = DefaultMaxRuleLines;
}
