import { useState, useEffect, useRef } from 'react';
import { Torrent } from '../../api/types';
import { formatBytes, formatSpeed, formatRatio } from '../../utils/formatters';
import LineChart from '../../components/LineChart';
import { StatusRow } from './shared';

export function MonitoringTab({ torrent }: { torrent: Torrent }) {
  const historyRef = useRef<{ uploadSpeed: number[]; downloadSpeed: number[] }>({
    uploadSpeed: [],
    downloadSpeed: [],
  });

  const prevRef = useRef<{
    uploaded: number;
    downloaded: number;
    timestamp: number;
  } | null>(null);

  // Force re-render so chart data is visible after ref update
  const [, setTick] = useState(0);

  useEffect(() => {
    const now = Date.now();
    const prev = prevRef.current;
    const h = historyRef.current;

    if (prev) {
      const timeDelta = (now - prev.timestamp) / 1000;
      if (timeDelta >= 1) {
        const upSpeed = Math.max(0, (torrent.uploaded - prev.uploaded) / timeDelta);
        const downSpeed = Math.max(0, (torrent.downloaded - prev.downloaded) / timeDelta);

        const push = (arr: number[], val: number) => {
          const next = [...arr, val];
          if (next.length > 60) next.splice(0, next.length - 60);
          return next;
        };

        h.uploadSpeed = push(h.uploadSpeed, upSpeed);
        h.downloadSpeed = push(h.downloadSpeed, downSpeed);
        setTick((t) => t + 1);
      }
    }

    prevRef.current = {
      uploaded: torrent.uploaded,
      downloaded: torrent.downloaded,
      timestamp: now,
    };
  }, [torrent.uploaded, torrent.downloaded]);

  const h = historyRef.current;
  const currentUpload = h.uploadSpeed.length > 0 ? h.uploadSpeed[h.uploadSpeed.length - 1] : 0;
  const currentDownload = h.downloadSpeed.length > 0 ? h.downloadSpeed[h.downloadSpeed.length - 1] : 0;

  return (
    <div className="card">
      <h3>Monitoring</h3>
      <div className="monitoring-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))' }}>
        <LineChart
          title="Upload Speed"
          value={formatSpeed(currentUpload)}
          data={h.uploadSpeed}
          color="#c8a84e"
          maxPoints={60}
        />
        <LineChart
          title="Download Speed"
          value={formatSpeed(currentDownload)}
          data={h.downloadSpeed}
          color="#b5443a"
          maxPoints={60}
        />
      </div>
      <div className="detail-grid" style={{ marginTop: '1rem' }}>
        <StatusRow label="Total Uploaded">{formatBytes(torrent.uploaded)}</StatusRow>
        <StatusRow label="Total Downloaded">{formatBytes(torrent.downloaded)}</StatusRow>
        <StatusRow label="Ratio">{formatRatio(torrent.ratio)}</StatusRow>
      </div>
    </div>
  );
}
