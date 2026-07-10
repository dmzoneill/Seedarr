import { formatSpeed } from '../utils/formatters';

const CHART_WIDTH = 480;
const CHART_HEIGHT = 160;
const PADDING = { top: 8, right: 12, bottom: 20, left: 55 };

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

export interface LineChartProps {
  title: string;
  value: string;
  data: number[];
  color: string;
  maxPoints: number;
  isSpeed?: boolean;
  isRatio?: boolean;
}

export default function LineChart({ title, value, data, color, maxPoints, isSpeed, isRatio }: LineChartProps) {
  const chartW = CHART_WIDTH - PADDING.left - PADDING.right;
  const chartH = CHART_HEIGHT - PADDING.top - PADDING.bottom;

  const autoSpeed = isSpeed ?? (title.toLowerCase().includes('speed') || title.toLowerCase().includes('network'));
  const autoRatio = isRatio ?? title.toLowerCase().includes('ratio');

  let maxVal = 0;
  for (const v of data) {
    if (v > maxVal) maxVal = v;
  }
  const niceMax = getNiceMax(maxVal > 0 ? maxVal * 1.1 : 1);

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
        const first = PADDING.left;
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
              {formatYLabel(val, autoSpeed, autoRatio)}
            </text>
          </g>
        ))}
        <text x={PADDING.left} y={CHART_HEIGHT - 4} fill="var(--text-dim)" fontSize={8} textAnchor="start">5m ago</text>
        <text x={CHART_WIDTH - PADDING.right} y={CHART_HEIGHT - 4} fill="var(--text-dim)" fontSize={8} textAnchor="end">now</text>
        <rect x={PADDING.left} y={PADDING.top} width={chartW} height={chartH} fill="none" stroke="var(--border-light)" strokeWidth={0.5} />
        {areaPath && <path d={areaPath} fill={color} opacity={0.1} />}
        {points && (
          <polyline points={points} fill="none" stroke={color} strokeWidth={1.5} strokeLinejoin="round" strokeLinecap="round" />
        )}
      </svg>
    </div>
  );
}
