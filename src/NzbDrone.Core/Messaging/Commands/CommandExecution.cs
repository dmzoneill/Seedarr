using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NzbDrone.Core.Messaging.Commands;

public static class CommandExecution
{
    private static readonly AsyncLocal<CancellationToken> Ambient = new();
    private static readonly ConcurrentDictionary<int, CancellationTokenSource> Running = new();

    public static CancellationToken Token => Ambient.Value;

    public static IDisposable Push(CancellationToken token)
    {
        var previous = Ambient.Value;
        Ambient.Value = token;
        return new Pop(previous);
    }

    public static void Register(int commandId, CancellationTokenSource cancellation)
    {
        if (commandId > 0 && cancellation != null)
        {
            Running[commandId] = cancellation;
        }
    }

    public static void Unregister(int commandId)
    {
        if (commandId > 0)
        {
            Running.TryRemove(commandId, out _);
        }
    }

    public static bool TryCancel(int commandId)
    {
        if (commandId <= 0 || !Running.TryGetValue(commandId, out var cancellation))
        {
            return false;
        }

        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private sealed class Pop : IDisposable
    {
        private readonly CancellationToken _previous;

        public Pop(CancellationToken previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            Ambient.Value = _previous;
        }
    }
}
