import { useState } from "react";
import { usePeers } from "../api/hooks";
import { formatBytes, formatSpeed } from "../utils/formatters";
import { useToast } from "../context/ToastContext";
import {
  parsePeerFlags,
  maskIpAddress,
  getFlagBadgeColor,
} from "../utils/peerUtils";
import { getCountryFlag } from "./CountryFlag";

interface PeerListProps {
  torrentId: number;
}

function getClientBadge(client: string): { label: string; color: string } {
  const c = (client || "").toLowerCase();
  if (c.includes("qbittorrent") || c.includes("qbit"))
    return { label: "qBittorrent", color: "#3498db" };
  if (c.includes("transmission"))
    return { label: "Transmission", color: "#e74c3c" };
  if (c.includes("deluge")) return { label: "Deluge", color: "#34495e" };
  if (c.includes("rtorrent") || c.includes("libtorrent"))
    return { label: "rTorrent / libtorrent", color: "#27ae60" };
  if (c.includes("utorrent")) return { label: "uTorrent", color: "#2ecc71" };
  if (c.includes("seedarr"))
    return { label: "Seedarr", color: "var(--accent)" };
  return { label: client || "Unknown", color: "#666" };
}

function PeerList({ torrentId }: PeerListProps) {
  const { data: peers, isLoading, isError } = usePeers(torrentId);
  const { showToast } = useToast();
  const [maskIps, setMaskIps] = useState<boolean>(false);

  const handleCopyIp = (ipPort: string) => {
    navigator.clipboard.writeText(ipPort);
    showToast(`Copied ${ipPort} to clipboard`, "success");
  };

  if (isLoading) return <p className="loading">Loading peers...</p>;
  if (isError) return <p className="error">Failed to load peers.</p>;

  return (
    <div className="card">
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "0.5rem",
          gap: "0.5rem",
          flexWrap: "wrap",
        }}
      >
        <h3 style={{ margin: 0 }}>Connected Swarm Peers</h3>
        <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
          <button
            className={`btn btn-small ${maskIps ? "btn-primary" : "btn-outline"}`}
            style={{
              fontSize: "0.75rem",
              padding: "0.2rem 0.5rem",
              cursor: "pointer",
            }}
            onClick={() => setMaskIps((prev) => !prev)}
            title={
              maskIps
                ? "Click to show raw IP addresses"
                : "Click to mask IP addresses for privacy"
            }
          >
            {maskIps ? "👁️ Show IPs" : "🔒 Mask IPs"}
          </button>
          <span className="badge badge-secondary">
            {peers?.length || 0} Peers
          </span>
        </div>
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
                const displayIp = maskIps ? maskIpAddress(peer.ip) : peer.ip;
                const ipPort = `${displayIp}:${peer.port}`;
                const parsedFlags = parsePeerFlags(peer.flags);

                return (
                  <tr key={peer.id} className="peer-table-row">
                    <td>
                      <div
                        style={{
                          display: "flex",
                          alignItems: "center",
                          gap: "0.4rem",
                        }}
                      >
                        {peer.countryCode && (
                          <span
                            className="badge"
                            style={{
                              fontSize: "0.7rem",
                              padding: "0.1rem 0.35rem",
                              cursor: "help",
                              backgroundColor: "var(--bg-secondary, #2c3e50)",
                              color: "var(--text-secondary, #bdc3c7)",
                              border: "1px solid var(--border-color, #444)",
                              borderRadius: "3px",
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "0.25rem",
                              whiteSpace: "nowrap",
                            }}
                            title={
                              peer.countryName
                                ? `${peer.countryName} (${peer.countryCode.toUpperCase()})`
                                : `Country: ${peer.countryCode.toUpperCase()}`
                            }
                          >
                            <span>{getCountryFlag(peer.countryCode)}</span>
                            <span>[{peer.countryCode.toUpperCase()}]</span>
                          </span>
                        )}
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
                      <span
                        style={{
                          fontWeight: peer.uploadSpeed > 0 ? 600 : "inherit",
                          color:
                            peer.uploadSpeed > 0 ? "var(--success)" : "inherit",
                        }}
                      >
                        {formatSpeed(peer.uploadSpeed)}
                      </span>
                    </td>
                    <td>
                      <span
                        style={{
                          fontWeight: peer.downloadSpeed > 0 ? 600 : "inherit",
                          color:
                            peer.downloadSpeed > 0
                              ? "var(--accent)"
                              : "inherit",
                        }}
                      >
                        {formatSpeed(peer.downloadSpeed)}
                      </span>
                    </td>
                    <td>{formatBytes(peer.uploaded)}</td>
                    <td>{formatBytes(peer.downloaded)}</td>
                    <td>
                      {parsedFlags.length === 0 ? (
                        <span
                          style={{
                            color: "var(--text-muted, #7f8c8d)",
                            fontSize: "0.8rem",
                          }}
                        >
                          -
                        </span>
                      ) : (
                        <div
                          style={{
                            display: "flex",
                            gap: "0.25rem",
                            flexWrap: "wrap",
                            alignItems: "center",
                          }}
                        >
                          {parsedFlags.map((flagInfo, idx) => (
                            <span
                              key={idx}
                              className="badge"
                              style={{
                                fontSize: "0.7rem",
                                padding: "0.1rem 0.35rem",
                                cursor: "help",
                                backgroundColor: getFlagBadgeColor(
                                  flagInfo.flag,
                                ),
                                color: "#fff",
                                borderRadius: "3px",
                                fontWeight: 600,
                              }}
                              title={`${flagInfo.label}: ${flagInfo.description}`}
                            >
                              {flagInfo.label}
                            </span>
                          ))}
                        </div>
                      )}
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
