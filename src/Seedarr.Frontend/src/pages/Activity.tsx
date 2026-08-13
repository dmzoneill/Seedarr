import { useRef, useEffect } from "react";
import { useTorrents, useSeedingStats, useSpeedHistory } from "../api/hooks";
import { formatSpeed, formatRatio } from "../utils/formatters";
import LineChart from "../components/LineChart";

const MAX_POINTS = 60;

interface HistoryState {
  uploadSpeed: number[];
  downloadSpeed: number[];
  activeTorrents: number[];
  peerConnections: number[];
  ratio: number[];
  networkActivity: number[];
}

function Activity() {
  const { data: torrents } = useTorrents();
  const { data: stats } = useSeedingStats();
  const { data: serverHistory } = useSpeedHistory();

  const historyRef = useRef<HistoryState>({
    uploadSpeed: [],
    downloadSpeed: [],
    activeTorrents: [],
    peerConnections: [],
    ratio: [],
    networkActivity: [],
  });

  const seededRef = useRef(false);

  const prevRef = useRef<{
    totalUploaded: number;
    totalDownloaded: number;
    timestamp: number;
  } | null>(null);

  useEffect(() => {
    if (!serverHistory || seededRef.current) return;
    seededRef.current = true;

    const recent = serverHistory.slice(-MAX_POINTS);
    const h = historyRef.current;

    h.uploadSpeed = recent.map((s) => s.uploadSpeed);
    h.downloadSpeed = recent.map((s) => s.downloadSpeed);
    h.activeTorrents = recent.map((s) => s.activeTorrents);
    h.peerConnections = recent.map((s) => s.totalPeers);
    h.ratio = recent.map((s) => s.averageRatio);
    h.networkActivity = recent.map((s) => s.uploadSpeed + s.downloadSpeed);

    if (serverHistory.length > 0) {
      const last = serverHistory[serverHistory.length - 1];
      prevRef.current = {
        totalUploaded: last.totalUploaded,
        totalDownloaded: last.totalDownloaded,
        timestamp: new Date(last.timestamp).getTime(),
      };
    }
  }, [serverHistory]);

  useEffect(() => {
    if (!stats) return;

    const now = Date.now();
    const prev = prevRef.current;
    const h = historyRef.current;

    if (prev) {
      const timeDelta = (now - prev.timestamp) / 1000;
      if (timeDelta >= 1) {
        const upSpeed = Math.max(
          0,
          (stats.totalUploaded - prev.totalUploaded) / timeDelta,
        );
        const downSpeed = Math.max(
          0,
          (stats.totalDownloaded - prev.totalDownloaded) / timeDelta,
        );

        const totalPeers = (torrents ?? []).reduce(
          (sum, t) => sum + (t.seeders || 0) + (t.leechers || 0),
          0,
        );

        const push = (arr: number[], val: number) => {
          const next = [...arr, val];
          if (next.length > MAX_POINTS)
            next.splice(0, next.length - MAX_POINTS);
          return next;
        };

        h.uploadSpeed = push(h.uploadSpeed, upSpeed);
        h.downloadSpeed = push(h.downloadSpeed, downSpeed);
        h.activeTorrents = push(h.activeTorrents, stats.activeTorrents);
        h.peerConnections = push(h.peerConnections, totalPeers);
        h.ratio = push(h.ratio, stats.averageRatio);
        h.networkActivity = push(h.networkActivity, upSpeed + downSpeed);
      }
    }

    prevRef.current = {
      totalUploaded: stats.totalUploaded,
      totalDownloaded: stats.totalDownloaded,
      timestamp: now,
    };
  }, [stats, torrents]);

  const h = historyRef.current;

  const currentUpload =
    h.uploadSpeed.length > 0 ? h.uploadSpeed[h.uploadSpeed.length - 1] : 0;
  const currentDownload =
    h.downloadSpeed.length > 0
      ? h.downloadSpeed[h.downloadSpeed.length - 1]
      : 0;
  const currentActive = stats?.activeTorrents ?? 0;
  const currentPeers = (torrents ?? []).reduce(
    (sum, t) => sum + (t.seeders || 0) + (t.leechers || 0),
    0,
  );
  const currentRatio = stats?.averageRatio ?? 0;
  const currentNetwork = currentUpload + currentDownload;

  return (
    <div className="content-area">
      <div
        className="page-header"
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
        }}
      >
        <div className="page-header-group">
          <div
            style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}
          >
            <h1 className="page-heading" style={{ margin: 0 }}>
              Activity Metrics
            </h1>
            <span
              className="badge badge-success"
              style={{ fontSize: "0.75rem", borderRadius: "4px" }}
            >
              ● Live (1s)
            </span>
          </div>
        </div>
      </div>

      <div className="monitoring-grid">
        <LineChart
          title="Upload Speed"
          value={formatSpeed(currentUpload)}
          data={h.uploadSpeed}
          color="#c8a84e"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Download Speed"
          value={formatSpeed(currentDownload)}
          data={h.downloadSpeed}
          color="#b5443a"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Active Torrents"
          value={String(currentActive)}
          data={h.activeTorrents}
          color="#27ae60"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Peer Connections"
          value={String(currentPeers)}
          data={h.peerConnections}
          color="#d4843a"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Upload/Download Ratio"
          value={formatRatio(currentRatio)}
          data={h.ratio}
          color="#3498db"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Network Activity"
          value={formatSpeed(currentNetwork)}
          data={h.networkActivity}
          color="#9b59b6"
          maxPoints={MAX_POINTS}
        />
      </div>
    </div>
  );
}

export default Activity;
