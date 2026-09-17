using System;

namespace NzbDrone.Core.Peers;

public interface IConnectionReservation : IDisposable
{
    string InfoHash { get; }
    bool IsInbound { get; }
}
