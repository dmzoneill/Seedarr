using System;
using System.Net.Http;
using NzbDrone.Core.Http;
using Polly;

namespace NzbDrone.Core.ArrIntegration;

internal static class ArrConnectionResources
{
    internal static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    });

    internal static readonly ResiliencePipeline SharedPolicy = ResiliencePolicies.GetArrApiPolicy();
}
