using System.Collections.Generic;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace Seedarr.Http.REST;

public class RestSignalRCoalesceShutdownHandler : IHandle<ApplicationShutdownRequested>
{
    private readonly IEnumerable<IRestSignalRCoalesceController> _controllers;

    public RestSignalRCoalesceShutdownHandler(IEnumerable<IRestSignalRCoalesceController> controllers)
    {
        _controllers = controllers;
    }

    public void Handle(ApplicationShutdownRequested message)
    {
        foreach (var controller in _controllers)
        {
            controller.Flush();
        }
    }
}
