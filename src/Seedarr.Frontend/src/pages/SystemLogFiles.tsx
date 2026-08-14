import { useLogFiles, useClearLogFiles, useSystemStatus } from '../api/hooks';
import { useQueryClient } from '@tanstack/react-query';

function DownloadIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
      <polyline points="7 10 12 15 17 10" />
      <line x1="12" y1="15" x2="12" y2="3" />
    </svg>
  );
}

function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  const value = bytes / Math.pow(1024, i);
  return `${value.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

function formatRelativeTime(iso: string): string {
  const date = new Date(iso);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffSec = Math.floor(diffMs / 1000);
  const diffMin = Math.floor(diffSec / 60);
  const diffHour = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHour / 24);

  if (diffDay > 0) return `${diffDay} day${diffDay > 1 ? 's' : ''} ago`;
  if (diffHour > 0) return `${diffHour} hour${diffHour > 1 ? 's' : ''} ago`;
  if (diffMin > 0) return `${diffMin} minute${diffMin > 1 ? 's' : ''} ago`;
  return 'just now';
}

function SystemLogFiles() {
  const { data: logFiles, isLoading, error } = useLogFiles();
  const { data: status } = useSystemStatus();
  const clearLogFiles = useClearLogFiles();
  const queryClient = useQueryClient();

  const logPath = status?.appDataPath
    ? `${status.appDataPath}/logs`
    : '{appData}/logs';

  const handleRefresh = () => {
    queryClient.invalidateQueries({ queryKey: ['logfiles'] });
  };

  const handleClear = () => {
    clearLogFiles.mutate();
  };

  return (
    <div>
      <h1 className="page-heading">Log Files</h1>

      <div className="toolbar">
        <button className="btn btn-small" onClick={handleRefresh}>
          Refresh
        </button>
        <button
          className="btn btn-small btn-danger"
          onClick={handleClear}
          disabled={clearLogFiles.isPending}
        >
          {clearLogFiles.isPending ? 'Clearing...' : 'Clear'}
        </button>
      </div>

      <div style={{
        display: 'flex',
        alignItems: 'flex-start',
        gap: '0.5rem',
        padding: '0.6rem 1rem',
        backgroundColor: 'var(--accent-bg-alert)',
        borderBottom: '1px solid var(--accent-border-alert)',
        fontSize: '0.85rem',
        color: 'var(--accent)',
        lineHeight: 1.5,
      }}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ flexShrink: 0, marginTop: '0.1rem' }}>
          <circle cx="12" cy="12" r="10" />
          <line x1="12" y1="16" x2="12" y2="12" />
          <line x1="12" y1="8" x2="12.01" y2="8" />
        </svg>
        <span>
          Log files are located in: <code style={{ fontFamily: 'monospace', fontWeight: 600 }}>{logPath}</code>.
          The log level defaults to &apos;Info&apos; and can be changed in{' '}
          <a href="/settings/advanced" style={{ color: 'var(--accent)', textDecoration: 'underline' }}>
            General Settings
          </a>.
        </span>
      </div>

      {isLoading && <p className="loading">Loading log files...</p>}

      {error && (
        <div className="card">
          <p className="error">Failed to load log files.</p>
        </div>
      )}

      {logFiles && (
        <div className="torrent-table-wrapper">
          <table className="torrent-table">
            <thead>
              <tr>
                <th className="torrent-table-th">Filename</th>
                <th className="torrent-table-th">Last Write Time</th>
                <th className="torrent-table-th">Size</th>
                <th className="torrent-table-th">Download</th>
              </tr>
            </thead>
            <tbody>
              {logFiles.length === 0 && (
                <tr>
                  <td colSpan={4} className="torrent-table-empty">
                    No log files found
                  </td>
                </tr>
              )}
              {logFiles.map((file) => (
                <tr key={file.filename} className="torrent-table-row">
                  <td style={{ fontFamily: 'monospace', fontSize: '0.82rem' }}>
                    {file.filename}
                  </td>
                  <td title={new Date(file.lastWriteTime).toLocaleString()}>
                    {formatRelativeTime(file.lastWriteTime)}
                  </td>
                  <td>{formatFileSize(file.size)}</td>
                  <td>
                    <a
                      href={`/api/v1/logfile/${encodeURIComponent(file.filename)}`}
                      className="btn btn-small"
                      download={file.filename}
                      title={`Download ${file.filename}`}
                      style={{ display: 'inline-flex', textDecoration: 'none' }}
                    >
                      <DownloadIcon />
                    </a>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export default SystemLogFiles;
