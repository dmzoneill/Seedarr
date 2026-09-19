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

public class FastResumeUnfinishedPiece
{
    public int Piece { get; set; }

    public byte[] Bitmask { get; set; }

    public uint? Adler32 { get; set; }
}

public class FastResumeData
{
    public string FileFormat { get; set; } = "libtorrent resume file";

    public int FileVersion { get; set; } = 1;

    public string InfoHash { get; set; }

    public bool[] Bitfield { get; set; }

    public long Uploaded { get; set; }

    public long Downloaded { get; set; }

    public double Progress { get; set; }

    public string Status { get; set; }

    public DateTime? SavedAt { get; set; }

    public string SavePath { get; set; }

    public long ActiveTime { get; set; }

    public long SeedingTime { get; set; }

    public long FinishedTime { get; set; }

    public bool SequentialDownload { get; set; }

    public string Allocation { get; set; } = "sparse";

    public List<int> FilePriority { get; set; } = new();

    public byte[] PiecePriority { get; set; }

    public List<FastResumeUnfinishedPiece> Unfinished { get; set; } = new();

    public List<FastResumeFileEntry> Files { get; set; } = new();
}
