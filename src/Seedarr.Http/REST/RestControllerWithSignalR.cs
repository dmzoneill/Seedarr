using System;
using System.Collections.Generic;
using System.Threading;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;

namespace Seedarr.Http.REST;

public abstract class RestControllerWithSignalR<TResource, TModel> : RestController<TResource>, IHandle<ModelEvent<TModel>>, IRestSignalRCoalesceController
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
    private readonly Dictionary<int, ulong> _entityGenerations = new();
    private readonly Dictionary<int, ulong> _pendingUpdateGenerations = new();
    private long _observedConnectionEpoch = -1;
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
        if (_disposed)
        {
            return;
        }

        RefreshCoalesceStateForHubEpoch();

        if (!_signalRBroadcaster.IsConnected)
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
                PruneStaleBroadcastTimes(DateTime.UtcNow);
                BumpEntityGeneration(entityId);

                if (_pendingTimers.Remove(entityId, out var timer))
                {
                    timer.Dispose();
                }

                _pendingUpdates.Remove(entityId);
                _pendingUpdateGenerations.Remove(entityId);

                if (message.Action == ModelAction.Deleted)
                {
                    _lastBroadcastTimes.Remove(entityId);
                    _entityGenerations.Remove(entityId);
                }
            }

            DispatchBroadcast(message.Action, model);
            return;
        }

        var shouldBroadcastImmediately = false;
        TModel immediateBroadcastModel = null;
        ulong? immediateBroadcastGeneration = null;
        lock (_syncLock)
        {
            var now = DateTime.UtcNow;
            PruneStaleBroadcastTimes(now);

            if (!_lastBroadcastTimes.TryGetValue(entityId, out var lastTime) || (now - lastTime) >= _coalesceWindow)
            {
                if (_pendingTimers.Remove(entityId, out var timer))
                {
                    timer.Dispose();
                }

                if (_pendingUpdates.Remove(entityId, out var pendingModel))
                {
                    immediateBroadcastModel = pendingModel;
                    immediateBroadcastGeneration = _pendingUpdateGenerations.TryGetValue(entityId, out var pendingGeneration)
                        ? pendingGeneration
                        : GetEntityGeneration(entityId);
                    _pendingUpdateGenerations.Remove(entityId);
                }
                else
                {
                    immediateBroadcastModel = model;
                }

                shouldBroadcastImmediately = true;
            }
            else
            {
                _pendingUpdates[entityId] = model;
                _pendingUpdateGenerations[entityId] = GetEntityGeneration(entityId);

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
            DispatchBroadcast(ModelAction.Updated, immediateBroadcastModel, immediateBroadcastGeneration);
        }
    }

    [Microsoft.AspNetCore.Mvc.NonAction]
    public void Flush()
    {
        List<(int EntityId, TModel Model, ulong Generation)> toFlush;
        lock (_syncLock)
        {
            foreach (var timer in _pendingTimers.Values)
            {
                timer.Dispose();
            }

            _pendingTimers.Clear();

            toFlush = new List<(int, TModel, ulong)>(_pendingUpdates.Count);
            foreach (var kvp in _pendingUpdates)
            {
                var generation = _pendingUpdateGenerations.TryGetValue(kvp.Key, out var gen)
                    ? gen
                    : GetEntityGeneration(kvp.Key);
                toFlush.Add((kvp.Key, kvp.Value, generation));
            }
        }

        foreach (var (entityId, model, generation) in toFlush)
        {
            if (TryDispatchBroadcast(ModelAction.Updated, model, generation))
            {
                RemovePendingUpdate(entityId);
            }
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
            Flush();

            lock (_syncLock)
            {
                foreach (var timer in _pendingTimers.Values)
                {
                    timer.Dispose();
                }

                _pendingTimers.Clear();
                _pendingUpdates.Clear();
                _lastBroadcastTimes.Clear();
                _pendingUpdateGenerations.Clear();
                _entityGenerations.Clear();
            }
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    protected abstract TResource GetResourceById(TModel model);

    protected void BroadcastResourceChange(ModelAction action, TResource resource)
    {
        var signalRMessage = new SignalRMessage
        {
            Name = resource.ResourceName,
            Body = resource,
            Action = action
        };

        if (SignalRBroadcastFanout.TryResolveScope(resource.ResourceName, resource, out var channel, out var torrentId))
        {
            SignalRBroadcastFanout.Broadcast(_signalRBroadcaster, signalRMessage, channel, torrentId);
        }
        else
        {
            _signalRBroadcaster.BroadcastMessage(signalRMessage);
        }
    }

    private void OnCoalesceTimerTick(object state)
    {
        if (_disposed || state is not int entityId)
        {
            return;
        }

        TModel modelToBroadcast = null;
        ulong generationAtQueue = 0;
        var hasPending = false;
        lock (_syncLock)
        {
            if (_pendingTimers.Remove(entityId, out var timer))
            {
                timer.Dispose();
            }

            if (_pendingUpdates.TryGetValue(entityId, out var model))
            {
                hasPending = true;
                generationAtQueue = _pendingUpdateGenerations.TryGetValue(entityId, out var gen)
                    ? gen
                    : GetEntityGeneration(entityId);
                modelToBroadcast = model;
            }
        }

        if (!hasPending || modelToBroadcast == null)
        {
            return;
        }

        if (TryDispatchBroadcast(ModelAction.Updated, modelToBroadcast, generationAtQueue))
        {
            RemovePendingUpdate(entityId);
        }
        else if (!_signalRBroadcaster.IsConnected)
        {
            SchedulePendingCoalesceTimer(entityId);
        }
        else
        {
            RemovePendingUpdate(entityId);
        }
    }

    private ulong GetEntityGeneration(int entityId)
    {
        return _entityGenerations.TryGetValue(entityId, out var generation) ? generation : 0UL;
    }

    private void BumpEntityGeneration(int entityId)
    {
        _entityGenerations.TryGetValue(entityId, out var generation);
        _entityGenerations[entityId] = generation + 1;
    }

    private bool IsPendingGenerationCurrent(int entityId, ulong generationAtQueue)
    {
        lock (_syncLock)
        {
            return GetEntityGeneration(entityId) == generationAtQueue;
        }
    }

    private void DispatchBroadcast(ModelAction action, TModel model, ulong? pendingGeneration = null)
    {
        TryDispatchBroadcast(action, model, pendingGeneration);
    }

    private bool TryDispatchBroadcast(ModelAction action, TModel model, ulong? pendingGeneration = null)
    {
        if (!_signalRBroadcaster.IsConnected)
        {
            return false;
        }

        if (pendingGeneration.HasValue && !IsPendingGenerationCurrent(model.Id, pendingGeneration.Value))
        {
            return false;
        }

        try
        {
            var resource = GetResourceById(model);
            if (pendingGeneration.HasValue && !IsPendingGenerationCurrent(model.Id, pendingGeneration.Value))
            {
                return false;
            }

            if (resource != null)
            {
                BroadcastResourceChange(action, resource);
                if (action == ModelAction.Updated)
                {
                    RecordSuccessfulBroadcast(model.Id);
                }

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to broadcast SignalR model event for {0}", typeof(TModel).Name);
            return false;
        }
    }

    private void RemovePendingUpdate(int entityId)
    {
        lock (_syncLock)
        {
            _pendingUpdates.Remove(entityId);
            _pendingUpdateGenerations.Remove(entityId);
        }
    }

    private void SchedulePendingCoalesceTimer(int entityId)
    {
        lock (_syncLock)
        {
            if (_pendingTimers.ContainsKey(entityId) || !_pendingUpdates.ContainsKey(entityId))
            {
                return;
            }

            _pendingTimers[entityId] = new Timer(OnCoalesceTimerTick, entityId, _coalesceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void RecordSuccessfulBroadcast(int entityId)
    {
        lock (_syncLock)
        {
            _lastBroadcastTimes[entityId] = DateTime.UtcNow;
        }
    }

    private void RefreshCoalesceStateForHubEpoch()
    {
        var epoch = MessageHub.ConnectionEpoch;
        if (epoch == _observedConnectionEpoch)
        {
            return;
        }

        _observedConnectionEpoch = epoch;
        ClearCoalesceThrottleState();
    }

    private void ClearCoalesceThrottleState()
    {
        lock (_syncLock)
        {
            foreach (var timer in _pendingTimers.Values)
            {
                timer.Dispose();
            }

            _pendingTimers.Clear();
            _pendingUpdates.Clear();
            _pendingUpdateGenerations.Clear();
            _lastBroadcastTimes.Clear();
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
