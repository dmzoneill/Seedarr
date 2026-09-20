using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Indexers;

public class ReleaseInfo
{
    public string Guid { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Comments { get; set; }
    public int IndexerId { get; set; }
    public string Indexer { get; set; }
    public long Size { get; set; }
    public int? Seeders { get; set; }
    public int? Leechers { get; set; }
    public DateTime? PublishDate { get; set; }
    public string DownloadUrl { get; set; }
    public string MagnetUrl { get; set; }
    public string InfoHash { get; set; }
    public List<string> Categories { get; set; } = new();
    public string Protocol { get; set; } = "torrent";
    public double? DownloadVolumeFactor { get; set; } = 1.0;
    public double? UploadVolumeFactor { get; set; } = 1.0;
    public bool IsFreeleech => DownloadVolumeFactor <= 0.001;
    public int? ResponseOffset { get; set; }
    public int? ResponseTotal { get; set; }
    public int CustomFormatScore { get; set; }
    public List<string> MatchedCustomFormats { get; set; } = new List<string>();

    private string _imdbId;

    public string ImdbId
    {
        get => _imdbId;
        set => _imdbId = NormalizeImdbId(value);
    }

    public int? TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public string Resolution { get; set; }
    public string VideoCodec { get; set; }
    public string AudioCodec { get; set; }
    public double? MinimumRatio { get; set; }
    public long? MinimumSeedTime { get; set; }

    public static string NormalizeImdbId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            return $"tt{trimmed.Substring(2)}";
        }

        if (long.TryParse(trimmed, out _))
        {
            return $"tt{trimmed}";
        }

        return trimmed;
    }
}
