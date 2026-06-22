import { useRef, useEffect } from 'react';
import { useSeedingStats, useNetworkStatus, useTorrents } from '../api/hooks';
import { formatBytes, formatSpeed, formatRatio } from '../utils/formatters';
import { SeedingIcon, UploadIcon, DownloadIcon, UsersIcon, WifiIcon } from './icons/UIIcons';

function StatusBar() {
  const { data: stats } = useSeedingStats();
  const { data: network } = useNetworkStatus();
  const { data: torrents } = useTorrents();

  // Derive instantaneous speed from polling deltas (same approach as Activity.tsx)
  const prevRef = useRef<{
    totalUploaded: number;
    totalDownloaded: number;
    timestamp: number;
  } | null>(null);
  const speedRef = useRef({ uploadSpeed: 0, downloadSpeed: 0 });

  useEffect(() => {
    if (!stats) return;

    const now = Date.now();
    const prev = prevRef.current;

    if (prev) {
      const timeDelta = (now - prev.timestamp) / 1000;
      if (timeDelta >= 1) {
        speedRef.current = {
          uploadSpeed: Math.max(0, (stats.totalUploaded - prev.totalUploaded) / timeDelta),
          downloadSpeed: Math.max(0, (stats.totalDownloaded - prev.totalDownloaded) / timeDelta),
        };
      }
    }

    prevRef.current = {
      totalUploaded: stats.totalUploaded,
      totalDownloaded: stats.totalDownloaded,
      timestamp: now,
    };
  }, [stats]);

  const { uploadSpeed, downloadSpeed } = speedRef.current;

  // Aggregate peer counts across all torrents
  const totalSeeders = (torrents ?? []).reduce((sum, t) => sum + (t.seeders ?? 0), 0);
  const totalLeechers = (torrents ?? []).reduce((sum, t) => sum + (t.leechers ?? 0), 0);
  const totalPeers = totalSeeders + totalLeechers;

  return (
    <footer className="status-bar">
      <div className="status-bar-content">
        <span className="status-bar-item">
          <SeedingIcon size={12} /> Active: {stats?.activeTorrents ?? 0}
        </span>
        <span className="status-bar-item status-bar-upload">
          <UploadIcon size={12} /> {formatSpeed(uploadSpeed)}
        </span>
        <span className="status-bar-item status-bar-download">
          <DownloadIcon size={12} /> {formatSpeed(downloadSpeed)}
        </span>
        <span className="status-bar-item">
          <UsersIcon size={12} /> Peers: {totalSeeders} / {totalPeers}
        </span>
        <span className="status-bar-item">
          <UploadIcon size={12} /> Uploaded: {formatBytes(stats?.totalUploaded ?? 0)}
        </span>
        <span className="status-bar-item">
          <DownloadIcon size={12} /> Downloaded: {formatBytes(stats?.totalDownloaded ?? 0)}
        </span>
        <span className="status-bar-item">
          Ratio: {formatRatio(stats?.averageRatio ?? 0)}
        </span>
        <span className="status-bar-item">
          <WifiIcon size={12} /> IP: {network?.externalIp ?? '...'}
        </span>
      </div>
    </footer>
  );
}

export default StatusBar;
