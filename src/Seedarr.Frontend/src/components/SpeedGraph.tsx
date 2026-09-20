import { useRef, useEffect, useState, useMemo } from "react";
import { useSpeedHistory, useSeedingStats } from "../api/hooks";
import { formatSpeed } from "../utils/formatters";
import {
  downsampleLTTB,
  SpeedRingBuffer,
  type SpeedDataPoint,
} from "../utils/downsample";

export type TimeRange = "60s" | "5m" | "15m" | "30m";

export interface TimeRangeConfig {
  value: TimeRange;
  label: string;
  points: number;
  startLabel: string;
  midLabel: string;
}

export const TIME_RANGES: Record<TimeRange, TimeRangeConfig> = {
  "60s": {
    value: "60s",
    label: "60s",
    points: 60,
    startLabel: "60s ago",
    midLabel: "30s ago",
  },
  "5m": {
    value: "5m",
    label: "5m",
    points: 300,
    startLabel: "5m ago",
    midLabel: "2.5m ago",
  },
  "15m": {
    value: "15m",
    label: "15m",
    points: 900,
    startLabel: "15m ago",
    midLabel: "7.5m ago",
  },
  "30m": {
    value: "30m",
    label: "30m",
    points: 1800,
    startLabel: "30m ago",
    midLabel: "15m ago",
  },
};

export const TIME_RANGE_OPTIONS: TimeRangeConfig[] = Object.values(TIME_RANGES);
export const MAX_BUFFER_POINTS = 1800;
const STORAGE_KEY = "seedarr_speedgraph_range";
const SCALE_HOLD_DELAY_MS = 8000;
const LERP_FACTOR = 0.15;

export const CANVAS_HEIGHT = 180;
export const CANVAS_PADDING = { top: 12, right: 24, bottom: 26, left: 75 };
export const PADDING = CANVAS_PADDING;
export const UPLOAD_COLOR = "#3498db";
export const DOWNLOAD_COLOR = "#2ecc71";

function safeRequestAnimationFrame(callback: FrameRequestCallback): number {
  if (
    typeof window !== "undefined" &&
    typeof window.requestAnimationFrame === "function"
  ) {
    return window.requestAnimationFrame(callback);
  }
  if (typeof requestAnimationFrame === "function") {
    return requestAnimationFrame(callback);
  }
  return setTimeout(callback, 16) as unknown as number;
}

function safeCancelAnimationFrame(id: number): void {
  if (
    typeof window !== "undefined" &&
    typeof window.cancelAnimationFrame === "function"
  ) {
    window.cancelAnimationFrame(id);
    return;
  }
  if (typeof cancelAnimationFrame === "function") {
    cancelAnimationFrame(id);
    return;
  }
  clearTimeout(id);
}

function getInitialRange(propMaxPoints?: number): TimeRange {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved && saved in TIME_RANGES) {
      return saved as TimeRange;
    }
  } catch {
    // localStorage might be unavailable
  }
  if (propMaxPoints) {
    if (propMaxPoints >= 1800) return "30m";
    if (propMaxPoints >= 900) return "15m";
    if (propMaxPoints >= 300) return "5m";
    return "60s";
  }
  return "60s";
}

interface SpeedGraphProps {
  maxPoints?: number;
}

export function getNiceMax(value: number): number {
  if (!Number.isFinite(value) || value <= 1024) return 1024;

  // Sub-megabyte speeds (KiB scale): binary power-of-two increments (1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 KiB)
  const kib = value / 1024;
  if (kib <= 1024) {
    const p = Math.pow(2, Math.ceil(Math.log2(kib) - 1e-9));
    return p * 1024;
  }

  // Megabyte and above (MiB, GiB, TiB scale): clean step multipliers on powers of 1024
  let unit = 1024 * 1024;
  while (value > unit * 1024) {
    unit *= 1024;
  }

  const normalized = value / unit;
  const MULTIPLIERS = [1, 2, 5, 10, 25, 50, 100, 250, 500];
  for (const m of MULTIPLIERS) {
    if (normalized <= m + 1e-9) {
      return m * unit;
    }
  }
  return 1024 * unit;
}

export function getGridLineCount(niceMax: number): number {
  if (niceMax < 1024 * 1024) {
    return 4;
  }
  let unit = 1024 * 1024;
  while (niceMax >= unit * 1024) {
    unit *= 1024;
  }
  const norm = Math.round(niceMax / unit);
  if (
    norm === 5 ||
    norm === 10 ||
    norm === 25 ||
    norm === 50 ||
    norm === 250 ||
    norm === 500
  ) {
    return 5;
  }
  return 4;
}

function SpeedGraph({ maxPoints }: SpeedGraphProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const gridCacheRef = useRef<HTMLCanvasElement | null>(null);
  const gridCacheKeyRef = useRef<string>("");

  const [containerWidth, setContainerWidth] = useState<number>(1000);
  const [selectedRange, setSelectedRange] = useState<TimeRange>(() =>
    getInitialRange(maxPoints),
  );
  const currentRangeConfig = TIME_RANGES[selectedRange];
  const ringBufferRef = useRef<SpeedRingBuffer>(
    new SpeedRingBuffer(MAX_BUFFER_POINTS),
  );
  const [history, setHistory] = useState<SpeedDataPoint[]>([]);
  const seededRef = useRef(false);
  const prevRef = useRef<{
    totalUploaded: number;
    totalDownloaded: number;
    timestamp: number;
  } | null>(null);

  const { data: serverHistory } = useSpeedHistory();
  const { data: stats } = useSeedingStats();

  let maxSpeed = 0;
  for (const point of history) {
    maxSpeed = Math.max(maxSpeed, point.uploadSpeed, point.downloadSpeed);
  }
  const rawNiceMax = getNiceMax(maxSpeed > 0 ? maxSpeed * 1.15 : 1024);

  const [targetNiceMax, setTargetNiceMax] = useState<number>(rawNiceMax);
  const [renderedMax, setRenderedMax] = useState<number>(rawNiceMax);

  const decayHoldRef = useRef<{
    dropStartTime: number;
    highestRawDuringDrop: number;
  } | null>(null);

  const renderedMaxRef = useRef<number>(renderedMax);
  renderedMaxRef.current = renderedMax;
  const targetMaxRef = useRef<number>(targetNiceMax);
  targetMaxRef.current = targetNiceMax;

  // Scale Hysteresis with Decay Hold Timer
  useEffect(() => {
    const now = Date.now();

    if (rawNiceMax > targetNiceMax) {
      // 1. Instant expansion: expand immediately to prevent line clipping
      setTargetNiceMax(rawNiceMax);
      decayHoldRef.current = null;
    } else if (rawNiceMax === targetNiceMax) {
      // Steady in current tier
      decayHoldRef.current = null;
    } else {
      // 2. Decay hold timer: bandwidth dropped below current tier
      if (!decayHoldRef.current) {
        decayHoldRef.current = {
          dropStartTime: now,
          highestRawDuringDrop: rawNiceMax,
        };
      } else {
        decayHoldRef.current.highestRawDuringDrop = Math.max(
          decayHoldRef.current.highestRawDuringDrop,
          rawNiceMax,
        );
      }

      const elapsed = now - decayHoldRef.current.dropStartTime;
      const remaining = Math.max(0, SCALE_HOLD_DELAY_MS - elapsed);

      const timer = setTimeout(() => {
        if (decayHoldRef.current) {
          const newTarget = Math.max(
            1024,
            decayHoldRef.current.highestRawDuringDrop,
          );
          setTargetNiceMax(newTarget);
          decayHoldRef.current = null;
        }
      }, remaining);

      return () => clearTimeout(timer);
    }
  }, [rawNiceMax, targetNiceMax]);

  // Smooth Exponential Interpolation (Lerp) towards targetNiceMax
  useEffect(() => {
    let animId: number;

    const animate = () => {
      const current = renderedMaxRef.current;
      const target = targetMaxRef.current;
      const diff = target - current;

      if (Math.abs(diff) < 0.5 || Math.abs(diff / target) < 0.002) {
        setRenderedMax(target);
        renderedMaxRef.current = target;
        return;
      }

      const next = current + diff * LERP_FACTOR;
      setRenderedMax(next);
      renderedMaxRef.current = next;
      animId = safeRequestAnimationFrame(animate);
    };

    if (Math.abs(renderedMaxRef.current - targetNiceMax) >= 0.5) {
      animId = safeRequestAnimationFrame(animate);
    }

    return () => {
      if (animId) {
        safeCancelAnimationFrame(animId);
      }
    };
  }, [targetNiceMax]);

  // ResizeObserver for Container Dimensions with clean disconnect
  useEffect(() => {
    if (!containerRef.current) return;
    const el = containerRef.current;
    if (el.clientWidth > 0) {
      setContainerWidth(el.clientWidth);
    }

    if (typeof ResizeObserver === "undefined") return;

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        if (entry.contentRect.width > 0) {
          setContainerWidth(entry.contentRect.width);
        }
      }
    });

    observer.observe(el);
    return () => {
      observer.disconnect();
    };
  }, []);

  // Cleanup cached offscreen canvas on unmount
  useEffect(() => {
    return () => {
      gridCacheRef.current = null;
    };
  }, []);

  useEffect(() => {
    if (!serverHistory || seededRef.current) return;
    seededRef.current = true;

    for (const s of serverHistory) {
      ringBufferRef.current.push(
        new Date(s.timestamp).getTime(),
        s.uploadSpeed,
        s.downloadSpeed,
      );
    }
    const initialPoints = ringBufferRef.current.getPoints(
      currentRangeConfig.points,
    );
    setHistory(initialPoints);

    if (serverHistory.length > 0) {
      const last = serverHistory[serverHistory.length - 1];
      prevRef.current = {
        totalUploaded: last.totalUploaded,
        totalDownloaded: last.totalDownloaded,
        timestamp: new Date(last.timestamp).getTime(),
      };

      let initialMax = 0;
      for (const p of initialPoints) {
        initialMax = Math.max(initialMax, p.uploadSpeed, p.downloadSpeed);
      }
      const initialNiceMax = getNiceMax(
        initialMax > 0 ? initialMax * 1.15 : 1024,
      );
      setTargetNiceMax(initialNiceMax);
      setRenderedMax(initialNiceMax);
      renderedMaxRef.current = initialNiceMax;
    }
  }, [serverHistory, currentRangeConfig.points]);

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

        ringBufferRef.current.push(now, uploadSpeed, downloadSpeed);
        setHistory(ringBufferRef.current.getPoints(currentRangeConfig.points));

        prevRef.current = {
          totalUploaded: stats.totalUploaded,
          totalDownloaded: stats.totalDownloaded,
          timestamp: now,
        };
      }
    } else {
      prevRef.current = {
        totalUploaded: stats.totalUploaded,
        totalDownloaded: stats.totalDownloaded,
        timestamp: now,
      };
    }
  }, [stats, currentRangeConfig.points]);

  const handleRangeChange = (range: TimeRange) => {
    setSelectedRange(range);
    const cfg = TIME_RANGES[range];
    if (cfg) {
      const points = ringBufferRef.current.getPoints(cfg.points);
      setHistory(points);
      decayHoldRef.current = null;
      let newMaxSpeed = 0;
      for (const p of points) {
        newMaxSpeed = Math.max(newMaxSpeed, p.uploadSpeed, p.downloadSpeed);
      }
      const newNiceMax = getNiceMax(
        newMaxSpeed > 0 ? newMaxSpeed * 1.15 : 1024,
      );
      setTargetNiceMax(newNiceMax);
    }
    try {
      localStorage.setItem(STORAGE_KEY, range);
    } catch {
      // ignore localStorage errors
    }
  };

  const chartWidth = Math.max(
    100,
    Math.max(300, containerWidth) - PADDING.left - PADDING.right,
  );

  const indexedHistory = useMemo(() => {
    return history.map((pt, idx) => ({
      ...pt,
      index: idx,
    }));
  }, [history]);

  // Downsample matching visual resolution between 120 and 250 points
  const targetPoints = Math.min(250, Math.max(120, Math.floor(chartWidth / 4)));

  const displayUpload = useMemo(() => {
    if (indexedHistory.length <= targetPoints) {
      return indexedHistory;
    }
    return downsampleLTTB(
      indexedHistory,
      targetPoints,
      (d) => d.index,
      (d) => d.uploadSpeed,
    );
  }, [indexedHistory, targetPoints]);

  const displayDownload = useMemo(() => {
    if (indexedHistory.length <= targetPoints) {
      return indexedHistory;
    }
    return downsampleLTTB(
      indexedHistory,
      targetPoints,
      (d) => d.index,
      (d) => d.downloadSpeed,
    );
  }, [indexedHistory, targetPoints]);

  // Render HTML5 Canvas Telemetry Layer via requestAnimationFrame
  useEffect(() => {
    let animId: number;

    const render = () => {
      const canvas = canvasRef.current;
      if (!canvas) return;

      const ctx = canvas.getContext("2d");
      if (!ctx) return;

      const dpr =
        typeof window !== "undefined" && window.devicePixelRatio
          ? window.devicePixelRatio
          : 1;

      const rect = canvas.getBoundingClientRect();
      const width = Math.max(300, rect.width || containerWidth);
      const height = rect.height || CANVAS_HEIGHT;

      const targetWidth = Math.round(width * dpr);
      const targetHeight = Math.round(height * dpr);

      if (canvas.width !== targetWidth || canvas.height !== targetHeight) {
        canvas.width = targetWidth;
        canvas.height = targetHeight;
      }

      ctx.save();
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.scale(dpr, dpr);
      ctx.imageSmoothingEnabled = true;
      ctx.clearRect(0, 0, width, height);

      const curChartWidth = Math.max(
        100,
        width - PADDING.left - PADDING.right,
      );
      const curChartHeight = height - PADDING.top - PADDING.bottom;
      const niceMax = Math.max(1024, renderedMaxRef.current);

      // --- Static Grid & Labels Layer (Double-buffering / OffscreenCanvas Cache) ---
      const cacheKey = `${width}x${height}@${dpr}:${targetNiceMax}:${currentRangeConfig.value}`;
      if (!gridCacheRef.current && typeof document !== "undefined") {
        gridCacheRef.current = document.createElement("canvas");
      }

      let drawnFromCache = false;
      if (gridCacheRef.current) {
        const offCanvas = gridCacheRef.current;
        if (gridCacheKeyRef.current !== cacheKey) {
          offCanvas.width = targetWidth;
          offCanvas.height = targetHeight;
          const offCtx = offCanvas.getContext("2d");
          if (offCtx) {
            offCtx.setTransform(1, 0, 0, 1, 0, 0);
            offCtx.scale(dpr, dpr);
            offCtx.imageSmoothingEnabled = true;
            offCtx.clearRect(0, 0, width, height);

            // Background grid box
            offCtx.fillStyle = "rgba(255, 255, 255, 0.015)";
            offCtx.strokeStyle = "rgba(255, 255, 255, 0.06)";
            offCtx.lineWidth = 1;
            offCtx.beginPath();
            if (typeof offCtx.roundRect === "function") {
              offCtx.roundRect(
                PADDING.left,
                PADDING.top,
                curChartWidth,
                curChartHeight,
                4,
              );
            } else {
              offCtx.rect(
                PADDING.left,
                PADDING.top,
                curChartWidth,
                curChartHeight,
              );
            }
            offCtx.fill();
            offCtx.stroke();

            // Grid lines & speed tick labels
            const gridLineCount = getGridLineCount(targetNiceMax);
            offCtx.font =
              '10px -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
            offCtx.textAlign = "right";
            offCtx.textBaseline = "middle";

            for (let i = 0; i <= gridLineCount; i++) {
              const value = (targetNiceMax / gridLineCount) * i;
              const y =
                PADDING.top +
                curChartHeight -
                (i / gridLineCount) * curChartHeight;

              offCtx.strokeStyle = "rgba(255, 255, 255, 0.06)";
              offCtx.lineWidth = 1;
              offCtx.setLineDash(i === 0 ? [] : [3, 3]);
              offCtx.beginPath();
              offCtx.moveTo(PADDING.left, y);
              offCtx.lineTo(width - PADDING.right, y);
              offCtx.stroke();

              offCtx.fillStyle = "rgba(160, 160, 160, 0.7)";
              offCtx.fillText(formatSpeed(value), PADDING.left - 8, y);
            }
            offCtx.setLineDash([]);

            // Time axis labels
            offCtx.font =
              '9.5px -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
            offCtx.fillStyle = "rgba(160, 160, 160, 0.7)";
            offCtx.textBaseline = "alphabetic";

            offCtx.textAlign = "left";
            offCtx.fillText(
              currentRangeConfig.startLabel,
              PADDING.left,
              height - 6,
            );

            offCtx.textAlign = "center";
            offCtx.fillText(
              currentRangeConfig.midLabel,
              PADDING.left + curChartWidth / 2,
              height - 6,
            );

            offCtx.textAlign = "right";
            offCtx.fillText("now", width - PADDING.right, height - 6);

            gridCacheKeyRef.current = cacheKey;
          }
        }
        ctx.drawImage(offCanvas, 0, 0, width, height);
        drawnFromCache = true;
      }

      if (!drawnFromCache) {
        // Fallback direct draw if offscreen canvas is unavailable
        ctx.fillStyle = "rgba(255, 255, 255, 0.015)";
        ctx.strokeStyle = "rgba(255, 255, 255, 0.06)";
        ctx.lineWidth = 1;
        ctx.beginPath();
        if (typeof ctx.roundRect === "function") {
          ctx.roundRect(
            PADDING.left,
            PADDING.top,
            curChartWidth,
            curChartHeight,
            4,
          );
        } else {
          ctx.rect(
            PADDING.left,
            PADDING.top,
            curChartWidth,
            curChartHeight,
          );
        }
        ctx.fill();
        ctx.stroke();

        const gridLineCount = getGridLineCount(targetNiceMax);
        ctx.font =
          '10px -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
        ctx.textAlign = "right";
        ctx.textBaseline = "middle";

        for (let i = 0; i <= gridLineCount; i++) {
          const value = (targetNiceMax / gridLineCount) * i;
          const y =
            PADDING.top +
            curChartHeight -
            (i / gridLineCount) * curChartHeight;

          ctx.strokeStyle = "rgba(255, 255, 255, 0.06)";
          ctx.lineWidth = 1;
          ctx.setLineDash(i === 0 ? [] : [3, 3]);
          ctx.beginPath();
          ctx.moveTo(PADDING.left, y);
          ctx.lineTo(width - PADDING.right, y);
          ctx.stroke();

          ctx.fillStyle = "rgba(160, 160, 160, 0.7)";
          ctx.fillText(formatSpeed(value), PADDING.left - 8, y);
        }
        ctx.setLineDash([]);

        ctx.font =
          '9.5px -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
        ctx.fillStyle = "rgba(160, 160, 160, 0.7)";
        ctx.textBaseline = "alphabetic";

        ctx.textAlign = "left";
        ctx.fillText(
          currentRangeConfig.startLabel,
          PADDING.left,
          height - 6,
        );

        ctx.textAlign = "center";
        ctx.fillText(
          currentRangeConfig.midLabel,
          PADDING.left + curChartWidth / 2,
          height - 6,
        );

        ctx.textAlign = "right";
        ctx.fillText("now", width - PADDING.right, height - 6);
      }

      // --- Dynamic Telemetry Line Paths & Dual-Channel Gradients ---
      ctx.globalCompositeOperation = "source-over";

      const windowPoints = currentRangeConfig.points;
      const offset =
        windowPoints > history.length ? windowPoints - history.length : 0;

      const renderChannel = (
        data: (SpeedDataPoint & { index: number })[],
        key: "uploadSpeed" | "downloadSpeed",
        strokeColor: string,
        fillRgbaStart: string,
        fillRgbaEnd: string,
      ) => {
        if (data.length === 0) return;

        const bottom = PADDING.top + curChartHeight;

        // Area fill
        if (data.length >= 2) {
          const firstX =
            PADDING.left +
            ((offset + data[0].index) / Math.max(1, windowPoints - 1)) *
              curChartWidth;
          const lastX =
            PADDING.left +
            ((offset + data[data.length - 1].index) /
              Math.max(1, windowPoints - 1)) *
              curChartWidth;

          ctx.beginPath();
          ctx.moveTo(firstX, bottom);
          for (let i = 0; i < data.length; i++) {
            const pt = data[i];
            const px =
              PADDING.left +
              ((offset + pt.index) / Math.max(1, windowPoints - 1)) *
                curChartWidth;
            const py = Math.max(
              PADDING.top,
              PADDING.top +
                curChartHeight -
                (pt[key] / niceMax) * curChartHeight,
            );
            ctx.lineTo(px, py);
          }
          ctx.lineTo(lastX, bottom);
          ctx.closePath();

          const grad = ctx.createLinearGradient(0, PADDING.top, 0, bottom);
          grad.addColorStop(0, fillRgbaStart);
          grad.addColorStop(1, fillRgbaEnd);
          ctx.fillStyle = grad;
          ctx.fill();
        }

        // Line stroke
        ctx.beginPath();
        for (let i = 0; i < data.length; i++) {
          const pt = data[i];
          const px =
            PADDING.left +
            ((offset + pt.index) / Math.max(1, windowPoints - 1)) *
              curChartWidth;
          const py = Math.max(
            PADDING.top,
            PADDING.top +
              curChartHeight -
              (pt[key] / niceMax) * curChartHeight,
          );
          if (i === 0) {
            ctx.moveTo(px, py);
          } else {
            ctx.lineTo(px, py);
          }
        }
        ctx.strokeStyle = strokeColor;
        ctx.lineWidth = 2;
        ctx.lineJoin = "round";
        ctx.lineCap = "round";
        ctx.stroke();

        // Single point fallback dot
        if (data.length === 1) {
          const pt = data[0];
          const px =
            PADDING.left +
            ((offset + pt.index) / Math.max(1, windowPoints - 1)) *
              curChartWidth;
          const py = Math.max(
            PADDING.top,
            PADDING.top +
              curChartHeight -
              (pt[key] / niceMax) * curChartHeight,
          );
          ctx.beginPath();
          ctx.arc(px, py, 2, 0, Math.PI * 2);
          ctx.fillStyle = strokeColor;
          ctx.fill();
        }
      };

      // Clip dynamic line paths to chart area
      ctx.save();
      ctx.beginPath();
      ctx.rect(PADDING.left, PADDING.top, curChartWidth, curChartHeight);
      ctx.clip();

      // Download channel: stroke #2ecc71 (width 2), linear gradient fill #2ecc71 (alpha 0.25 fading to 0.0)
      renderChannel(
        displayDownload,
        "downloadSpeed",
        DOWNLOAD_COLOR,
        "rgba(46, 204, 113, 0.25)",
        "rgba(46, 204, 113, 0.0)",
      );

      // Upload channel: stroke #3498db (width 2), linear gradient fill #3498db (alpha 0.25 fading to 0.0)
      renderChannel(
        displayUpload,
        "uploadSpeed",
        UPLOAD_COLOR,
        "rgba(52, 152, 219, 0.25)",
        "rgba(52, 152, 219, 0.0)",
      );

      ctx.restore(); // restore clip
      ctx.restore(); // restore transform
    };

    animId = safeRequestAnimationFrame(render);

    return () => {
      if (animId) {
        safeCancelAnimationFrame(animId);
      }
    };
  }, [
    renderedMax,
    displayUpload,
    displayDownload,
    targetNiceMax,
    containerWidth,
    currentRangeConfig,
    history.length,
  ]);

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
        border: "1px solid var(--border-light)",
        marginBottom: "1.25rem",
        padding: "1rem 1.25rem",
      }}
    >
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "0.75rem",
          flexWrap: "wrap",
          gap: "0.75rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
          <h3
            style={{
              margin: 0,
              border: "none",
              padding: 0,
              fontSize: "1.05rem",
            }}
          >
            Transfer Speed
          </h3>
          <span
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "0.3rem",
              fontSize: "0.72rem",
              color: "var(--accent, #c8a84e)",
              background: "rgba(200, 168, 78, 0.12)",
              padding: "0.15rem 0.5rem",
              borderRadius: "4px",
              fontWeight: 600,
            }}
          >
            <span
              style={{
                width: 6,
                height: 6,
                borderRadius: "50%",
                backgroundColor: "var(--accent, #c8a84e)",
                display: "inline-block",
                animation: "pulse 2s infinite",
              }}
            />
            Live (1s)
          </span>
        </div>

        {/* Time-Window Range Selector Buttons */}
        <div
          className="speed-graph-ranges"
          style={{
            display: "inline-flex",
            alignItems: "center",
            background: "rgba(255, 255, 255, 0.05)",
            borderRadius: "6px",
            padding: "2px",
            gap: "2px",
            border: "1px solid var(--border-light, rgba(255, 255, 255, 0.1))",
          }}
          role="group"
          aria-label="Time window range"
        >
          {TIME_RANGE_OPTIONS.map((opt) => {
            const isActive = selectedRange === opt.value;
            return (
              <button
                key={opt.value}
                type="button"
                onClick={() => handleRangeChange(opt.value)}
                style={{
                  background: isActive
                    ? "var(--accent, #c8a84e)"
                    : "transparent",
                  color: isActive ? "#000" : "var(--text-muted, #888)",
                  fontWeight: isActive ? 600 : 400,
                  border: "none",
                  borderRadius: "4px",
                  padding: "0.2rem 0.55rem",
                  fontSize: "0.75rem",
                  cursor: "pointer",
                  transition: "all 0.15s ease",
                  lineHeight: 1.2,
                }}
                className={`speed-range-btn ${isActive ? "active" : ""}`}
              >
                {opt.label}
              </button>
            );
          })}
        </div>

        <div
          className="speed-graph-legend"
          style={{ margin: 0, display: "flex", gap: "1rem" }}
        >
          <span
            className="speed-graph-legend-item"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              fontSize: "0.82rem",
            }}
          >
            <span
              className="speed-graph-indicator"
              style={{
                width: 10,
                height: 10,
                borderRadius: "50%",
                backgroundColor: UPLOAD_COLOR,
                display: "inline-block",
              }}
            />
            Upload:{" "}
            <strong style={{ color: UPLOAD_COLOR }}>
              {formatSpeed(currentUpload)}
            </strong>
          </span>
          <span
            className="speed-graph-legend-item"
            style={{
              display: "flex",
              alignItems: "center",
              gap: "0.4rem",
              fontSize: "0.82rem",
            }}
          >
            <span
              className="speed-graph-indicator"
              style={{
                width: 10,
                height: 10,
                borderRadius: "50%",
                backgroundColor: DOWNLOAD_COLOR,
                display: "inline-block",
              }}
            />
            Download:{" "}
            <strong style={{ color: DOWNLOAD_COLOR }}>
              {formatSpeed(currentDownload)}
            </strong>
          </span>
        </div>
      </div>

      <div
        className="speed-graph"
        ref={containerRef}
        style={{
          width: "100%",
          height: `${CANVAS_HEIGHT}px`,
          overflow: "hidden",
          position: "relative",
        }}
      >
        <canvas
          ref={canvasRef}
          role="img"
          aria-label="Transfer speed history graph"
          style={{ width: "100%", height: "100%", display: "block" }}
        />
      </div>
    </div>
  );
}

export default SpeedGraph;
