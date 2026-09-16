import { useId, useRef, useState, useEffect } from "react";
import { formatSpeed } from "../utils/formatters";

const DEFAULT_CHART_WIDTH = 600;
const CHART_HEIGHT = 170;
const PADDING = { top: 10, right: 16, bottom: 24, left: 70 };

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

function formatYLabel(
  value: number,
  isSpeed: boolean,
  isRatio: boolean,
): string {
  if (isRatio) return value.toFixed(2);
  if (isSpeed) return formatSpeed(value);
  if (value >= 1000) return `${(value / 1000).toFixed(1)}k`;
  return value.toFixed(0);
}

export interface LineChartProps {
  title: string;
  value: string;
  data: number[];
  color: string;
  maxPoints: number;
  isSpeed?: boolean;
  isRatio?: boolean;
}

export default function LineChart({
  title,
  value,
  data,
  color,
  maxPoints,
  isSpeed,
  isRatio,
}: LineChartProps) {
  const gradId = useId().replace(/:/g, "_");
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState(DEFAULT_CHART_WIDTH);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;

    if (el.clientWidth > 0) {
      setContainerWidth(el.clientWidth);
    }

    const ro = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const w = Math.round(entry.contentRect.width);
        if (w > 0) {
          setContainerWidth(w);
        }
      }
    });

    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const chartW = Math.max(10, containerWidth - PADDING.left - PADDING.right);
  const chartH = CHART_HEIGHT - PADDING.top - PADDING.bottom;

  const autoSpeed =
    isSpeed ??
    (title.toLowerCase().includes("speed") ||
      title.toLowerCase().includes("network"));
  const autoRatio = isRatio ?? title.toLowerCase().includes("ratio");

  let maxVal = 0;
  for (const v of data) {
    if (typeof v === "number" && Number.isFinite(v) && v > maxVal) {
      maxVal = v;
    }
  }

  // Prevent duplicate 0 B/s or 0/1 counts at idle by enforcing minimum sensible niceMax
  let minNiceMax = 1;
  if (autoSpeed) {
    minNiceMax = 1024; // 1 KB/s minimum scale for transfer rates
  } else if (autoRatio) {
    minNiceMax = 1;
  } else {
    minNiceMax = 3; // Minimum 3 units for discrete counts
  }

  let niceMax = getNiceMax(maxVal > 0 ? maxVal * 1.1 : minNiceMax);
  if (niceMax < minNiceMax) {
    niceMax = minNiceMax;
  }

  const gridLineCount = 3;
  if (!autoSpeed && !autoRatio && niceMax < 10 && niceMax % gridLineCount !== 0) {
    niceMax = Math.ceil(niceMax / gridLineCount) * gridLineCount;
  }

  const seenLabels = new Set<string>();
  const gridLines = Array.from({ length: gridLineCount + 1 }, (_, i) => {
    const val = (niceMax / gridLineCount) * i;
    const y = PADDING.top + chartH - (val / niceMax) * chartH;
    const formatted = formatYLabel(val, autoSpeed, autoRatio);
    const isDuplicate = seenLabels.has(formatted);
    seenLabels.add(formatted);
    return { value: val, y, label: isDuplicate ? "" : formatted };
  });

  const getPoint = (v: number, i: number) => {
    const rawVal = typeof v === "number" && Number.isFinite(v) ? v : 0;
    const clampedVal = Math.max(0, Math.min(rawVal, niceMax));
    const x = PADDING.left + (i / Math.max(1, maxPoints - 1)) * chartW;
    const y = PADDING.top + chartH - (clampedVal / niceMax) * chartH;
    return { x: Number(x.toFixed(1)), y: Number(y.toFixed(1)) };
  };

  const points =
    data.length === 0
      ? ""
      : data
          .map((v, i) => {
            const pt = getPoint(v, i);
            return `${pt.x},${pt.y}`;
          })
          .join(" ");

  const areaPath =
    data.length < 2
      ? ""
      : (() => {
          const first = PADDING.left;
          const last =
            PADDING.left +
            ((data.length - 1) / Math.max(1, maxPoints - 1)) * chartW;
          const bottom = PADDING.top + chartH;
          const linePoints = data
            .map((v, i) => {
              const pt = getPoint(v, i);
              return `${pt.x},${pt.y}`;
            })
            .join(" ");
          return `M${first.toFixed(1)},${bottom.toFixed(1)} L${linePoints} L${last.toFixed(1)},${bottom.toFixed(1)} Z`;
        })();

  return (
    <div className="monitoring-tile">
      <div className="monitoring-tile-title">{title}</div>
      <div className="monitoring-tile-value" style={{ color }}>
        {value}
      </div>
      <div
        ref={containerRef}
        style={{ width: "100%", height: "170px", position: "relative" }}
      >
        <svg
          width="100%"
          height="100%"
          viewBox={`0 0 ${containerWidth} ${CHART_HEIGHT}`}
          style={{ display: "block" }}
        >
          <defs>
            <linearGradient id={`grad_${gradId}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={color} stopOpacity={0.25} />
              <stop offset="100%" stopColor={color} stopOpacity={0.0} />
            </linearGradient>
            <clipPath id={`clip_${gradId}`}>
              <rect
                x={PADDING.left}
                y={PADDING.top}
                width={chartW}
                height={chartH}
                rx={3}
              />
            </clipPath>
          </defs>

          {gridLines.map(({ y, label }, i) => (
            <g key={i}>
              <line
                x1={PADDING.left}
                y1={y}
                x2={containerWidth - PADDING.right}
                y2={y}
                stroke="rgba(255, 255, 255, 0.08)"
                strokeWidth={1}
                strokeDasharray={i === 0 ? "none" : "3 3"}
                vectorEffect="non-scaling-stroke"
              />
              {label && (
                <text
                  x={PADDING.left - 8}
                  y={y + 3.5}
                  textAnchor="end"
                  fill="var(--text-dim)"
                  fontSize={10}
                  fontFamily="inherit"
                >
                  {label}
                </text>
              )}
            </g>
          ))}

          <text
            x={PADDING.left}
            y={CHART_HEIGHT - 6}
            fill="var(--text-dim)"
            fontSize={10}
            textAnchor="start"
          >
            {maxPoints}s ago
          </text>
          <text
            x={containerWidth - PADDING.right}
            y={CHART_HEIGHT - 6}
            fill="var(--text-dim)"
            fontSize={10}
            textAnchor="end"
          >
            now
          </text>

          <rect
            x={PADDING.left}
            y={PADDING.top}
            width={chartW}
            height={chartH}
            fill="rgba(255, 255, 255, 0.01)"
            stroke="rgba(255, 255, 255, 0.08)"
            strokeWidth={1}
            rx={3}
            vectorEffect="non-scaling-stroke"
          />

          <g clipPath={`url(#clip_${gradId})`}>
            {areaPath && <path d={areaPath} fill={`url(#grad_${gradId})`} />}
            {points && (
              <polyline
                points={points}
                fill="none"
                stroke={color}
                strokeWidth={2}
                strokeLinejoin="round"
                strokeLinecap="round"
                vectorEffect="non-scaling-stroke"
              />
            )}
          </g>
        </svg>
      </div>
    </div>
  );
}
