using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Packages;

public class PackageManifest
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string Client { get; set; } = "Seedarr";
    public List<PackageTorrentItem> Torrents { get; set; } = new();
}

public class PackageTorrentItem
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string InfoHash { get; set; }
    public string Category { get; set; }
    public List<string> Tags { get; set; } = new();
    public long TotalSize { get; set; }
    public int PieceCount { get; set; }
    public int PieceLength { get; set; }
    public bool IsPrivate { get; set; }
}
