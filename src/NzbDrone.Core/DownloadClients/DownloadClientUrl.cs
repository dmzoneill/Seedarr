namespace NzbDrone.Core.DownloadClients;

public static class DownloadClientUrl
{
    public static string NormalizeUrlBase(string urlBase)
    {
        if (string.IsNullOrWhiteSpace(urlBase))
        {
            return string.Empty;
        }

        var trimmed = urlBase.Trim().Trim('/');
        return string.IsNullOrEmpty(trimmed) ? string.Empty : "/" + trimmed;
    }

    public static string BuildRootUrl(bool useSsl, string host, int port, string urlBase)
    {
        var scheme = useSsl ? "https" : "http";
        return $"{scheme}://{host}:{port}{NormalizeUrlBase(urlBase)}";
    }
}
