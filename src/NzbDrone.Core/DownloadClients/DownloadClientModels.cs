using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.DownloadClients;

public class DownloadClientDefinition : ProviderDefinition
{
    public string ClientType { get; set; }
    public string Host { get; set; }
    public int Port { get; set; }
    public bool UseSsl { get; set; }
    public string UrlBase { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Category { get; set; }
    public List<int> Tags { get; set; } = new();

    [Ignore]
    public bool? IsOnline { get; set; }

    [Ignore]
    public string Version { get; set; }

    [Ignore]
    public DateTime? LastSyncTime { get; set; }

    [Ignore]
    public string LastErrorMessage { get; set; }

    [Ignore]
    public int ConsecutiveFailures { get; set; }

    [Ignore]
    public DateTime? BackoffUntil { get; set; }

    public DownloadClientDefinition Clone() => (DownloadClientDefinition)MemberwiseClone();
}

public class DownloadClientItem
{
    public string DownloadId { get; set; }
    public string Title { get; set; }
    public string InfoHash { get; set; }
    public long TotalSize { get; set; }
    public long RemainingSize { get; set; }
    public string Status { get; set; }
    public string OutputPath { get; set; }
    public string Category { get; set; }
    public bool IsPrivate { get; set; }
    public long? DownloadSpeed { get; set; }
    public long? UploadSpeed { get; set; }
}

public class DownloadClientRemoteItem
{
    public string DownloadId { get; set; }
    public string Title { get; set; }
    public string InfoHash { get; set; }
    public long TotalSize { get; set; }
    public long RemainingSize { get; set; }
    public double Progress { get; set; }
    public string Status { get; set; }
    public string OutputPath { get; set; }
    public string Category { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsInLibrary { get; set; }
    public int? LibraryTorrentId { get; set; }
    public long? DownloadSpeed { get; set; }
    public long? UploadSpeed { get; set; }
}

public class DownloadClientImportRequest
{
    public System.Collections.Generic.List<string> InfoHashes { get; set; } = new();
}

public class BatchImportItemResult
{
    public string InfoHash { get; set; }
    public string Title { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
}

public class BatchImportResponse
{
    public int Added { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<BatchImportItemResult> Items { get; set; } = new();
}

public class DownloadClientSpeedLimits
{
    public long? UploadLimitBps { get; set; }
    public long? DownloadLimitBps { get; set; }
    public long? CurrentUploadRateBps { get; set; }
    public long? CurrentDownloadRateBps { get; set; }
}
