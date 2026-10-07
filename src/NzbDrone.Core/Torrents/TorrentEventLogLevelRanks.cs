namespace NzbDrone.Core.Torrents;

public static class TorrentEventLogLevelRanks
{
    public const int Trace = 0;
    public const int Debug = 1;
    public const int Info = 2;
    public const int Warn = 3;
    public const int Error = 4;
    public const int Fatal = 5;

    // SQLite / PostgreSQL expression matching API level ordering (see Seedarr.Api.V1.Torrents.LevelRank).
    public const string SqlRankExpression = @"
        CASE lower(""Level"")
            WHEN 'trace' THEN 0
            WHEN 'debug' THEN 1
            WHEN 'info' THEN 2
            WHEN 'warn' THEN 3
            WHEN 'warning' THEN 3
            WHEN 'error' THEN 4
            WHEN 'fatal' THEN 5
            ELSE -1
        END";

    public static int? GetRank(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return null;
        }

        return level.Trim().ToLowerInvariant() switch
        {
            "trace" => Trace,
            "debug" => Debug,
            "info" => Info,
            "warn" or "warning" => Warn,
            "error" => Error,
            "fatal" => Fatal,
            _ => null
        };
    }
}
