import { useState, useRef, useEffect, useCallback } from "react";
import { useTorrents, useSeedingStats, useSpeedHistory } from "../api/hooks";
import type { SpeedSnapshot, SeedingStats } from "../api/types";
import { formatSpeed, formatRatio } from "../utils/formatters";
import LineChart from "../components/LineChart";

const MAX_POINTS = 60;

function sanitizeNumber(val: unknown): number {
  const num = typeof val === "number" ? val : parseFloat(String(val));
  return Number.isFinite(num) && num > 0 ? num : 0;
}

function pushPoint(arr: number[], val: number): number[] {
  const next = [...arr, sanitizeNumber(val)];
  if (next.length > MAX_POINTS) {
    next.splice(0, next.length - MAX_POINTS);
  }
  return next;
}

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
  const { data: serverHistory, refetch: refetchHistory } = useSpeedHistory();

  const [history, setHistory] = useState<HistoryState>({
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

  const lastStatsRef = useRef<SeedingStats | null>(null);

  // Derive total peers and keep in ref to decouple from speed calculation effect
  const currentPeers = (torrents ?? []).reduce(
    (sum, t) => sum + (t.seeders || 0) + (t.leechers || 0),
    0,
  );
  const peersRef = useRef(currentPeers);
  peersRef.current = currentPeers;

  const seedFromHistory = useCallback((historyData: SpeedSnapshot[]) => {
    const recent = historyData.slice(-MAX_POINTS);
    const up = recent.map((s) => sanitizeNumber(s.uploadSpeed));
    const down = recent.map((s) => sanitizeNumber(s.downloadSpeed));
    const act = recent.map((s) => sanitizeNumber(s.activeTorrents));
    const peers = recent.map((s) => sanitizeNumber(s.totalPeers));
    const rat = recent.map((s) => sanitizeNumber(s.averageRatio));
    const net = recent.map(
      (s) => sanitizeNumber(s.uploadSpeed) + sanitizeNumber(s.downloadSpeed),
    );

    setHistory({
      uploadSpeed: up,
      downloadSpeed: down,
      activeTorrents: act,
      peerConnections: peers,
      ratio: rat,
      networkActivity: net,
    });

    if (historyData.length > 0) {
      const last = historyData[historyData.length - 1];
      const lastTime = new Date(last.timestamp).getTime();
      prevRef.current = {
        totalUploaded: sanitizeNumber(last.totalUploaded),
        totalDownloaded: sanitizeNumber(last.totalDownloaded),
        timestamp: Number.isFinite(lastTime) ? lastTime : Date.now(),
      };
    } else {
      prevRef.current = null;
    }
  }, []);

  useEffect(() => {
    if (!serverHistory || seededRef.current) return;
    seededRef.current = true;
    seedFromHistory(serverHistory);
  }, [serverHistory, seedFromHistory]);

  // Handle visibility changes: when tab becomes visible, reset prevRef and refetch server history
  // to avoid huge time deltas and corrupted rolling history windows.
  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.visibilityState === "visible") {
        prevRef.current = null;
        refetchHistory().then((res) => {
          if (res.data) {
            seedFromHistory(res.data);
          }
        });
      }
    };

    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => {
      document.removeEventListener("visibilitychange", handleVisibilityChange);
    };
  }, [refetchHistory, seedFromHistory]);

  useEffect(() => {
    if (!stats) return;

    // Avoid running if stats reference has not changed
    if (stats === lastStatsRef.current) return;
    lastStatsRef.current = stats;

    const now = Date.now();
    const prev = prevRef.current;

    if (prev) {
      const timeDelta = (now - prev.timestamp) / 1000;

      // If timeDelta exceeds 5 seconds (tab throttled, suspended, or latency spike),
      // avoid computing an erroneous delta across the gap and refresh from server history.
      if (timeDelta > 5) {
        prevRef.current = {
          totalUploaded: sanitizeNumber(stats.totalUploaded),
          totalDownloaded: sanitizeNumber(stats.totalDownloaded),
          timestamp: now,
        };
        refetchHistory().then((res) => {
          if (res.data) {
            seedFromHistory(res.data);
          }
        });
        return;
      }

      if (timeDelta >= 0.8) {
        const statsUp = sanitizeNumber(stats.totalUploaded);
        const statsDown = sanitizeNumber(stats.totalDownloaded);

        const upSpeed =
          statsUp >= prev.totalUploaded
            ? Math.max(0, (statsUp - prev.totalUploaded) / timeDelta)
            : 0;
        const downSpeed =
          statsDown >= prev.totalDownloaded
            ? Math.max(0, (statsDown - prev.totalDownloaded) / timeDelta)
            : 0;

        setHistory((curr) => ({
          uploadSpeed: pushPoint(curr.uploadSpeed, upSpeed),
          downloadSpeed: pushPoint(curr.downloadSpeed, downSpeed),
          activeTorrents: pushPoint(
            curr.activeTorrents,
            sanitizeNumber(stats.activeTorrents),
          ),
          peerConnections: pushPoint(
            curr.peerConnections,
            sanitizeNumber(peersRef.current),
          ),
          ratio: pushPoint(curr.ratio, sanitizeNumber(stats.averageRatio)),
          networkActivity: pushPoint(curr.networkActivity, upSpeed + downSpeed),
        }));

        prevRef.current = {
          totalUploaded: statsUp,
          totalDownloaded: statsDown,
          timestamp: now,
        };
      }
    } else {
      prevRef.current = {
        totalUploaded: sanitizeNumber(stats.totalUploaded),
        totalDownloaded: sanitizeNumber(stats.totalDownloaded),
        timestamp: now,
      };
    }
  }, [stats, refetchHistory, seedFromHistory]);

  const currentUpload =
    history.uploadSpeed.length > 0
      ? history.uploadSpeed[history.uploadSpeed.length - 1]
      : 0;
  const currentDownload =
    history.downloadSpeed.length > 0
      ? history.downloadSpeed[history.downloadSpeed.length - 1]
      : 0;
  const currentActive = stats?.activeTorrents ?? 0;
  const currentRatio = stats?.averageRatio ?? 0;
  const currentNetwork = currentUpload + currentDownload;

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
            <span>📈</span> Activity Metrics
          </h1>
          <p
            style={{
              color: "var(--text-muted, #888)",
              margin: "0.25rem 0 0 0",
              fontSize: "0.9rem",
            }}
          >
            Real-time transfer rates, network traffic graphs, active swarms, and
            peer dynamics
          </p>
        </div>

        <div style={{ display: "flex", gap: "0.75rem", alignItems: "center" }}>
          <span
            className="badge badge-success"
            style={{
              fontSize: "0.85rem",
              padding: "0.35rem 0.75rem",
              borderRadius: "4px",
            }}
          >
            ● Live (1s)
          </span>
        </div>
      </div>

      <div className="monitoring-grid">
        <LineChart
          title="Upload Speed"
          value={formatSpeed(currentUpload)}
          data={history.uploadSpeed}
          color="#c8a84e"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Download Speed"
          value={formatSpeed(currentDownload)}
          data={history.downloadSpeed}
          color="#b5443a"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Active Torrents"
          value={String(currentActive)}
          data={history.activeTorrents}
          color="#27ae60"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Peer Connections"
          value={String(currentPeers)}
          data={history.peerConnections}
          color="#d4843a"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Upload/Download Ratio"
          value={formatRatio(currentRatio)}
          data={history.ratio}
          color="#3498db"
          maxPoints={MAX_POINTS}
        />
        <LineChart
          title="Network Activity"
          value={formatSpeed(currentNetwork)}
          data={history.networkActivity}
          color="#9b59b6"
          maxPoints={MAX_POINTS}
        />
      </div>
    </div>
  );
}

export default Activity;
