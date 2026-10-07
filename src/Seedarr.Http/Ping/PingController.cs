using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

namespace Seedarr.Http.Ping;

[AllowAnonymous]
[ApiController]
[Route("ping")]
public class PingController : ControllerBase
{
    private static readonly object ProbeLock = new();
    private static readonly TimeSpan ProbeCacheDuration = TimeSpan.FromSeconds(5);
    private static DateTime _lastProbeUtc = DateTime.MinValue;
    private static bool _lastProbeSucceeded;

    private readonly IBasicRepository<ConfigModel> _configRepository;

    public PingController(IBasicRepository<ConfigModel> configRepository)
    {
        _configRepository = configRepository;
    }

    [HttpGet]
    [HttpHead]
    [Produces("application/json")]
    public ActionResult<PingResource> Ping()
    {
        if (!IsDatastoreReady())
        {
            if (HttpMethods.IsHead(Request.Method))
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new PingResource { Status = "Error" });
        }

        if (HttpMethods.IsHead(Request.Method))
        {
            return Ok();
        }

        return Ok(new PingResource { Status = "OK" });
    }

    private bool IsDatastoreReady()
    {
        lock (ProbeLock)
        {
            if (DateTime.UtcNow - _lastProbeUtc < ProbeCacheDuration)
            {
                return _lastProbeSucceeded;
            }
        }

        var succeeded = ProbeDatastore();

        lock (ProbeLock)
        {
            _lastProbeUtc = DateTime.UtcNow;
            _lastProbeSucceeded = succeeded;
        }

        return succeeded;
    }

    private bool ProbeDatastore()
    {
        try
        {
            _configRepository.All();
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void ResetProbeCacheForTesting()
    {
        lock (ProbeLock)
        {
            _lastProbeUtc = DateTime.MinValue;
            _lastProbeSucceeded = false;
        }
    }
}

public class PingResource
{
    public string Status { get; set; }
}
