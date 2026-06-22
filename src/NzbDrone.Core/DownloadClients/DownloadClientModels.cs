using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.DownloadClients;

public class DownloadClientDefinition : ProviderDefinition
{
    public string ClientType { get; set; }
    public string Host { get; set; }
    public int Port { get; set; }
    public bool UseSsl { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Category { get; set; }
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
}
