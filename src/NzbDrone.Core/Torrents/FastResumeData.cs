using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.Torrents;

public class FastResumeFileEntry
{
    public string Path { get; set; }

    public long Length { get; set; }

    [JsonIgnore]
    public long Size
    {
        get => Length;
        set => Length = value;
    }

    public DateTime? Mtime { get; set; }

    [JsonIgnore]
    public DateTime? LastWriteTimeUtc
    {
        get => Mtime;
        set => Mtime = value;
    }
}

public class FastResumeData
{
    public string InfoHash { get; set; }

    public bool[] Bitfield { get; set; }

    public long Uploaded { get; set; }

    public long Downloaded { get; set; }

    public double Progress { get; set; }

    public string Status { get; set; }

    public DateTime? SavedAt { get; set; }

    public string SavePath { get; set; }

    public List<FastResumeFileEntry> Files { get; set; } = new();
}
