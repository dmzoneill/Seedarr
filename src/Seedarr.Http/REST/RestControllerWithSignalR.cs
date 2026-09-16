using System;
using System.Collections.Generic;
using System.Threading;
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
    public const int DefaultCoalesceWindowMs = 300;

    private readonly IBroadcastSignalRMessage _signalRBroadcaster;
    private readonly Logger _logger;
    private readonly TimeSpan _coalesceWindow;
    private readonly object _syncLock = new();
    private readonly Dictionary<int, TModel> _pendingUpdates = new();
    private readonly Dictionary<int, DateTime> _lastBroadcastTimes = new();
    private readonly Dictionary<int, Timer> _pendingTimers = new();
    private bool _disposed;

    protected RestControllerWithSignalR(IBroadcastSignalRMessage signalRBroadcaster)
        : this(signalRBroadcaster, null, TimeSpan.FromMilliseconds(DefaultCoalesceWindowMs))
    {
    }

    protected RestControllerWithSignalR(IBroadcastSignalRMessage signalRBroadcaster, Logger logger)
        : this(signalRBroadcaster, logger, TimeSpan.FromMilliseconds(DefaultCoalesceWindowMs))
    {
    }

    protected RestControllerWithSignalR(IBroadcastSignalRMessage signalRBroadcaster, TimeSpan coalesceWindow)
        : this(signalRBroadcaster, null, coalesceWindow)
    {
    }

    protected RestControllerWithSignalR(IBroadcastSignalRMessage signalRBroadcaster, Logger logger, TimeSpan? coalesceWindow)
    {
        _signalRBroadcaster = signalRBroadcaster;
        _logger = logger ?? LogManager.GetLogger(GetType().ToString());
        _coalesceWindow = coalesceWindow ?? TimeSpan.FromMilliseconds(DefaultCoalesceWindowMs);
    }

    public int PendingUpdatesCount
    {
        get
        {
            lock (_syncLock)
            {
                return _pendingUpdates.Count;
            }
        }
    }

    [Microsoft.AspNetCore.Mvc.NonAction]
    public void Handle(ModelEvent<TModel> message)
    {
        if (_disposed || !_signalRBroadcaster.IsConnected)
        {
            return;
        }

        if (message?.Model == null)
        {
            return;
        }

        var model = message.Model;
        var entityId = model.Id;

        if (message.Action != ModelAction.Updated || _coalesceWindow <= TimeSpan.Zero)
        {
            lock (_syncLock)
            {
                if (_pendingTimers.Remove(entityId, out var timer))
                {
                    timer.Dispose();
                }

                _pendingUpdates.Remove(entityId);

                if (message.Action == ModelAction.Deleted)
                {
                    _lastBroadcastTimes.Remove(entityId);
                }
                else
                {
                    _lastBroadcastTimes[entityId] = DateTime.UtcNow;
                }
            }

            DispatchBroadcast(message.Action, model);
            return;
        }

        var shouldBroadcastImmediately = false;
        lock (_syncLock)
        {
            var now = DateTime.UtcNow;
            PruneStaleBroadcastTimes(now);

            if (!_lastBroadcastTimes.TryGetValue(entityId, out var lastTime) || (now - lastTime) >= _coalesceWindow)
            {
                _lastBroadcastTimes[entityId] = now;
                _pendingUpdates.Remove(entityId);

                if (_pendingTimers.Remove(entityId, out var timer))
                {
                    timer.Dispose();
                }

                shouldBroadcastImmediately = true;
            }
            else
            {
                _pendingUpdates[entityId] = model;

                if (!_pendingTimers.ContainsKey(entityId))
                {
                    var remaining = _coalesceWindow - (now - lastTime);
                    if (remaining < TimeSpan.Zero)
                    {
                        remaining = TimeSpan.Zero;
                    }

                    _pendingTimers[entityId] = new Timer(OnCoalesceTimerTick, entityId, remaining, Timeout.InfiniteTimeSpan);
                }
            }
        }

        if (shouldBroadcastImmediately)
        {
            DispatchBroadcast(ModelAction.Updated, model);
        }
    }

    public void Flush()
    {
        List<TModel> toFlush;
        lock (_syncLock)
        {
            foreach (var timer in _pendingTimers.Values)
            {
                timer.Dispose();
            }

            _pendingTimers.Clear();

            toFlush = new List<TModel>(_pendingUpdates.Values);
            _pendingUpdates.Clear();

            var now = DateTime.UtcNow;
            foreach (var item in toFlush)
            {
                _lastBroadcastTimes[item.Id] = now;
            }
        }

        foreach (var model in toFlush)
        {
            DispatchBroadcast(ModelAction.Updated, model);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            lock (_syncLock)
            {
                foreach (var timer in _pendingTimers.Values)
                {
                    timer.Dispose();
                }

                _pendingTimers.Clear();
                _pendingUpdates.Clear();
                _lastBroadcastTimes.Clear();
            }
        }

        _disposed = true;
        base.Dispose(disposing);
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

    private void OnCoalesceTimerTick(object state)
    {
        if (_disposed || state is not int entityId)
        {
            return;
        }

        TModel modelToBroadcast = null;
        lock (_syncLock)
        {
            if (_pendingTimers.Remove(entityId, out var timer))
            {
                timer.Dispose();
            }

            if (_pendingUpdates.Remove(entityId, out var model))
            {
                _lastBroadcastTimes[entityId] = DateTime.UtcNow;
                modelToBroadcast = model;
            }
        }

        if (modelToBroadcast != null)
        {
            DispatchBroadcast(ModelAction.Updated, modelToBroadcast);
        }
    }

    private void DispatchBroadcast(ModelAction action, TModel model)
    {
        if (!_signalRBroadcaster.IsConnected)
        {
            return;
        }

        try
        {
            var resource = GetResourceById(model);
            if (resource != null)
            {
                BroadcastResourceChange(action, resource);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to broadcast SignalR model event for {0}", typeof(TModel).Name);
        }
    }

    private void PruneStaleBroadcastTimes(DateTime now)
    {
        if (_lastBroadcastTimes.Count > 1000)
        {
            var staleThreshold = now - TimeSpan.FromMinutes(5);
            var staleKeys = new List<int>();
            foreach (var kvp in _lastBroadcastTimes)
            {
                if (kvp.Value < staleThreshold)
                {
                    staleKeys.Add(kvp.Key);
                }
            }

            foreach (var key in staleKeys)
            {
                _lastBroadcastTimes.Remove(key);
            }
        }
    }
}
