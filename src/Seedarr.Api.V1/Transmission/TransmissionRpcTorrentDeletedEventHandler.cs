using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Seedarr.Api.V1.Transmission;

public class TransmissionRpcTorrentDeletedEventHandler : IHandle<TorrentDeletedEvent>
{
    public void Handle(TorrentDeletedEvent message)
    {
        if (message != null)
        {
            var torrentId = message.TorrentId > 0 ? message.TorrentId : message.Torrent?.Id ?? 0;
            if (torrentId > 0)
            {
                TransmissionRpcController.RecordRemovedId(torrentId);
            }
        }
    }
}
