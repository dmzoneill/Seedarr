using Microsoft.AspNetCore.Http;

namespace NzbDrone.Core.RemotePathMappings;

public interface ICallerHostResolver
{
    string ResolveHost(HttpContext httpContext);

    string ResolveFromHeaders(string xForwardedFor, string xRealIp, string xForwardedHost, string remoteIp = null);

    string TryResolveHostname(string ipAddress);
}
