using System;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;

namespace Seedarr.Http.REST;

public abstract class RestControllerWithSignalR<TResource, TModel> : RestController<TResource>, IHandle<ModelEvent<TModel>>
    where TResource : RestResource, new()
    where TModel : ModelBase, new()
{
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;
    private readonly Logger _logger;

    protected RestControllerWithSignalR(IBroadcastSignalRMessage signalRBroadcaster)
    {
        _signalRBroadcaster = signalRBroadcaster;
        _logger = LogManager.GetLogger(GetType().ToString());
    }

    protected RestControllerWithSignalR(IBroadcastSignalRMessage signalRBroadcaster, Logger logger)
    {
        _signalRBroadcaster = signalRBroadcaster;
        _logger = logger ?? LogManager.GetLogger(GetType().ToString());
    }

    [Microsoft.AspNetCore.Mvc.NonAction]
    public void Handle(ModelEvent<TModel> message)
    {
        if (!_signalRBroadcaster.IsConnected)
        {
            return;
        }

        if (message?.Model == null)
        {
            return;
        }

        try
        {
            var resource = GetResourceById(message.Model);
            if (resource != null)
            {
                BroadcastResourceChange(message.Action, resource);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to broadcast SignalR model event for {0}", typeof(TModel).Name);
        }
    }

    protected virtual TResource GetResourceById(TModel model)
    {
        throw new NotImplementedException($"{GetType().Name} must override GetResourceById");
    }

    protected void BroadcastResourceChange(ModelAction action, TResource resource)
    {
        var signalRMessage = new SignalRMessage
        {
            Name = resource.ResourceName,
            Body = resource,
            Action = action
        };

        _signalRBroadcaster.BroadcastMessage(signalRMessage);
    }
}
