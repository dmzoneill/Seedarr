using System;

namespace NzbDrone.Core.Backup;

public class BackupInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    public long Size { get; set; }
    public DateTime Time { get; set; }
    public bool IsConfigOnly { get; set; }
}

public class BackupManifest
{
    public string Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public string DatabaseType { get; set; }
    public bool IsConfigOnly { get; set; }
    public string Warning { get; set; }
}
