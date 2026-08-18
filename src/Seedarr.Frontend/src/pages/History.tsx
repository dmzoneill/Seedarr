import { useState } from 'react';
import { usePeerConnectionLog, useActivePeers } from '../api/hooks';
import { formatDate } from '../utils/formatters';

function History() {
  const [activeTab, setActiveTab] = useState<'log' | 'active'>('log');
  const [filters, setFilters] = useState({ start: '', end: '', infoHash: '' });
  const [appliedFilters, setAppliedFilters] = useState<{ start?: string; end?: string; infoHash?: string }>({});

  const { data: logs, isLoading: logsLoading, isError: logsError } = usePeerConnectionLog(
    activeTab === 'log' ? appliedFilters : undefined
  );
  const { data: activePeers, isLoading: activeLoading, isError: activeError } = useActivePeers();

  function applyFilters() {
    setAppliedFilters({
      start: filters.start || undefined,
      end: filters.end || undefined,
      infoHash: filters.infoHash || undefined,
    });
  }

  function clearFilters() {
    setFilters({ start: '', end: '', infoHash: '' });
    setAppliedFilters({});
  }

  return (
    <div>
      <div className="page-heading-row">
        <h1 className="page-heading">History</h1>
      </div>

      <div style={{ display: 'flex', gap: 0, marginBottom: 16 }}>
        <button
          className={`btn ${activeTab === 'log' ? 'btn-primary' : 'btn-default'}`}
          onClick={() => setActiveTab('log')}
        >
          Connection Log
        </button>
        <button
          className={`btn ${activeTab === 'active' ? 'btn-primary' : 'btn-default'}`}
          onClick={() => setActiveTab('active')}
        >
          Active Peers
        </button>
      </div>

      {activeTab === 'log' && (
        <>
          <div className="card" style={{ marginBottom: 16 }}>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <input
                type="date"
                className="form-input"
                value={filters.start}
                onChange={(e) => setFilters({ ...filters, start: e.target.value })}
                placeholder="Start date"
              />
              <input
                type="date"
                className="form-input"
                value={filters.end}
                onChange={(e) => setFilters({ ...filters, end: e.target.value })}
                placeholder="End date"
              />
              <input
                type="text"
                className="form-input"
                value={filters.infoHash}
                onChange={(e) => setFilters({ ...filters, infoHash: e.target.value })}
                placeholder="Info hash filter"
                style={{ minWidth: 200 }}
              />
              <button className="btn btn-primary" onClick={applyFilters}>Apply</button>
              <button className="btn btn-default" onClick={clearFilters}>Clear</button>
            </div>
          </div>

          <div className="card">
            {logsLoading ? (
              <p className="loading">Loading connection log...</p>
            ) : logsError ? (
              <p className="error">Failed to load data.</p>
            ) : (
              <div className="torrent-table-wrapper">
                <table className="torrent-table">
                  <thead>
                    <tr>
                      <th className="torrent-table-th">Timestamp</th>
                      <th className="torrent-table-th">Event</th>
                      <th className="torrent-table-th">IP:Port</th>
                      <th className="torrent-table-th">Info Hash</th>
                      <th className="torrent-table-th">Encrypted</th>
                      <th className="torrent-table-th">Torrent</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(logs ?? []).length === 0 ? (
                      <tr>
                        <td colSpan={6} className="torrent-table-empty">No history entries</td>
                      </tr>
                    ) : (
                      (logs ?? []).map((entry) => (
                        <tr key={entry.id} className="torrent-table-row">
                          <td>{formatDate(entry.timestamp)}</td>
                          <td>
                            <span className={`badge ${entry.eventType === 'Connected' ? 'badge-seeding' : 'badge-stopped'}`}>
                              {entry.eventType}
                            </span>
                          </td>
                          <td><code>{entry.remoteIp}:{entry.remotePort}</code></td>
                          <td><code className="info-hash">{entry.infoHash}</code></td>
                          <td>
                            <span className={`badge ${entry.isEncrypted ? 'badge-warning' : 'badge-stopped'}`}>
                              {entry.isEncrypted ? 'Yes' : 'No'}
                            </span>
                          </td>
                          <td>{entry.torrentName || '-'}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </>
      )}

      {activeTab === 'active' && (
        <div className="card">
          {activeLoading ? (
            <p className="loading">Loading active peers...</p>
          ) : activeError ? (
            <p className="error">Failed to load data.</p>
          ) : (
            <div className="torrent-table-wrapper">
              <table className="torrent-table">
                <thead>
                  <tr>
                    <th className="torrent-table-th">IP:Port</th>
                    <th className="torrent-table-th">Info Hash</th>
                    <th className="torrent-table-th">Encrypted</th>
                    <th className="torrent-table-th">Torrent</th>
                    <th className="torrent-table-th">Connected Since</th>
                  </tr>
                </thead>
                <tbody>
                  {(activePeers ?? []).length === 0 ? (
                    <tr>
                      <td colSpan={5} className="torrent-table-empty">No active peers</td>
                    </tr>
                  ) : (
                    (activePeers ?? []).map((peer) => (
                      <tr key={peer.id} className="torrent-table-row">
                        <td><code>{peer.remoteIp}:{peer.remotePort}</code></td>
                        <td><code className="info-hash">{peer.infoHash}</code></td>
                        <td>
                          <span className={`badge ${peer.isEncrypted ? 'badge-warning' : 'badge-stopped'}`}>
                            {peer.isEncrypted ? 'Yes' : 'No'}
                          </span>
                        </td>
                        <td>{peer.torrentName || '-'}</td>
                        <td>{formatDate(peer.timestamp)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default History;
