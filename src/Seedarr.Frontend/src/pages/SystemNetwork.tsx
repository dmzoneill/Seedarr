import { useNetworkDiagnostics } from '../api/hooks';

function EncryptionDonut({ encrypted, plaintext }: { encrypted: number; plaintext: number }) {
  const total = encrypted + plaintext;
  if (total === 0) return null;

  const encPct = encrypted / total;
  const radius = 35;
  const circumference = 2 * Math.PI * radius;

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
      <svg width={80} height={80} viewBox="0 0 80 80">
        <circle cx={40} cy={40} r={radius} fill="none" stroke="var(--color-success, #27ae60)"
          strokeWidth={12} strokeDasharray={`${encPct * circumference} ${circumference}`}
          strokeDashoffset={0} transform="rotate(-90 40 40)" />
        <circle cx={40} cy={40} r={radius} fill="none" stroke="var(--color-danger, #e74c3c)"
          strokeWidth={12} strokeDasharray={`${(1 - encPct) * circumference} ${circumference}`}
          strokeDashoffset={-encPct * circumference} transform="rotate(-90 40 40)" />
        <text x={40} y={44} textAnchor="middle" fontSize={13} fontWeight={700} fill="var(--color-text, #ccc)">
          {Math.round(encPct * 100)}%
        </text>
      </svg>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 13 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <div style={{ width: 10, height: 10, borderRadius: '50%', backgroundColor: 'var(--color-success, #27ae60)' }} />
          Encrypted: {encrypted}
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <div style={{ width: 10, height: 10, borderRadius: '50%', backgroundColor: 'var(--color-danger, #e74c3c)' }} />
          Plaintext: {plaintext}
        </div>
      </div>
    </div>
  );
}

function SystemNetwork() {
  const { data: diag, isLoading, isError } = useNetworkDiagnostics();

  if (isLoading) {
    return (
      <div>
        <h1 className="page-heading">Network Diagnostics</h1>
        <p className="loading">Loading diagnostics...</p>
      </div>
    );
  }

  if (isError || !diag) {
    return (
      <div>
        <h1 className="page-heading">Network Diagnostics</h1>
        <p className="error">Failed to load data.</p>
      </div>
    );
  }

  return (
    <div>
      <h1 className="page-heading">Network Diagnostics</h1>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 16, marginBottom: 16 }}>
        <div className="card">
          <h3>Connection</h3>
          <div className="status-row">
            <span className="status-label">Local IP</span>
            <span className="status-value"><code>{diag.localIp}</code></span>
          </div>
          <div className="status-row">
            <span className="status-label">External IP</span>
            <span className="status-value"><code>{diag.externalIp || 'Unknown'}</code></span>
          </div>
          <div className="status-row">
            <span className="status-label">Listening Port</span>
            <span className="status-value">{diag.listeningPort}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Active Connections</span>
            <span className="status-value">{diag.activeConnections}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Upload Slots</span>
            <span className="status-value">{diag.uploadSlots}</span>
          </div>
        </div>

        <div className="card">
          <h3>Services</h3>
          <div className="status-row">
            <span className="status-label">UPnP</span>
            <span className="status-value">
              <span className={`badge ${diag.upnpAvailable ? 'badge-seeding' : 'badge-stopped'}`}>
                {diag.upnpAvailable ? 'Available' : 'Unavailable'}
              </span>
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">Proxy</span>
            <span className="status-value">
              <span className={`badge ${diag.proxyEnabled ? 'badge-seeding' : 'badge-stopped'}`}>
                {diag.proxyEnabled ? 'Enabled' : 'Disabled'}
              </span>
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">DHT</span>
            <span className="status-value">
              <span className={`badge ${diag.dhtEnabled ? 'badge-seeding' : 'badge-stopped'}`}>
                {diag.dhtEnabled ? 'Enabled' : 'Disabled'}
              </span>
            </span>
          </div>
          <div className="status-row">
            <span className="status-label">DHT Nodes</span>
            <span className="status-value">{diag.dhtNodeCount}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Encryption Mode</span>
            <span className="status-value">
              <span className="badge badge-info">{diag.encryptionMode}</span>
            </span>
          </div>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 16, marginBottom: 16 }}>
        <div className="card">
          <h3>Encryption (24h)</h3>
          <EncryptionDonut encrypted={diag.encryptedConnections} plaintext={diag.plaintextConnections} />
          {diag.encryptedConnections + diag.plaintextConnections === 0 && (
            <p style={{ color: 'var(--color-text-muted, #888)', fontSize: 13 }}>No connections in the last 24 hours</p>
          )}
        </div>

        {diag.localAddresses.length > 0 && (
          <div className="card">
            <h3>Local Addresses</h3>
            {diag.localAddresses.map((addr) => (
              <div key={addr} className="status-row">
                <span className="status-value"><code>{addr}</code></span>
              </div>
            ))}
          </div>
        )}
      </div>

      {diag.portMappings.length > 0 && (
        <div className="card">
          <h3>Port Mappings</h3>
          <div className="torrent-table-wrapper">
            <table className="torrent-table">
              <thead>
                <tr>
                  <th className="torrent-table-th">Protocol</th>
                  <th className="torrent-table-th">Internal Port</th>
                  <th className="torrent-table-th">External Port</th>
                  <th className="torrent-table-th">Description</th>
                  <th className="torrent-table-th">Status</th>
                </tr>
              </thead>
              <tbody>
                {diag.portMappings.map((pm, i) => (
                  <tr key={i} className="torrent-table-row">
                    <td>{pm.protocol}</td>
                    <td>{pm.internalPort}</td>
                    <td>{pm.externalPort}</td>
                    <td>{pm.description}</td>
                    <td>
                      <span className={`badge ${pm.isActive ? 'badge-seeding' : 'badge-stopped'}`}>
                        {pm.isActive ? 'Active' : 'Inactive'}
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
