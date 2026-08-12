import { useRef, useEffect } from "react";
import { useSpeedHistory, useSeedingStats } from "../api/hooks";
import { formatSpeed } from "../utils/formatters";

interface SpeedDataPoint {
  uploadSpeed: number;
  downloadSpeed: number;
}

interface SpeedGraphProps {
  width?: number;
  height?: number;
  maxPoints?: number;
}

function getNiceMax(value: number): number {
  if (value <= 0) return 1024;
  const magnitude = Math.pow(10, Math.floor(Math.log10(value)));
  const normalized = value / magnitude;
  let nice: number;
  if (normalized <= 1) nice = 1;
  else if (normalized <= 2) nice = 2;
  else if (normalized <= 5) nice = 5;
  else nice = 10;
  return nice * magnitude;
}

function SpeedGraph({
  width = 1200,
  height = 200,
  maxPoints = 60,
}: SpeedGraphProps) {
  const historyRef = useRef<SpeedDataPoint[]>([]);
  const seededRef = useRef(false);
  const prevRef = useRef<{
    totalUploaded: number;
    totalDownloaded: number;
    timestamp: number;
  } | null>(null);

  const { data: serverHistory } = useSpeedHistory();
  const { data: stats } = useSeedingStats();

  useEffect(() => {
    if (!serverHistory || seededRef.current) return;
    seededRef.current = true;

    const points: SpeedDataPoint[] = serverHistory
      .slice(-maxPoints)
      .map((s) => ({
        uploadSpeed: s.uploadSpeed,
        downloadSpeed: s.downloadSpeed,
      }));
    historyRef.current = points;

    if (serverHistory.length > 0) {
      const last = serverHistory[serverHistory.length - 1];
      prevRef.current = {
        totalUploaded: last.totalUploaded,
        totalDownloaded: last.totalDownloaded,
        timestamp: new Date(last.timestamp).getTime(),
      };
    }
  }, [serverHistory, maxPoints]);

  useEffect(() => {
    if (!stats) return;

    const now = Date.now();
    const prev = prevRef.current;

    if (prev) {
      const timeDelta = (now - prev.timestamp) / 1000;
      if (timeDelta >= 0.5) {
        const uploadSpeed = Math.max(
          0,
          (stats.totalUploaded - prev.totalUploaded) / timeDelta,
        );
        const downloadSpeed = Math.max(
          0,
          (stats.totalDownloaded - prev.totalDownloaded) / timeDelta,
        );

        const next = [...historyRef.current, { uploadSpeed, downloadSpeed }];
        if (next.length > maxPoints) {
          next.splice(0, next.length - maxPoints);
        }
        historyRef.current = next;
      }
    }

    prevRef.current = {
      totalUploaded: stats.totalUploaded,
      totalDownloaded: stats.totalDownloaded,
      timestamp: now,
    };
  }, [stats, maxPoints]);

  const history = historyRef.current;
  const padding = { top: 12, right: 24, bottom: 26, left: 80 };
  const chartWidth = width - padding.left - padding.right;
  const chartHeight = height - padding.top - padding.bottom;

  let maxSpeed = 0;
  for (const point of history) {
    maxSpeed = Math.max(maxSpeed, point.uploadSpeed, point.downloadSpeed);
  }
  const niceMax = getNiceMax(maxSpeed > 0 ? maxSpeed * 1.1 : 1024);

  const gridLineCount = 4;
  const gridLines = Array.from({ length: gridLineCount + 1 }, (_, i) => {
    const value = (niceMax / gridLineCount) * i;
    const y = padding.top + chartHeight - (value / niceMax) * chartHeight;
    return { value, y };
  });

  const toPoints = (
    data: SpeedDataPoint[],
    key: "uploadSpeed" | "downloadSpeed",
  ): string => {
    if (data.length === 0) return "";
    return data
      .map((point, i) => {
        const x = padding.left + (i / Math.max(1, maxPoints - 1)) * chartWidth;
        const y =
          padding.top + chartHeight - (point[key] / niceMax) * chartHeight;
        return `${x},${y}`;
      })
      .join(" ");
  };

  const uploadPoints = toPoints(history, "uploadSpeed");
  const downloadPoints = toPoints(history, "downloadSpeed");

  const currentUpload =
    history.length > 0 ? history[history.length - 1].uploadSpeed : 0;
  const currentDownload =
    history.length > 0 ? history[history.length - 1].downloadSpeed : 0;

  return (
    <div
      className="card"
      style={{
        borderRadius: "8px",
        boxShadow:
          "0 4px 14px rgba(0, 0, 0, 0.32), 0 1px 3px rgba(0, 0, 0, 0.18)",
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "0.75rem",
        }}
      >
        <h3 style={{ margin: 0, border: "none", padding: 0 }}>
          Transfer Speed
        </h3>
        <div className="speed-graph-legend" style={{ margin: 0 }}>
          <span className="speed-graph-legend-item">
            <span
              className="speed-graph-indicator"
              style={{ backgroundColor: "var(--accent)" }}
            />
            Upload: <strong>{formatSpeed(currentUpload)}</strong>
          </span>
          <span className="speed-graph-legend-item">
            <span
              className="speed-graph-indicator"
              style={{ backgroundColor: "var(--danger)" }}
            />
            Download: <strong>{formatSpeed(currentDownload)}</strong>
          </span>
        </div>
      </div>

      <div className="speed-graph" style={{ width: "100%", height: "200px" }}>
        <svg
          width="100%"
          height="100%"
          viewBox={`0 0 ${width} ${height}`}
          preserveAspectRatio="none"
        >
          <defs>
            <linearGradient id="uploadGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#c8a84e" stopOpacity="0.25" />
              <stop offset="100%" stopColor="#c8a84e" stopOpacity="0.0" />
            </linearGradient>
            <linearGradient id="downloadGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#b5443a" stopOpacity="0.2" />
              <stop offset="100%" stopColor="#b5443a" stopOpacity="0.0" />
            </linearGradient>
          </defs>

          {/* Grid lines & values */}
          {gridLines.map(({ value, y }, i) => (
            <g key={i}>
              <line
                x1={padding.left}
                y1={y}
                x2={width - padding.right}
                y2={y}
                stroke="rgba(255, 255, 255, 0.08)"
                strokeWidth={1}
                strokeDasharray={i === 0 ? "none" : "3 3"}
              />
              <text
                x={padding.left - 8}
                y={y + 4}
                textAnchor="end"
                fill="var(--text-dim)"
                fontSize={11}
                fontFamily="inherit"
              >
                {formatSpeed(value)}
              </text>
            </g>
          ))}

          {/* Time axis labels */}
          <text
            x={padding.left}
            y={height - 6}
            fill="var(--text-dim)"
            fontSize={11}
            textAnchor="start"
          >
            {maxPoints}s ago
          </text>
          <text
            x={padding.left + chartWidth / 2}
            y={height - 6}
            fill="var(--text-dim)"
            fontSize={11}
            textAnchor="middle"
          >
            {Math.floor(maxPoints / 2)}s ago
          </text>
          <text
            x={width - padding.right}
            y={height - 6}
            fill="var(--text-dim)"
            fontSize={11}
            textAnchor="end"
          >
            now
          </text>

          {/* Chart Boundary */}
          <rect
            x={padding.left}
            y={padding.top}
            width={chartWidth}
            height={chartHeight}
            fill="rgba(255, 255, 255, 0.01)"
            stroke="rgba(255, 255, 255, 0.1)"
            strokeWidth={1}
            rx={4}
          />

          {/* Polylines */}
          {uploadPoints && (
            <polyline
              points={uploadPoints}
              fill="none"
              stroke="#c8a84e"
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
            />
          )}

          {downloadPoints && (
            <polyline
              points={downloadPoints}
              fill="none"
              stroke="#b5443a"
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
            />
          )}
        </svg>
      </div>
    </div>
  );
}

export default SpeedGraph;
