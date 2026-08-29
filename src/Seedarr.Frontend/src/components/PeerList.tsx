import { usePeers } from "../api/hooks";
import { formatBytes, formatSpeed } from "../utils/formatters";
import { useToast } from "../context/ToastContext";

interface PeerListProps {
  torrentId: number;
}

function getClientBadge(client: string): { label: string; color: string } {
  const c = (client || "").toLowerCase();
  if (c.includes("qbittorrent") || c.includes("qbit")) return { label: "qBittorrent", color: "#3498db" };
  if (c.includes("transmission")) return { label: "Transmission", color: "#e74c3c" };
  if (c.includes("deluge")) return { label: "Deluge", color: "#34495e" };
  if (c.includes("rtorrent") || c.includes("libtorrent")) return { label: "rTorrent / libtorrent", color: "#27ae60" };
  if (c.includes("utorrent")) return { label: "uTorrent", color: "#2ecc71" };
  if (c.includes("seedarr")) return { label: "Seedarr", color: "var(--accent)" };
  return { label: client || "Unknown", color: "#666" };
}

function PeerList({ torrentId }: PeerListProps) {
  const { data: peers, isLoading, isError } = usePeers(torrentId);
  const { showToast } = useToast();

  const handleCopyIp = (ipPort: string) => {
    navigator.clipboard.writeText(ipPort);
    showToast(`Copied ${ipPort} to clipboard`, "success");
  };

  if (isLoading) return <p className="loading">Loading peers...</p>;
  if (isError) return <p className="error">Failed to load peers.</p>;

  return (
    <div className="card">
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.5rem" }}>
        <h3 style={{ margin: 0 }}>Connected Swarm Peers</h3>
        <span className="badge badge-secondary">{peers?.length || 0} Peers</span>
      </div>

      {!peers || peers.length === 0 ? (
        <p className="peer-table-empty">No peers connected</p>
      ) : (
        <div className="peer-table-wrapper">
          <table className="peer-table">
            <thead>
              <tr>
                <th className="peer-table-th">Peer Endpoint</th>
                <th className="peer-table-th">Client Software</th>
                <th className="peer-table-th">Swarm Progress</th>
                <th className="peer-table-th">Upload Velocity</th>
                <th className="peer-table-th">Download Velocity</th>
                <th className="peer-table-th">Uploaded</th>
                <th className="peer-table-th">Downloaded</th>
                <th className="peer-table-th">Flags</th>
              </tr>
            </thead>
            <tbody>
              {peers.map((peer) => {
                const clientBadge = getClientBadge(peer.client);
                const ipPort = `${peer.ip}:${peer.port}`;

                return (
                  <tr key={peer.id} className="peer-table-row">
                    <td>
                      <div style={{ display: "flex", alignItems: "center", gap: "0.4rem" }}>
                        <code className="mono" style={{ fontSize: "0.8rem" }}>
                          {ipPort}
                        </code>
                        <button
                          className="btn btn-small"
                          style={{
                            padding: "0.1rem 0.3rem",
                            fontSize: "0.7rem",
                            background: "none",
                            border: "none",
                            cursor: "pointer",
                            opacity: 0.7,
                          }}
                          onClick={() => handleCopyIp(ipPort)}
                          title="Copy IP:Port to clipboard"
                        >
                          📋
                        </button>
                      </div>
                    </td>
                    <td>
                      <span
                        className="badge"
                        style={{
                          backgroundColor: clientBadge.color,
                          color: "#fff",
                          fontSize: "0.75rem",
                        }}
                      >
                        {clientBadge.label}
                      </span>
                    </td>
                    <td>
                      <div className="peer-progress">
                        <div
                          className="peer-progress-bar"
                          style={{
                            width: `${(peer.progress * 100).toFixed(1)}%`,
                          }}
                        />
                        <span className="peer-progress-text">
                          {(peer.progress * 100).toFixed(1)}%
                        </span>
                      </div>
                    </td>
                    <td>
                      <span style={{ fontWeight: peer.uploadSpeed > 0 ? 600 : "inherit", color: peer.uploadSpeed > 0 ? "var(--success)" : "inherit" }}>
                        {formatSpeed(peer.uploadSpeed)}
                      </span>
                    </td>
                    <td>
                      <span style={{ fontWeight: peer.downloadSpeed > 0 ? 600 : "inherit", color: peer.downloadSpeed > 0 ? "var(--accent)" : "inherit" }}>
                        {formatSpeed(peer.downloadSpeed)}
                      </span>
                    </td>
                    <td>{formatBytes(peer.uploaded)}</td>
                    <td>{formatBytes(peer.downloaded)}</td>
                    <td className="mono" style={{ fontSize: "0.75rem" }}>
                      {peer.flags || "-"}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export default PeerList;
