using System;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Backup;

public enum BackupType
{
    Scheduled = 0,
    Manual = 1,
    Update = 2
}

public class BackupCreatedEvent : IEvent
{
    public string Path { get; set; }

    public string FileName { get; set; }

    public BackupType Type { get; set; }

    public long Size { get; set; }

    public BackupCreatedEvent()
    {
    }

    public BackupCreatedEvent(string path, string fileName, BackupType type, long size = 0)
    {
        Path = path;
        FileName = fileName;
        Type = type;
        Size = size;
    }
}

public class BackupCompletedEvent : IEvent
{
    public string BackupFileName { get; set; }

    public long SizeBytes { get; set; }

    public long DurationMs { get; set; }

    public BackupCompletedEvent()
    {
    }

    public BackupCompletedEvent(string backupFileName, long sizeBytes, long durationMs)
    {
        BackupFileName = backupFileName;
        SizeBytes = sizeBytes;
        DurationMs = durationMs;
    }
}

public class BackupFailedEvent : IEvent
{
    public Backup.BackupType Type { get; set; }

    public string ErrorMessage { get; set; }

    public Exception Exception { get; set; }

    public string BackupType => Type.ToString();

    public BackupFailedEvent()
    {
    }

    public BackupFailedEvent(string errorMessage, Exception exception = null, Backup.BackupType type = Backup.BackupType.Manual)
    {
        ErrorMessage = errorMessage;
        Exception = exception;
        Type = type;
    }

    public BackupFailedEvent(Backup.BackupType type, string errorMessage, Exception exception = null)
    {
        Type = type;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    public BackupFailedEvent(string backupType, string errorMessage)
    {
        ErrorMessage = errorMessage;
        if (Enum.TryParse<Backup.BackupType>(backupType, true, out var parsed))
        {
            Type = parsed;
        }
        else
        {
            Type = Backup.BackupType.Manual;
        }
    }
}
