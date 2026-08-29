import { useTorrentTrackers } from "../../api/hooks";
import { formatDate } from "../../utils/formatters";
import { PanelLoading, PanelEmpty } from "./shared";

function trackerBadgeClass(status: string): string {
  switch (status) {
    case "Working":
      return "badge-seeding";
    case "Announcing":
      return "badge-announcing";
    case "Failed":
      return "badge-error";
    case "Disabled":
      return "badge-stopped";
    default:
      return "badge-warning";
  }
}

export function TrackersTab({ torrentId }: { torrentId: number }) {
  const { data: trackers, isLoading, isError } = useTorrentTrackers(torrentId);

  if (isLoading) return <PanelLoading>Loading trackers...</PanelLoading>;
  if (isError) return <PanelEmpty>Failed to load trackers.</PanelEmpty>;
  if (!trackers || trackers.length === 0)
    return <PanelEmpty>No trackers</PanelEmpty>;

  return (
    <div className="detail-panel-table-wrap">
      <table className="torrent-table">
        <thead>
          <tr>
            <th className="torrent-table-th">URL</th>
            <th className="torrent-table-th">Tier</th>
            <th className="torrent-table-th">Status</th>
            <th className="torrent-table-th">Seeders</th>
            <th className="torrent-table-th">Leechers</th>
            <th className="torrent-table-th">Announces</th>
            <th className="torrent-table-th">Last Announce</th>
          </tr>
        </thead>
        <tbody>
          {trackers.map((t) => (
            <tr key={t.id} className="torrent-table-row">
              <td className="mono">{t.url}</td>
              <td>{t.tier}</td>
              <td>
                <span className={`badge ${trackerBadgeClass(t.status)}`}>
                  {t.status}
                </span>
              </td>
              <td>{t.seeders}</td>
              <td>{t.leechers}</td>
              <td>
                {t.successfulAnnounces}/{t.totalAnnounces}
              </td>
              <td>{formatDate(t.lastAnnounce)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
