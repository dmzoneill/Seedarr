import { useState, useEffect } from "react";
import { useNetworkDiagnostics, useTestPort } from "../api/hooks";
import { usePortMappingStatus, useRefreshPortMapping } from "../api/network";
import { useToast } from "../context/ToastContext";

function EncryptionDonut({
  encrypted,
  plaintext,
}: {
  encrypted: number;
  plaintext: number;
}) {
  const enc = Number(encrypted) || 0;
  const plain = Number(plaintext) || 0;
  const total = enc + plain;

  const radius = 35;
  const circumference = 2 * Math.PI * radius;

  if (total === 0 || isNaN(total)) {
    return (
      <div style={{ display: "flex", alignItems: "center", gap: 20 }}>
        <svg width={84} height={84} viewBox="0 0 80 80">
          <circle
            cx={40}
            cy={40}
            r={radius}
            fill="none"
            stroke="var(--border-light, #333)"
            strokeWidth={10}
          />
          <text
            x={40}
            y={45}
            textAnchor="middle"
            fontSize={13}
            fontWeight={700}
            fill="var(--text-muted, #888)"
          >
            0%
          </text>
        </svg>
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: 6,
            fontSize: "0.85rem",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <div
              style={{
                width: 10,
                height: 10,
                borderRadius: "50%",
                backgroundColor: "var(--success, #28a745)",
                opacity: 0.5,
              }}
            />
            <span style={{ color: "var(--text-muted)" }}>
              Encrypted: <strong>0</strong>
            </span>
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <div
              style={{
                width: 10,
                height: 10,
                borderRadius: "50%",
                backgroundColor: "var(--danger, #dc3545)",
                opacity: 0.5,
              }}
            />
            <span style={{ color: "var(--text-muted)" }}>
              Plaintext: <strong>0</strong>
            </span>
          </div>
        </div>
      </div>
    );
  }

  const rawPct = enc / total;
  const encPct = Math.max(0, Math.min(1, isNaN(rawPct) ? 0 : rawPct));

  return (
    <div style={{ display: "flex", alignItems: "center", gap: 20 }}>
      <svg width={84} height={84} viewBox="0 0 80 80">
        {encPct > 0 && (
          <circle
            cx={40}
            cy={40}
            r={radius}
            fill="none"
            stroke="var(--success, #28a745)"
            strokeWidth={10}
            strokeDasharray={
              encPct === 1
                ? `${circumference} 0`
                : `${encPct * circumference} ${circumference}`
            }
            strokeDashoffset={0}
            transform="rotate(-90 40 40)"
          />
        )}
        {encPct < 1 && (
          <circle
            cx={40}
            cy={40}
            r={radius}
            fill="none"
            stroke="var(--danger, #dc3545)"
            strokeWidth={10}
            strokeDasharray={
              encPct === 0
                ? `${circumference} 0`
                : `${(1 - encPct) * circumference} ${circumference}`
            }
            strokeDashoffset={encPct === 0 ? 0 : -encPct * circumference}
            transform="rotate(-90 40 40)"
          />
        )}
        <text
          x={40}
          y={45}
          textAnchor="middle"
          fontSize={13}
          fontWeight={700}
          fill="var(--text-primary, #fff)"
        >
          {Math.round(encPct * 100)}%
        </text>
      </svg>
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          gap: 6,
          fontSize: "0.85rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <div
            style={{
              width: 10,
              height: 10,
              borderRadius: "50%",
              backgroundColor: "var(--success, #28a745)",
            }}
          />
          <span>
            Encrypted: <strong>{enc}</strong>
          </span>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <div
            style={{
              width: 10,
              height: 10,
              borderRadius: "50%",
              backgroundColor: "var(--danger, #dc3545)",
            }}
          />
          <span>
            Plaintext: <strong>{plain}</strong>
          </span>
        </div>
      </div>
    </div>
  );
}

function getProtocolBadge(protocol?: string): {
  label: string;
  className: string;
} {
  const p = (protocol || "").toUpperCase();
  if (p === "PCP") {
    return { label: "PCP v2", className: "badge badge-primary" };
  }
  if (p === "NAT-PMP" || p === "NATPMP") {
    return { label: "NAT-PMP", className: "badge badge-primary" };
  }
  if (p === "UPNP" || p === "UPNP-IGD") {
    return { label: "UPnP-IGD", className: "badge badge-primary" };
  }
  return { label: "Inactive", className: "badge badge-stopped" };
}

function formatCountdown(
  expiryUtc: string | null | undefined,
  leaseSeconds?: number,
): string {
  if (!expiryUtc) {
    if (leaseSeconds && leaseSeconds > 0) {
      const h = Math.floor(leaseSeconds / 3600);
      const m = Math.floor((leaseSeconds % 3600) / 60);
      return h > 0 ? `${h}h ${m}m` : `${m}m`;
    }
    return "N/A";
  }
  const expiry = new Date(expiryUtc).getTime();
  const now = Date.now();
  const diffSec = Math.floor((expiry - now) / 1000);
  if (diffSec <= 0) {
    return "Expired";
  }
  const h = Math.floor(diffSec / 3600);
  const m = Math.floor((diffSec % 3600) / 60);
  const s = diffSec % 60;
  if (h > 0) {
    return `${h}h ${m}m`;
  }
  if (m > 0) {
    return `${m}m ${s}s`;
  }
  return `${s}s`;
}

function SystemNetwork() {
  const { data: diag, isLoading, isError } = useNetworkDiagnostics();
  const testPortMutation = useTestPort();
  const { data: portMapping } = usePortMappingStatus();
  const refreshMappingsMutation = useRefreshPortMapping();
  const { showToast } = useToast();
  const [, setTick] = useState(0);

  useEffect(() => {
    const timer = setInterval(() => setTick((t) => t + 1), 1000);
    return () => clearInterval(timer);
  }, []);

  const handleRefreshMappings = async () => {
    try {
      await refreshMappingsMutation.mutateAsync();
      showToast("Port mappings refreshed successfully", "success");
    } catch (err) {
      showToast(
        err instanceof Error ? err.message : "Failed to refresh port mappings",
        "error",
      );
    }
  };

  if (isLoading) {
    return (
      <div className="content-area">
        <div className="page-header">
          <h1 className="page-heading">System: Network Diagnostics</h1>
        </div>
        <p className="loading">Loading network diagnostics...</p>
      </div>
    );
  }

  if (isError || !diag) {
    return (
      <div className="content-area">
        <div className="page-header">
          <h1 className="page-heading">System: Network Diagnostics</h1>
        </div>
        <p className="error">Failed to load network diagnostic data.</p>
      </div>
    );
  }

  return (
    <div className="content-area" style={{ padding: "1.5rem" }}>
      {/* Header Banner */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1.5rem",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div>
          <h1
            style={{
              fontSize: "1.75rem",
              fontWeight: 700,
              margin: 0,
              display: "flex",
              alignItems: "center",
              gap: "0.5rem",
            }}
          >
            <span>🌐</span> System: Network Diagnostics
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Peer-to-peer connection endpoints, listening ports, proxy routes,
            DHT node counts, and encryption metrics
          </p>
        </div>

        <div
          style={{
            display: "flex",
            gap: "0.75rem",
            alignItems: "center",
            flexWrap: "wrap",
          }}
        >
          <span
            className="badge badge-seeding"
            style={{ padding: "0.35rem 0.75rem", fontSize: "0.85rem" }}
          >
            Port {diag.listeningPort} (TCP/UDP)
          </span>

          <button
            type="button"
            className="btn btn-outline"
            style={{ fontSize: "0.85rem", padding: "0.35rem 0.75rem" }}
            onClick={() => testPortMutation.mutate(diag.listeningPort)}
            disabled={testPortMutation.isPending}
            title="Test external reachability of BitTorrent listening port"
          >
            {testPortMutation.isPending
              ? "Testing..."
              : "Test Port Reachability"}
          </button>

          {testPortMutation.data && (
            <span
              className={`badge ${testPortMutation.data.isOpen ? "badge-success" : "badge-danger"}`}
              style={{
                padding: "0.35rem 0.75rem",
                fontSize: "0.85rem",
                backgroundColor: testPortMutation.data.isOpen
                  ? "rgba(40, 167, 69, 0.2)"
                  : "rgba(220, 53, 69, 0.2)",
                color: testPortMutation.data.isOpen
                  ? "var(--success, #28a745)"
                  : "var(--danger, #dc3545)",
                border: `1px solid ${testPortMutation.data.isOpen ? "var(--success, #28a745)" : "var(--danger, #dc3545)"}`,
              }}
            >
              {testPortMutation.data.isOpen
                ? `✓ Reachable / Open (${testPortMutation.data.responseTimeMs ? `${testPortMutation.data.responseTimeMs}ms` : testPortMutation.data.responseTime})`
                : `✗ Unreachable / Filtered ${testPortMutation.data.errorMessage ? `(${testPortMutation.data.errorMessage})` : ""}`}
            </span>
          )}

          {testPortMutation.isError && (
            <span
              className="badge badge-danger"
              style={{
                padding: "0.35rem 0.75rem",
                fontSize: "0.85rem",
                backgroundColor: "rgba(220, 53, 69, 0.2)",
                color: "var(--danger, #dc3545)",
                border: "1px solid var(--danger, #dc3545)",
              }}
            >
              Error: {testPortMutation.error?.message || "Failed to test port"}
            </span>
          )}
        </div>
      </div>

      {/* Primary 2-Column Metric Cards */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(340px, 1fr))",
          gap: "1.25rem",
          marginBottom: "1.25rem",
        }}
      >
        {/* Connection Endpoints Card */}
        <div
          className="card"
          style={{
            borderRadius: "8px",
            border: "1px solid var(--border-light)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            padding: "1.25rem",
          }}
        >
          <h2
            style={{
              fontSize: "1.05rem",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
              marginTop: 0,
              marginBottom: "0.85rem",
            }}
          >
            Connection Endpoints
          </h2>
          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}
          >
            <div className="status-row">
              <span className="status-label">Local IP Address</span>
              <span className="status-value">
                <code>{diag.localIp}</code>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">External Public IP</span>
              <span className="status-value">
                <code>{diag.externalIp || "Unknown"}</code>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">BitTorrent Port</span>
              <span className="status-value" style={{ fontWeight: 600 }}>
                {diag.listeningPort}
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Active Peer Connections</span>
              <span className="status-value">
                <span className="badge badge-primary">
                  {diag.activeConnections}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Configured Upload Slots</span>
              <span className="status-value">{diag.uploadSlots}</span>
            </div>
          </div>
        </div>

        {/* Network Services & Protocols Card */}
        <div
          className="card"
          style={{
            borderRadius: "8px",
            border: "1px solid var(--border-light)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            padding: "1.25rem",
          }}
        >
          <h2
            style={{
              fontSize: "1.05rem",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
              marginTop: 0,
              marginBottom: "0.85rem",
            }}
          >
            Services & Protocols
          </h2>
          <div
            style={{ display: "flex", flexDirection: "column", gap: "0.6rem" }}
          >
            <div className="status-row">
              <span className="status-label">UPnP Port Forwarding</span>
              <span className="status-value">
                <span
                  className={`badge ${diag.upnpAvailable ? "badge-seeding" : "badge-stopped"}`}
                >
                  {diag.upnpAvailable ? "Available" : "Unavailable"}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Proxy Tunneling</span>
              <span className="status-value">
                <span
                  className={`badge ${diag.proxyEnabled ? "badge-seeding" : "badge-stopped"}`}
                >
                  {diag.proxyEnabled ? "Enabled" : "Disabled"}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Mainline DHT</span>
              <span className="status-value">
                <span
                  className={`badge ${diag.dhtEnabled ? "badge-seeding" : "badge-stopped"}`}
                >
                  {diag.dhtEnabled ? "Enabled" : "Disabled"}
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Known DHT Routing Nodes</span>
              <span className="status-value">
                <span className="badge badge-secondary">
                  {diag.dhtNodeCount} nodes
                </span>
              </span>
            </div>
            <div className="status-row">
              <span className="status-label">Protocol Encryption Mode</span>
              <span className="status-value">
                <span className="badge badge-primary">
                  {diag.encryptionMode}
                </span>
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Secondary 2-Column Grid */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(340px, 1fr))",
          gap: "1.25rem",
          marginBottom: "1.25rem",
        }}
      >
        {/* Encryption Donut Card */}
        <div
          className="card"
          style={{
            borderRadius: "8px",
            border: "1px solid var(--border-light)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            padding: "1.25rem",
          }}
        >
          <h2
            style={{
              fontSize: "1.05rem",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
              marginTop: 0,
              marginBottom: "0.85rem",
            }}
          >
            Encryption Distribution (24h)
          </h2>
          <div style={{ marginTop: "0.5rem" }}>
            <EncryptionDonut
              encrypted={diag.encryptedConnections}
              plaintext={diag.plaintextConnections}
            />
            {(Number(diag.encryptedConnections) || 0) +
              (Number(diag.plaintextConnections) || 0) ===
              0 && (
              <p
                style={{
                  color: "var(--text-muted)",
                  fontSize: "0.85rem",
                  margin: "0.5rem 0 0",
                }}
              >
                No peer connection sessions recorded in the last 24 hours.
              </p>
            )}
          </div>
        </div>

        {/* Local Addresses Card */}
        <div
          className="card"
          style={{
            borderRadius: "8px",
            border: "1px solid var(--border-light)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            padding: "1.25rem",
          }}
        >
          <h2
            style={{
              fontSize: "1.05rem",
              fontWeight: 600,
              color: "var(--accent, #c8a84e)",
              marginTop: 0,
              marginBottom: "0.85rem",
            }}
          >
            Detected Local Interfaces
          </h2>
          {diag.localAddresses.length > 0 ? (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "0.5rem",
                marginTop: "0.5rem",
              }}
            >
              {diag.localAddresses.map((addr) => (
                <div
                  key={addr}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: "0.4rem 0.75rem",
                    backgroundColor: "rgba(255, 255, 255, 0.03)",
                    borderRadius: "4px",
                    border: "1px solid var(--border-light)",
                  }}
                >
                  <code style={{ fontSize: "0.85rem" }}>{addr}</code>
                  <span
                    className="badge badge-secondary"
                    style={{ fontSize: "0.72rem" }}
                  >
                    Interface
                  </span>
                </div>
              ))}
            </div>
          ) : (
            <div style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}>
              No local network interfaces found.
            </div>
          )}
        </div>
      </div>

      {/* Port Mappings Card */}
      {((diag.portMappings && diag.portMappings.length > 0) ||
        (portMapping?.mappings && portMapping.mappings.length > 0) ||
        portMapping?.protocol) && (
        <div
          className="card"
          style={{
            borderRadius: "8px",
            border: "1px solid var(--border-light)",
            boxShadow:
              "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
            padding: 0,
            overflow: "hidden",
          }}
        >
          <div
            style={{
              padding: "1.1rem 1.25rem 0.85rem",
              borderBottom: "1px solid var(--border-light)",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: "0.75rem",
            }}
          >
            <div>
              <div
                style={{ display: "flex", alignItems: "center", gap: "0.6rem" }}
              >
                <h2
                  style={{
                    fontSize: "1.05rem",
                    fontWeight: 600,
                    color: "var(--accent, #c8a84e)",
                    margin: 0,
                  }}
                >
                  Active Port Mappings
                </h2>
                <span
                  className={getProtocolBadge(portMapping?.protocol).className}
                  style={{ fontSize: "0.75rem", padding: "0.2rem 0.55rem" }}
                >
                  {getProtocolBadge(portMapping?.protocol).label}
                </span>
              </div>
              <div
                style={{
                  fontSize: "0.8rem",
                  color: "var(--text-muted)",
                  marginTop: "0.3rem",
                  display: "flex",
                  gap: "1.25rem",
                  flexWrap: "wrap",
                }}
              >
                <span>
                  Gateway:{" "}
                  <strong>{portMapping?.gatewayIp || "Auto-discovered"}</strong>
                </span>
                {portMapping?.routerModel && (
                  <span>
                    Router: <strong>{portMapping.routerModel}</strong>
                  </span>
                )}
                {portMapping?.externalIp && (
                  <span>
                    External IP: <strong>{portMapping.externalIp}</strong>
                  </span>
                )}
              </div>
            </div>

            <div>
              <button
                type="button"
                className="btn btn-outline"
                style={{ fontSize: "0.82rem", padding: "0.35rem 0.75rem" }}
                onClick={handleRefreshMappings}
                disabled={refreshMappingsMutation.isPending}
                title="Trigger an immediate gateway probe and lease refresh"
              >
                {refreshMappingsMutation.isPending
                  ? "Refreshing..."
                  : "Refresh Mappings"}
              </button>
            </div>
          </div>

          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Protocol</th>
                  <th className="torrent-table-th">Internal Port</th>
                  <th className="torrent-table-th">External Port</th>
                  <th className="torrent-table-th">Description</th>
                  <th className="torrent-table-th">Expires In</th>
                  <th
                    className="torrent-table-th"
                    style={{ textAlign: "right" }}
                  >
                    Status
                  </th>
                </tr>
              </thead>
              <tbody>
                {(portMapping?.mappings && portMapping.mappings.length > 0
                  ? portMapping.mappings
                  : diag.portMappings.map((pm) => ({
                      internalPort: pm.internalPort,
                      externalPort: pm.externalPort,
                      protocol: pm.protocol,
                      description: pm.description,
                      leaseSeconds:
                        (pm as { leaseSeconds?: number }).leaseSeconds || 7200,
                      expiryUtc:
                        (pm as { expiryUtc?: string }).expiryUtc || null,
                      isActive: pm.isActive,
                      status: pm.isActive ? "Active" : "Inactive",
                      errorMessage: pm.errorMessage,
                    }))
                ).map((pm, i) => (
                  <tr key={i} className="torrent-table-row">
                    <td>
                      <span className="badge badge-primary">{pm.protocol}</span>
                    </td>
                    <td>{pm.internalPort}</td>
                    <td>{pm.externalPort}</td>
                    <td>{pm.description}</td>
                    <td
                      style={{
                        color: "var(--text-muted)",
                        fontSize: "0.85rem",
                      }}
                    >
                      {formatCountdown(pm.expiryUtc, pm.leaseSeconds)}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <span
                        className={`badge ${pm.isActive ? "badge-seeding" : "badge-stopped"}`}
                        title={pm.errorMessage || undefined}
                      >
                        {pm.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

export default SystemNetwork;
