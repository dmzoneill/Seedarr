using System;

namespace NzbDrone.Core.Torrents;

public class FastResumeData
{
    public string InfoHash { get; set; }

    public bool[] Bitfield { get; set; }

    public long Uploaded { get; set; }

    public long Downloaded { get; set; }

    public double Progress { get; set; }

    public string Status { get; set; }

    public DateTime? SavedAt { get; set; }
}
