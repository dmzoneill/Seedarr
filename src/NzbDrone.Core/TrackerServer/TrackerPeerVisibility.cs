using System.Net;

namespace NzbDrone.Core.TrackerServer;

internal static class TrackerPeerVisibility
{
    public static bool IsHiddenLoopback(string peerIp, string requesterIp)
    {
        if (!TryNormalize(peerIp, out var peer) || !IPAddress.IsLoopback(peer))
        {
            return false;
        }

        if (!TryNormalize(requesterIp, out var requester))
        {
            return true;
        }

        return !IPAddress.IsLoopback(requester);
    }

    private static bool TryNormalize(string ip, out IPAddress address)
    {
        if (!IPAddress.TryParse(ip, out address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return true;
    }
}
