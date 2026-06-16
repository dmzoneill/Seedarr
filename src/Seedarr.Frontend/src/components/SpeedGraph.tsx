import { useRef, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../api/client';
import type { SeedingStats } from '../api/types';
import { formatSpeed } from '../utils/formatters';

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

function SpeedGraph({ width = 700, height = 250, maxPoints = 60 }: SpeedGraphProps) {
  const historyRef = useRef<SpeedDataPoint[]>([]);
  const prevRef = useRef<{
    totalUploaded: number;
    totalDownloaded: number;
    timestamp: number;
  } | null>(null);

  const { data: stats } = useQuery<SeedingStats>({
    queryKey: ['seeding', 'stats'],
    queryFn: () => apiClient.get('/seeding/stats'),
    refetchInterval: 1000,
  });

  useEffect(() => {
    if (!stats) return;

    const now = Date.now();
    const prev = prevRef.current;

    if (prev) {
      const timeDelta = (now - prev.timestamp) / 1000;
      if (timeDelta >= 0.5) {
        const uploadSpeed = Math.max(
          0,
          (stats.totalUploaded - prev.totalUploaded) / timeDelta
        );
        const downloadSpeed = Math.max(
          0,
          (stats.totalDownloaded - prev.totalDownloaded) / timeDelta
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
  const padding = { top: 10, right: 20, bottom: 30, left: 70 };
  const chartWidth = width - padding.left - padding.right;
  const chartHeight = height - padding.top - padding.bottom;

  // Auto-scale Y axis based on max speed in current window
  let maxSpeed = 0;
  for (const point of history) {
    maxSpeed = Math.max(maxSpeed, point.uploadSpeed, point.downloadSpeed);
  }
  const niceMax = getNiceMax(maxSpeed > 0 ? maxSpeed * 1.1 : 1024);

  // Grid lines (5 horizontal lines including 0)
  const gridLineCount = 4;
  const gridLines = Array.from({ length: gridLineCount + 1 }, (_, i) => {
    const value = (niceMax / gridLineCount) * i;
    const y = padding.top + chartHeight - (value / niceMax) * chartHeight;
    return { value, y };
  });

  // Build polyline points string
  const toPoints = (
    data: SpeedDataPoint[],
    key: 'uploadSpeed' | 'downloadSpeed'
  ): string => {
    if (data.length === 0) return '';
    return data
      .map((point, i) => {
        const x =
          padding.left + (i / Math.max(1, maxPoints - 1)) * chartWidth;
        const y =
          padding.top +
          chartHeight -
          (point[key] / niceMax) * chartHeight;
        return `${x},${y}`;
      })
      .join(' ');
  };

  const uploadPoints = toPoints(history, 'uploadSpeed');
  const downloadPoints = toPoints(history, 'downloadSpeed');

  const currentUpload =
    history.length > 0 ? history[history.length - 1].uploadSpeed : 0;
  const currentDownload =
    history.length > 0 ? history[history.length - 1].downloadSpeed : 0;

  return (
    <div className="card">
      <h3>Transfer Speed</h3>
      <div className="speed-graph-legend">
        <span className="speed-graph-legend-item">
          <span
            className="speed-graph-indicator"
            style={{ backgroundColor: '#35c5f4' }}
          />
          Upload: {formatSpeed(currentUpload)}
        </span>
        <span className="speed-graph-legend-item">
          <span
            className="speed-graph-indicator"
            style={{ backgroundColor: '#f44336' }}
          />
          Download: {formatSpeed(currentDownload)}
        </span>
      </div>
      <div className="speed-graph">
        <svg
          width="100%"
          height="100%"
          viewBox={`0 0 ${width} ${height}`}
          preserveAspectRatio="xMidYMid meet"
        >
          {/* Grid lines and Y-axis labels */}
          {gridLines.map(({ value, y }, i) => (
            <g key={i}>
              <line
                x1={padding.left}
                y1={y}
                x2={width - padding.right}
                y2={y}
                stroke="#3a3f4b"
                strokeWidth={1}
              />
              <text
                x={padding.left - 8}
                y={y + 4}
                textAnchor="end"
                fill="#999"
                fontSize={11}
                fontFamily="inherit"
              >
                {formatSpeed(value)}
              </text>
            </g>
          ))}

          {/* X-axis labels */}
          <text
            x={padding.left}
            y={height - 5}
            fill="#999"
            fontSize={11}
            textAnchor="start"
          >
            {maxPoints}s ago
          </text>
          <text
            x={padding.left + chartWidth / 2}
            y={height - 5}
            fill="#999"
            fontSize={11}
            textAnchor="middle"
          >
            {Math.floor(maxPoints / 2)}s ago
          </text>
          <text
            x={width - padding.right}
            y={height - 5}
            fill="#999"
            fontSize={11}
            textAnchor="end"
          >
            now
          </text>

          {/* Chart area border */}
          <rect
            x={padding.left}
            y={padding.top}
            width={chartWidth}
            height={chartHeight}
            fill="none"
            stroke="#3a3f4b"
            strokeWidth={1}
          />

          {/* Upload speed line (cyan) */}
          {uploadPoints && (
            <polyline
              points={uploadPoints}
              fill="none"
              stroke="#35c5f4"
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
            />
          )}

          {/* Download speed line (red) */}
          {downloadPoints && (
            <polyline
              points={downloadPoints}
              fill="none"
              stroke="#f44336"
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
