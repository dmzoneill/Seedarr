using System;

namespace Seedarr.Api.V1.Torrents;

public static class LevelRank
{
    public const int Trace = 0;
    public const int Debug = 1;
    public const int Info = 2;
    public const int Warn = 3;
    public const int Error = 4;
    public const int Fatal = 5;
}

public class TorrentEventLogResource
{
    public int Id { get; set; }
    public int TorrentId { get; set; }
    public DateTime TimeStamp { get; set; }
    public string Level { get; set; }
    public string Source { get; set; }
    public string Message { get; set; }
}
