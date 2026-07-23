import { useSeedingStats } from '../api/hooks';
import { formatBytes, formatRatio } from '../utils/formatters';

function StatusBar() {
  const { data: stats } = useSeedingStats();

  return (
    <footer className="status-bar">
      <div className="status-bar-content">
        <span className="status-bar-item">
          Active: {stats?.activeTorrents ?? 0}
        </span>
        <span className="status-bar-item">
          Uploaded: {formatBytes(stats?.totalUploaded ?? 0)}
        </span>
        <span className="status-bar-item">
          Downloaded: {formatBytes(stats?.totalDownloaded ?? 0)}
        </span>
        <span className="status-bar-item">
          Ratio: {formatRatio(stats?.averageRatio ?? 0)}
        </span>
      </div>
    </footer>
  );
}

export default StatusBar;
