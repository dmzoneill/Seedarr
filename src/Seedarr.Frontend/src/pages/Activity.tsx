import { useRef, useEffect } from 'react';
import { useTorrents, useSeedingStats, useSpeedHistory } from '../api/hooks';
import { formatSpeed, formatRatio } from '../utils/formatters';

const MAX_POINTS = 60;
const CHART_WIDTH = 320;
const CHART_HEIGHT = 120;
const PADDING = { top: 8, right: 12, bottom: 20, left: 55 };

interface GraphTileProps {
  title: string;
  value: string;
  data: number[];
  color: string;
  maxPoints: number;
}

function getNiceMax(value: number): number {
  if (value <= 0) return 1;
  const magnitude = Math.pow(10, Math.floor(Math.log10(value)));
  const normalized = value / magnitude;
  let nice: number;
  if (normalized <= 1) nice = 1;
  else if (normalized <= 2) nice = 2;
  else if (normalized <= 5) nice = 5;
  else nice = 10;
  return nice * magnitude;
}

function formatYLabel(value: number, isSpeed: boolean, isRatio: boolean): string {
  if (isRatio) return value.toFixed(2);
  if (isSpeed) return formatSpeed(value);
  if (value >= 1000) return `${(value / 1000).toFixed(1)}k`;
  return value.toFixed(0);
}

function GraphTile({ title, value, data, color, maxPoints }: GraphTileProps) {
  const chartW = CHART_WIDTH - PADDING.left - PADDING.right;
  const chartH = CHART_HEIGHT - PADDING.top - PADDING.bottom;

  let maxVal = 0;
  for (const v of data) {
    if (v > maxVal) maxVal = v;
  }
  const niceMax = getNiceMax(maxVal > 0 ? maxVal * 1.1 : 1);

  const isSpeed = title.toLowerCase().includes('speed') || title.toLowerCase().includes('network');
  const isRatio = title.toLowerCase().includes('ratio');

  const gridLineCount = 3;
  const gridLines = Array.from({ length: gridLineCount + 1 }, (_, i) => {
    const val = (niceMax / gridLineCount) * i;
    const y = PADDING.top + chartH - (val / niceMax) * chartH;
    return { value: val, y };
  });

  const points = data.length === 0
    ? ''
    : data
        .map((v, i) => {
          const x = PADDING.left + (i / Math.max(1, maxPoints - 1)) * chartW;
          const y = PADDING.top + chartH - (v / niceMax) * chartH;
          return `${x},${y}`;
        })
        .join(' ');

  const areaPath = data.length < 2
    ? ''
    : (() => {
        const first = PADDING.left + (0 / Math.max(1, maxPoints - 1)) * chartW;
        const last = PADDING.left + ((data.length - 1) / Math.max(1, maxPoints - 1)) * chartW;
        const bottom = PADDING.top + chartH;
        const linePoints = data
          .map((v, i) => {
            const x = PADDING.left + (i / Math.max(1, maxPoints - 1)) * chartW;
            const y = PADDING.top + chartH - (v / niceMax) * chartH;
            return `${x},${y}`;
          })
          .join(' ');
        return `M${first},${bottom} L${linePoints} L${last},${bottom} Z`;
      })();

  return (
    <div className="monitoring-tile">
      <div className="monitoring-tile-title">{title}</div>
      <div className="monitoring-tile-value" style={{ color }}>{value}</div>
      <svg
        width="100%"
        viewBox={`0 0 ${CHART_WIDTH} ${CHART_HEIGHT}`}
        preserveAspectRatio="xMidYMid meet"
      >
        {gridLines.map(({ value: val, y }, i) => (
          <g key={i}>
            <line
              x1={PADDING.left}
              y1={y}
              x2={CHART_WIDTH - PADDING.right}
              y2={y}
              stroke="var(--border-light)"
              strokeWidth={0.5}
            />
            <text
              x={PADDING.left - 4}
              y={y + 3}
              textAnchor="end"
              fill="var(--text-dim)"
              fontSize={8}
              fontFamily="inherit"
            >
              {formatYLabel(val, isSpeed, isRatio)}
            </text>
          </g>
        ))}

        <text
          x={PADDING.left}
          y={CHART_HEIGHT - 4}
          fill="var(--text-dim)"
          fontSize={8}
          textAnchor="start"
        >
          5m ago
        </text>
        <text
          x={CHART_WIDTH - PADDING.right}
          y={CHART_HEIGHT - 4}
          fill="var(--text-dim)"
          fontSize={8}
          textAnchor="end"
        >
          now
        </text>

        <rect
          x={PADDING.left}
          y={PADDING.top}
          width={chartW}
          height={chartH}
          fill="none"
          stroke="var(--border-light)"
          strokeWidth={0.5}
        />

        {areaPath && (
          <path
            d={areaPath}
            fill={color}
            opacity={0.1}
          />
        )}

        {points && (
          <polyline
            points={points}
            fill="none"
            stroke={color}
            strokeWidth={1.5}
            strokeLinejoin="round"
            strokeLinecap="round"
          />
        )}
      </svg>
    </div>
  );
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
        const upSpeed = Math.max(0, (stats.totalUploaded - prev.totalUploaded) / timeDelta);
        const downSpeed = Math.max(0, (stats.totalDownloaded - prev.totalDownloaded) / timeDelta);

        const totalPeers = (torrents ?? []).reduce(
          (sum, t) => sum + (t.seeders || 0) + (t.leechers || 0),
          0
        );

        const push = (arr: number[], val: number) => {
          const next = [...arr, val];
          if (next.length > MAX_POINTS) next.splice(0, next.length - MAX_POINTS);
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

  const currentUpload = h.uploadSpeed.length > 0 ? h.uploadSpeed[h.uploadSpeed.length - 1] : 0;
  const currentDownload = h.downloadSpeed.length > 0 ? h.downloadSpeed[h.downloadSpeed.length - 1] : 0;
  const currentActive = stats?.activeTorrents ?? 0;
  const currentPeers = (torrents ?? []).reduce(
    (sum, t) => sum + (t.seeders || 0) + (t.leechers || 0),
    0
  );
  const currentRatio = stats?.averageRatio ?? 0;
  const currentNetwork = currentUpload + currentDownload;

  return (
    <div>
      <h1 className="page-heading">Activity</h1>
      <div className="monitoring-grid">
        <GraphTile
          title="Upload Speed"
          value={formatSpeed(currentUpload)}
          data={h.uploadSpeed}
          color="#c8a84e"
          maxPoints={MAX_POINTS}
        />
        <GraphTile
          title="Download Speed"
          value={formatSpeed(currentDownload)}
          data={h.downloadSpeed}
          color="#b5443a"
          maxPoints={MAX_POINTS}
        />
        <GraphTile
          title="Active Torrents"
          value={String(currentActive)}
          data={h.activeTorrents}
          color="#8a9a3a"
          maxPoints={MAX_POINTS}
        />
        <GraphTile
          title="Peer Connections"
          value={String(currentPeers)}
          data={h.peerConnections}
          color="#d4843a"
          maxPoints={MAX_POINTS}
        />
        <GraphTile
          title="Upload/Download Ratio"
          value={formatRatio(currentRatio)}
          data={h.ratio}
          color="#9a8a5a"
          maxPoints={MAX_POINTS}
        />
        <GraphTile
          title="Network Activity"
          value={formatSpeed(currentNetwork)}
          data={h.networkActivity}
          color="#7a9a6a"
          maxPoints={MAX_POINTS}
        />
      </div>
    </div>
  );
}

export default Activity;
