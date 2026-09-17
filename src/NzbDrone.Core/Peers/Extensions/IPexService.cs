namespace NzbDrone.Core.Peers.Extensions;

public interface IPexService
{
    void BroadcastPex();
    void BroadcastTick();
    void BroadcastPexTick();
}
