import React, {
  useState,
  useEffect,
  useMemo,
  useRef,
  useCallback,
} from "react";
import { formatBytes } from "../utils/formatters";
import { usePieceMap, useTorrentFiles } from "../api/hooks";
import type { TorrentFileInfo } from "../api/types";

export interface PieceMapProps {
  torrentId?: number;
  pieceCount: number;
  pieceLength: number;
  progress: number; // 0.0 - 1.0
  isSeeding?: boolean;
  className?: string;
  files?: TorrentFileInfo[];
  initialLayout?: "bar" | "grid";
}

export type PieceMapViewMode = "status" | "rarity" | "files";

interface HoveredPieceInfo {
  pieceIndex: number;
  pieceEndIndex: number;
  isRange: boolean;
  byteRange: string;
  state: number;
  statusText: string;
  statusColor: string;
  availability: number;
  files: { name: string; offset: number; size: number }[];
  clientX: number;
  clientY: number;
}

interface FileBoundary {
  file: TorrentFileInfo;
  startByte: number;
  endByte: number;
  startPiece: number;
  endPiece: number;
  colorIndex: number;
}

const FILE_PALETTE = [
  "#3498db",
  "#9b59b6",
  "#e67e22",
  "#1abc9c",
  "#f39c12",
  "#e74c3c",
  "#2ecc71",
  "#e84393",
  "#00cec9",
  "#6c5ce7",
];

export interface PieceBlock {
  index: number;
  status: "complete" | "missing" | "active";
}

export const NUM_BLOCKS = 120;

export function calculateBlocks(
  progressOrNumBlocks: number = 0,
  isSeedingOrTotalPieces: boolean | number = false,
  progressArg?: number,
  isSeedingArg?: boolean,
): PieceBlock[] {
  let numBlocks = NUM_BLOCKS;
  let totalPieces = 100;
  let progress = 0;
  let isSeeding = false;

  if (typeof isSeedingOrTotalPieces === "boolean") {
    // calculateBlocks(progress, isSeeding)
    progress = progressOrNumBlocks;
    isSeeding = isSeedingOrTotalPieces;
  } else if (typeof isSeedingOrTotalPieces === "number") {
    // calculateBlocks(numBlocks, totalPieces, progress, isSeeding)
    numBlocks = progressOrNumBlocks;
    totalPieces = isSeedingOrTotalPieces;
    progress = progressArg ?? 0;
    isSeeding = isSeedingArg ?? false;
  } else {
    progress = progressOrNumBlocks;
  }

  const blocks: PieceBlock[] = [];
  for (let i = 0; i < numBlocks; i++) {
    const blockProgress = (i + 0.5) / numBlocks;
    const isComplete =
      progress >= 1.0 ||
      (isSeeding && progress >= 1.0) ||
      blockProgress <= progress;
    let status: "complete" | "missing" | "active" = "missing";

    if (isComplete) {
      status = "complete";
    } else if (
      blockProgress <= progress + 0.05 &&
      progress > 0 &&
      progress < 1
    ) {
      status = "active";
    }

    blocks.push({
      index: Math.floor((i / numBlocks) * totalPieces),
      status,
    });
  }

  return blocks;
}

export interface PieceMapStats {
  verified: number;
  inFlight: number;
  corrupted: number;
  missing: number;
  completeCount: number;
  remainingCount: number;
  verifiedPercent: string;
}

export function calculatePieceStats(
  pieceStates: Uint8Array,
  totalPieces: number,
  progress: number,
): PieceMapStats {
  let verified = 0;
  let inFlight = 0;
  let corrupted = 0;
  let missing = 0;
  for (let i = 0; i < totalPieces; i++) {
    const s = pieceStates[i];
    if (s === 2) verified++;
    else if (s === 1) inFlight++;
    else if (s === 3) corrupted++;
    else missing++;
  }
  return {
    verified,
    inFlight,
    corrupted,
    missing,
    completeCount: verified,
    remainingCount: totalPieces - verified,
    verifiedPercent: (progress * 100).toFixed(1),
  };
}

export function calculatePieceStates(
  totalPieces: number,
  progress: number,
  isSeeding: boolean = false,
  pieceMapData?: {
    spans?: { count: number; state: number }[];
    rleSpans?: [number, number][];
  } | null,
): Uint8Array {
  const states = new Uint8Array(totalPieces);
  if (pieceMapData?.rleSpans && pieceMapData.rleSpans.length > 0) {
    let offset = 0;
    for (const [count, state] of pieceMapData.rleSpans) {
      states.fill(state, offset, Math.min(totalPieces, offset + count));
      offset += count;
    }
  } else if (pieceMapData?.spans && pieceMapData.spans.length > 0) {
    let offset = 0;
    for (const span of pieceMapData.spans) {
      states.fill(
        span.state,
        offset,
        Math.min(totalPieces, offset + span.count),
      );
      offset += span.count;
    }
  } else {
    // Fallback simulation when API piece data is not yet loaded
    const completed = Math.floor(progress * totalPieces);
    const isComplete = progress >= 1.0 || (isSeeding && progress >= 1.0);
    for (let i = 0; i < totalPieces; i++) {
      const pieceProgress = (i + 0.5) / totalPieces;
      if (isComplete || i < completed || pieceProgress <= progress) {
        states[i] = 2; // Completed
      } else if (i === completed && progress > 0 && progress < 1) {
        states[i] = 1; // In-flight
      } else {
        states[i] = 0; // Missing
      }
    }
  }
  return states;
}

export function PieceMap({
  torrentId,
  pieceCount,
  pieceLength,
  progress,
  isSeeding = false,
  className = "",
  files: propFiles,
  initialLayout = "grid",
}: PieceMapProps) {
  const [layoutMode, setLayoutMode] = useState<"bar" | "grid">(initialLayout);
  const [viewMode, setViewMode] = useState<PieceMapViewMode>("status");
  const [hoveredInfo, setHoveredInfo] = useState<HoveredPieceInfo | null>(null);
  const [selectedFileIndex, setSelectedFileIndex] = useState<number | null>(
    null,
  );
  const [hoveredFileIndex, setHoveredFileIndex] = useState<number | null>(null);

  const containerRef = useRef<HTMLDivElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const barContainerRef = useRef<HTMLDivElement | null>(null);
  const barCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const [containerWidth, setContainerWidth] = useState<number>(650);

  // Fetch authentic piece map data and files
  const { data: pieceMapData } = usePieceMap(torrentId);
  const { data: fetchedFiles } = useTorrentFiles(torrentId ?? 0);
  const files = propFiles || fetchedFiles || [];

  const totalPieces = Math.max(1, pieceMapData?.totalPieces || pieceCount || 1);
  const effectivePieceLength = Math.max(
    16384,
    pieceMapData?.pieceLength || pieceLength || 262144,
  );
  const totalBytes = totalPieces * effectivePieceLength;

  // Track container width with ResizeObserver
  useEffect(() => {
    const el =
      layoutMode === "bar" ? barContainerRef.current : containerRef.current;
    if (!el) return;

    const handleResize = () => {
      if (el.clientWidth > 0) {
        setContainerWidth(el.clientWidth);
      }
    };

    handleResize();
    const observer = new ResizeObserver(handleResize);
    observer.observe(el);
    return () => observer.disconnect();
  }, [layoutMode]);

  // Decompress states array: 0=Missing, 1=In-flight, 2=Completed/Verified, 3=Corrupted
  const pieceStates = useMemo(() => {
    return calculatePieceStates(totalPieces, progress, isSeeding, pieceMapData);
  }, [pieceMapData, totalPieces, progress, isSeeding]);

  // Decompress rarity array (peer availability count)
  const pieceRarity = useMemo(() => {
    const rarity = new Uint16Array(totalPieces);
    if (pieceMapData?.rarity && pieceMapData.rarity.length > 0) {
      rarity.set(pieceMapData.rarity.slice(0, totalPieces));
    } else if (
      pieceMapData?.raritySpans &&
      pieceMapData.raritySpans.length > 0
    ) {
      let offset = 0;
      for (const [count, val] of pieceMapData.raritySpans) {
        rarity.fill(val, offset, Math.min(totalPieces, offset + count));
        offset += count;
      }
    } else {
      // Fallback swarm availability
      const defaultAvail =
        isSeeding && progress >= 1.0 ? 3 : progress >= 1.0 ? 5 : 1;
      rarity.fill(defaultAvail);
    }
    return rarity;
  }, [pieceMapData, totalPieces, isSeeding, progress]);

  // Compute file boundaries
  const fileBoundaries = useMemo<FileBoundary[]>(() => {
    if (!files || files.length === 0) return [];
    let curByte = 0;
    return files.map((file, idx) => {
      const startByte = curByte;
      const endByte = curByte + file.size;
      curByte = endByte;
      const startPiece = Math.floor(startByte / effectivePieceLength);
      const endPiece = Math.max(
        startPiece,
        Math.floor(Math.max(0, endByte - 1) / effectivePieceLength),
      );
      return {
        file,
        startByte,
        endByte,
        startPiece,
        endPiece,
        colorIndex: idx,
      };
    });
  }, [files, effectivePieceLength]);

  // Calculate grid layout and dynamic binning
  const gridLayout = useMemo(() => {
    const cellSize = 12;
    const gap = 2;
    const padding = 2;
    const availableWidth = Math.max(200, containerWidth - padding * 2);
    const cols = Math.max(10, Math.floor((availableWidth + gap) / cellSize));

    const idealRows = Math.ceil(totalPieces / cols);
    const maxRows = 16;
    const minRows = 3;
    const rows = Math.min(maxRows, Math.max(minRows, idealRows));

    const totalCells = cols * rows;
    const isBinned = totalPieces > totalCells;
    const piecesPerCell = isBinned ? totalPieces / totalCells : 1;
    const activeCells = isBinned
      ? totalCells
      : Math.min(totalCells, totalPieces);

    const width = cols * cellSize - gap + padding * 2;
    const height = rows * cellSize - gap + padding * 2;

    return {
      cellSize,
      gap,
      padding,
      cols,
      rows,
      totalCells,
      isBinned,
      piecesPerCell,
      activeCells,
      width,
      height,
    };
  }, [containerWidth, totalPieces]);

  const getStatusColor = useCallback((state: number): string => {
    switch (state) {
      case 2:
        return "#27ae60"; // Completed/Verified green
      case 1:
        return "#3498db"; // In-flight blue
      case 3:
        return "#e74c3c"; // Corrupted red
      case 0:
      default:
        return "#282520"; // Missing gray
    }
  }, []);

  const getRarityColor = useCallback((count: number): string => {
    if (count <= 0) return "#282520"; // Missing / 0
    if (count === 1) return "#e74c3c"; // Rare red
    if (count <= 3) return "#e67e22"; // 2-3 orange
    if (count <= 5) return "#f1c40f"; // 4-5 yellow
    if (count <= 9) return "#82c91e"; // 6-9 lime/yellow-green
    return "#27ae60"; // 10+ green
  }, []);

  // Active highlighted file
  const activeFileIndex =
    hoveredFileIndex !== null ? hoveredFileIndex : selectedFileIndex;
  const activeFileBoundary =
    activeFileIndex !== null && fileBoundaries[activeFileIndex]
      ? fileBoundaries[activeFileIndex]
      : null;

  // Render Grid onto Canvas
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const {
      cellSize,
      gap,
      padding,
      cols,
      rows,
      piecesPerCell,
      activeCells,
      width,
      height,
    } = gridLayout;
    const dpr = window.devicePixelRatio || 1;

    canvas.width = width * dpr;
    canvas.height = height * dpr;
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;

    ctx.save();
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, width, height);

    const blockSize = cellSize - gap;

    for (let i = 0; i < activeCells; i++) {
      const col = i % cols;
      const row = Math.floor(i / cols);
      const x = padding + col * cellSize;
      const y = padding + row * cellSize;

      const pStart = Math.floor(i * piecesPerCell);
      const pEnd = Math.min(
        totalPieces - 1,
        Math.floor((i + 1) * piecesPerCell) - 1,
      );
      const spanLen = Math.max(1, pEnd - pStart + 1);

      let fillColor = "#282520";

      if (viewMode === "status") {
        if (piecesPerCell <= 1) {
          fillColor = getStatusColor(pieceStates[pStart]);
        } else {
          // Dynamic binning aggregation
          let corrupted = 0;
          let verified = 0;
          let inFlight = 0;
          for (let p = pStart; p <= pEnd; p++) {
            const s = pieceStates[p];
            if (s === 3) corrupted++;
            else if (s === 2) verified++;
            else if (s === 1) inFlight++;
          }

          if (corrupted > 0) {
            fillColor = "#e74c3c";
          } else if (verified === spanLen) {
            fillColor = "#27ae60";
          } else if (inFlight > 0) {
            fillColor = "#3498db";
          } else if (verified > 0) {
            // Partially verified
            const pct = verified / spanLen;
            fillColor = pct >= 0.5 ? "#1e824c" : "#1a5336";
          } else {
            fillColor = "#282520";
          }
        }
      } else if (viewMode === "rarity") {
        if (piecesPerCell <= 1) {
          fillColor = getRarityColor(pieceRarity[pStart] || 0);
        } else {
          let sum = 0;
          for (let p = pStart; p <= pEnd; p++) {
            sum += pieceRarity[p] || 0;
          }
          const avgRarity = Math.round(sum / spanLen);
          fillColor = getRarityColor(avgRarity);
        }
      } else if (viewMode === "files") {
        // Mode C: File Boundary Overlays
        if (activeFileBoundary) {
          // Check if this bin overlaps with the active file
          const overlaps =
            pStart <= activeFileBoundary.endPiece &&
            pEnd >= activeFileBoundary.startPiece;
          if (overlaps) {
            fillColor =
              FILE_PALETTE[activeFileBoundary.colorIndex % FILE_PALETTE.length];
          } else {
            fillColor = "#1a1815";
          }
        } else {
          // Color by containing file
          const containingFb = fileBoundaries.find(
            (fb) => pStart <= fb.endPiece && pEnd >= fb.startPiece,
          );
          if (containingFb) {
            fillColor =
              FILE_PALETTE[containingFb.colorIndex % FILE_PALETTE.length];
          } else {
            fillColor = getStatusColor(pieceStates[pStart]);
          }
        }
      }

      ctx.fillStyle = fillColor;
      ctx.fillRect(x, y, blockSize, blockSize);

      // In Mode C: draw subtle file boundary vertical divider if a file starts inside this cell
      if (
        viewMode === "files" &&
        !activeFileBoundary &&
        fileBoundaries.length > 1
      ) {
        const boundaryInCell = fileBoundaries.some(
          (fb) =>
            fb.startPiece >= pStart &&
            fb.startPiece <= pEnd &&
            fb.startPiece > 0,
        );
        if (boundaryInCell) {
          ctx.fillStyle = "rgba(255, 255, 255, 0.7)";
          ctx.fillRect(x, y, 1.5, blockSize);
        }
      }

      // Hover outline
      if (
        hoveredInfo &&
        ((!hoveredInfo.isRange && hoveredInfo.pieceIndex === pStart) ||
          (hoveredInfo.isRange &&
            hoveredInfo.pieceIndex <= pEnd &&
            hoveredInfo.pieceEndIndex >= pStart))
      ) {
        ctx.strokeStyle = "#ffffff";
        ctx.lineWidth = 1.5;
        ctx.strokeRect(x - 0.5, y - 0.5, blockSize + 1, blockSize + 1);
      }
    }

    ctx.restore();
  }, [
    gridLayout,
    viewMode,
    totalPieces,
    pieceStates,
    pieceRarity,
    fileBoundaries,
    activeFileBoundary,
    hoveredInfo,
    getStatusColor,
    getRarityColor,
  ]);

  // Render Linear Bar directly onto Canvas
  useEffect(() => {
    if (layoutMode !== "bar") return;
    const canvas = barCanvasRef.current;
    const container = barContainerRef.current;
    if (!canvas || !container) return;

    const availWidth = Math.max(100, containerWidth - 4);
    const height = 24;
    const dpr = window.devicePixelRatio || 1;

    canvas.width = Math.floor(availWidth * dpr);
    canvas.height = Math.floor(height * dpr);
    canvas.style.width = `${availWidth}px`;
    canvas.style.height = `${height}px`;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    ctx.save();
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, availWidth, height);

    const numSlices = Math.min(
      totalPieces,
      Math.max(50, Math.floor(availWidth)),
    );
    const piecesPerSlice = totalPieces / numSlices;

    for (let i = 0; i < numSlices; i++) {
      const x0 = (i / numSlices) * availWidth;
      const x1 = ((i + 1) / numSlices) * availWidth;
      const sliceW = Math.max(0.5, x1 - x0);

      const pStart = Math.floor(i * piecesPerSlice);
      const pEnd = Math.min(
        totalPieces - 1,
        Math.floor((i + 1) * piecesPerSlice) - 1,
      );
      const spanLen = Math.max(1, pEnd - pStart + 1);

      let fillColor = "#282520";

      if (viewMode === "status") {
        if (spanLen <= 1) {
          fillColor = getStatusColor(pieceStates[pStart]);
        } else {
          let corrupted = 0;
          let verified = 0;
          let inFlight = 0;
          for (let p = pStart; p <= pEnd; p++) {
            const s = pieceStates[p];
            if (s === 3) corrupted++;
            else if (s === 2) verified++;
            else if (s === 1) inFlight++;
          }
          if (corrupted > 0) {
            fillColor = "#e74c3c";
          } else if (verified === spanLen) {
            fillColor = "#27ae60";
          } else if (inFlight > 0) {
            fillColor = "#3498db";
          } else if (verified > 0) {
            const pct = verified / spanLen;
            fillColor = pct >= 0.5 ? "#1e824c" : "#1a5336";
          } else {
            fillColor = "#282520";
          }
        }
      } else if (viewMode === "rarity") {
        if (spanLen <= 1) {
          fillColor = getRarityColor(pieceRarity[pStart] || 0);
        } else {
          let sum = 0;
          for (let p = pStart; p <= pEnd; p++) {
            sum += pieceRarity[p] || 0;
          }
          fillColor = getRarityColor(Math.round(sum / spanLen));
        }
      } else if (viewMode === "files") {
        if (activeFileBoundary) {
          const overlaps =
            pStart <= activeFileBoundary.endPiece &&
            pEnd >= activeFileBoundary.startPiece;
          fillColor = overlaps
            ? FILE_PALETTE[activeFileBoundary.colorIndex % FILE_PALETTE.length]
            : "#1a1815";
        } else {
          const containingFb = fileBoundaries.find(
            (fb) => pStart <= fb.endPiece && pEnd >= fb.startPiece,
          );
          fillColor = containingFb
            ? FILE_PALETTE[containingFb.colorIndex % FILE_PALETTE.length]
            : getStatusColor(pieceStates[pStart]);
        }
      }

      ctx.fillStyle = fillColor;
      ctx.fillRect(x0, 0, sliceW, height);
    }

    // Draw active hover highlight
    if (hoveredInfo) {
      const hStart = (hoveredInfo.pieceIndex / totalPieces) * availWidth;
      const hEnd = ((hoveredInfo.pieceEndIndex + 1) / totalPieces) * availWidth;
      const hW = Math.max(3, hEnd - hStart);

      ctx.strokeStyle = "#ffd166";
      ctx.lineWidth = 2;
      ctx.strokeRect(hStart, 1, hW, height - 2);
    }

    ctx.restore();
  }, [
    layoutMode,
    containerWidth,
    totalPieces,
    pieceStates,
    pieceRarity,
    fileBoundaries,
    activeFileBoundary,
    hoveredInfo,
    viewMode,
    getStatusColor,
    getRarityColor,
  ]);

  // Resolve containing files and offsets for any piece
  const getFilesForPiece = useCallback(
    (pIdx: number) => {
      const pStartByte = pIdx * effectivePieceLength;
      const results: { name: string; offset: number; size: number }[] = [];

      for (const fb of fileBoundaries) {
        if (fb.startPiece <= pIdx && fb.endPiece >= pIdx) {
          const fileOffset = Math.max(0, pStartByte - fb.startByte);
          const fileName = fb.file.path.split("/").pop() || fb.file.path;
          results.push({
            name: fileName,
            offset: fileOffset,
            size: fb.file.size,
          });
        }
      }
      return results;
    },
    [fileBoundaries, effectivePieceLength],
  );

  // Handle Mouse Move on Canvas for Interactive Tooltip
  const handleMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

    const { cellSize, padding, cols, piecesPerCell, activeCells } = gridLayout;
    const col = Math.floor((x - padding) / cellSize);
    const row = Math.floor((y - padding) / cellSize);

    if (col < 0 || col >= cols || row < 0) {
      setHoveredInfo(null);
      return;
    }

    const cellIdx = row * cols + col;
    if (cellIdx < 0 || cellIdx >= activeCells) {
      setHoveredInfo(null);
      return;
    }

    const pStart = Math.min(
      totalPieces - 1,
      Math.floor(cellIdx * piecesPerCell),
    );
    const pEnd = Math.min(
      totalPieces - 1,
      Math.floor((cellIdx + 1) * piecesPerCell) - 1,
    );
    const isRange = pStart < pEnd;

    const startBytes = pStart * effectivePieceLength;
    const endBytes = Math.min(totalBytes, (pEnd + 1) * effectivePieceLength);
    const byteRange = `${formatBytes(startBytes)} - ${formatBytes(endBytes)}`;

    const state = pieceStates[pStart];
    let statusText = "Missing";
    let statusColor = "#95a5a6";
    if (state === 2) {
      statusText = "Verified";
      statusColor = "#27ae60";
    } else if (state === 1) {
      statusText = "In-flight";
      statusColor = "#3498db";
    } else if (state === 3) {
      statusText = "Corrupted";
      statusColor = "#e74c3c";
    }

    const availability = pieceRarity[pStart] || 0;
    const matchedFiles = getFilesForPiece(pStart);

    setHoveredInfo({
      pieceIndex: pStart,
      pieceEndIndex: pEnd,
      isRange,
      byteRange,
      state,
      statusText,
      statusColor,
      availability,
      files: matchedFiles,
      clientX: e.clientX,
      clientY: e.clientY,
    });
  };

  const handleBarMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = barCanvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const x = Math.max(0, Math.min(rect.width, e.clientX - rect.left));
    const frac = rect.width > 0 ? x / rect.width : 0;

    const targetPiece = Math.min(
      totalPieces - 1,
      Math.max(0, Math.floor(frac * totalPieces)),
    );
    const startBytes = targetPiece * effectivePieceLength;
    const endBytes = Math.min(
      totalBytes,
      (targetPiece + 1) * effectivePieceLength,
    );
    const byteRange = `${formatBytes(startBytes)} - ${formatBytes(endBytes)}`;

    const state = pieceStates[targetPiece];
    let statusText = "Missing";
    let statusColor = "#95a5a6";
    if (state === 2) {
      statusText = "Verified";
      statusColor = "#27ae60";
    } else if (state === 1) {
      statusText = "In-flight";
      statusColor = "#3498db";
    } else if (state === 3) {
      statusText = "Corrupted";
      statusColor = "#e74c3c";
    }

    const availability = pieceRarity[targetPiece] || 0;
    const matchedFiles = getFilesForPiece(targetPiece);

    setHoveredInfo({
      pieceIndex: targetPiece,
      pieceEndIndex: targetPiece,
      isRange: false,
      byteRange,
      state,
      statusText,
      statusColor,
      availability,
      files: matchedFiles,
      clientX: e.clientX,
      clientY: e.clientY,
    });
  };

  const handleMouseLeave = () => {
    setHoveredInfo(null);
  };

  // Stats calculation
  const stats = useMemo(() => {
    return calculatePieceStats(pieceStates, totalPieces, progress);
  }, [pieceStates, totalPieces, progress]);

  return (
    <div
      ref={containerRef}
      className={`piece-map-root ${className}`}
      style={{
        position: "relative",
        padding: "0.85rem",
        backgroundColor: "var(--bg-secondary, rgba(255, 255, 255, 0.03))",
        borderRadius: "8px",
        border: "1px solid var(--border-light, #302c24)",
        display: "flex",
        flexDirection: "column",
        gap: "0.65rem",
      }}
    >
      {/* Header bar: Title, verified badge, specs, view mode buttons */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          gap: "0.5rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
          <span style={{ fontWeight: 600, fontSize: "0.85rem" }}>
            🧩 BitTorrent Piece Map
          </span>
          <span
            className={`badge ${progress >= 1.0 ? "badge-success" : "badge-primary"}`}
            style={{ fontSize: "0.72rem" }}
          >
            {(progress * 100).toFixed(1)}% Verified
          </span>
        </div>

        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.5rem",
            flexWrap: "wrap",
          }}
        >
          <span
            style={{
              fontSize: "0.74rem",
              color: "var(--text-muted, #9c9484)",
              fontFamily: "monospace",
            }}
          >
            {totalPieces.toLocaleString()} pieces ×{" "}
            {formatBytes(effectivePieceLength)}
          </span>

          {/* Layout Mode Toggles (Bar vs Grid) */}
          <div
            className="view-toggle"
            style={{ margin: 0, display: "flex", gap: "2px" }}
          >
            <button
              type="button"
              className={`view-toggle-btn ${layoutMode === "bar" ? "active" : ""}`}
              onClick={() => setLayoutMode("bar")}
              style={{
                padding: "0.2rem 0.45rem",
                fontSize: "0.7rem",
                backgroundColor:
                  layoutMode === "bar"
                    ? "var(--accent, #c8a84e)"
                    : "transparent",
                color: layoutMode === "bar" ? "#000" : "inherit",
                border: "1px solid var(--border-light, #38332b)",
                borderRadius: "3px",
                cursor: "pointer",
              }}
              title="Linear Canvas Bar View"
            >
              Bar
            </button>
            <button
              type="button"
              className={`view-toggle-btn ${layoutMode === "grid" ? "active" : ""}`}
              onClick={() => setLayoutMode("grid")}
              style={{
                padding: "0.2rem 0.45rem",
                fontSize: "0.7rem",
                backgroundColor:
                  layoutMode === "grid"
                    ? "var(--accent, #c8a84e)"
                    : "transparent",
                color: layoutMode === "grid" ? "#000" : "inherit",
                border: "1px solid var(--border-light, #38332b)",
                borderRadius: "3px",
                cursor: "pointer",
              }}
              title="Matrix Grid View"
            >
              Grid
            </button>
          </div>

          {/* 3 View Mode Toggles */}
          <div
            className="view-toggle"
            style={{ margin: 0, display: "flex", gap: "2px" }}
          >
            <button
              type="button"
              className={`view-toggle-btn ${viewMode === "status" ? "active" : ""}`}
              onClick={() => setViewMode("status")}
              style={{
                padding: "0.2rem 0.45rem",
                fontSize: "0.7rem",
                backgroundColor:
                  viewMode === "status"
                    ? "var(--accent, #c8a84e)"
                    : "transparent",
                color: viewMode === "status" ? "#000" : "inherit",
                border: "1px solid var(--border-light, #38332b)",
                borderRadius: "3px",
                cursor: "pointer",
              }}
              title="Download Status: Missing, In-flight, Verified, Corrupted"
            >
              Status
            </button>
            <button
              type="button"
              className={`view-toggle-btn ${viewMode === "rarity" ? "active" : ""}`}
              onClick={() => setViewMode("rarity")}
              style={{
                padding: "0.2rem 0.45rem",
                fontSize: "0.7rem",
                backgroundColor:
                  viewMode === "rarity"
                    ? "var(--accent, #c8a84e)"
                    : "transparent",
                color: viewMode === "rarity" ? "#000" : "inherit",
                border: "1px solid var(--border-light, #38332b)",
                borderRadius: "3px",
                cursor: "pointer",
              }}
              title="Swarm Availability Heatmap (Rare -> Common)"
            >
              Rarity Heatmap
            </button>
            <button
              type="button"
              className={`view-toggle-btn ${viewMode === "files" ? "active" : ""}`}
              onClick={() => setViewMode("files")}
              style={{
                padding: "0.2rem 0.45rem",
                fontSize: "0.7rem",
                backgroundColor:
                  viewMode === "files"
                    ? "var(--accent, #c8a84e)"
                    : "transparent",
                color: viewMode === "files" ? "#000" : "inherit",
                border: "1px solid var(--border-light, #38332b)",
                borderRadius: "3px",
                cursor: "pointer",
              }}
              title="File Boundary Overlays"
            >
              File Boundaries
            </button>
          </div>
        </div>
      </div>

      {/* Accessible Linear Progress Bar Summary (WAI-ARIA compliant) */}
      <div
        role="progressbar"
        aria-valuenow={Math.round(Math.min(100, Math.max(0, progress * 100)))}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuetext={`${(progress * 100).toFixed(1)}% verified`}
        style={{
          position: "relative",
          width: "100%",
          height: "10px",
          backgroundColor: "rgba(255, 255, 255, 0.08)",
          borderRadius: "3px",
          overflow: "hidden",
          border: "1px solid var(--border-light, #38332b)",
        }}
      >
        <div
          style={{
            width: `${Math.min(100, Math.max(0, progress * 100))}%`,
            height: "100%",
            background:
              progress >= 1.0
                ? "linear-gradient(90deg, #27ae60 0%, #2ecc71 100%)"
                : "linear-gradient(90deg, #c8a84e 0%, #e67e22 100%)",
            transition: "width 0.3s ease",
          }}
        />
      </div>

      {/* Visualizer: Linear Bar or Matrix Grid */}
      {layoutMode === "bar" ? (
        <div
          ref={barContainerRef}
          style={{
            position: "relative",
            width: "100%",
            height: "26px",
            backgroundColor: "#151412",
            borderRadius: "4px",
            overflow: "hidden",
            border: "1px solid var(--border-light, #302c24)",
            display: "flex",
            alignItems: "center",
            padding: "1px",
          }}
        >
          <canvas
            ref={barCanvasRef}
            role="img"
            aria-label="BitTorrent Piece Map Linear Bar"
            onMouseMove={handleBarMouseMove}
            onMouseLeave={handleMouseLeave}
            style={{
              display: "block",
              width: "100%",
              height: "24px",
              cursor: "crosshair",
            }}
          />
        </div>
      ) : (
        <div
          style={{
            position: "relative",
            width: "100%",
            display: "flex",
            justifyContent: "center",
            backgroundColor: "#151412",
            borderRadius: "6px",
            padding: "4px",
            border: "1px solid var(--border-light, #302c24)",
            overflow: "hidden",
          }}
        >
          <canvas
            ref={canvasRef}
            role="img"
            aria-label="BitTorrent Piece Map Grid"
            onMouseMove={handleMouseMove}
            onMouseLeave={handleMouseLeave}
            style={{
              display: "block",
              cursor: "crosshair",
            }}
          />
        </div>
      )}

      {/* Mode C: Interactive File Boundary List */}
      {viewMode === "files" && fileBoundaries.length > 0 && (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "0.35rem",
            backgroundColor: "rgba(0, 0, 0, 0.2)",
            padding: "0.5rem",
            borderRadius: "4px",
            border: "1px solid var(--border-light, #302c24)",
            maxHeight: "130px",
            overflowY: "auto",
          }}
        >
          <div
            style={{
              fontSize: "0.72rem",
              fontWeight: 600,
              color: "var(--text-secondary, #b0a898)",
              display: "flex",
              justifyContent: "space-between",
            }}
          >
            <span>Hover or select file to overlay piece boundaries:</span>
            {selectedFileIndex !== null && (
              <button
                type="button"
                onClick={() => setSelectedFileIndex(null)}
                style={{
                  background: "none",
                  border: "none",
                  color: "var(--accent, #c8a84e)",
                  fontSize: "0.7rem",
                  cursor: "pointer",
                  padding: 0,
                }}
              >
                Clear selection
              </button>
            )}
          </div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: "0.35rem" }}>
            {fileBoundaries.map((fb, idx) => {
              const isSelected = selectedFileIndex === idx;
              const isHovered = hoveredFileIndex === idx;
              const color = FILE_PALETTE[fb.colorIndex % FILE_PALETTE.length];
              const fileName = fb.file.path.split("/").pop() || fb.file.path;

              return (
                <button
                  key={fb.file.id || idx}
                  type="button"
                  onMouseEnter={() => setHoveredFileIndex(idx)}
                  onMouseLeave={() => setHoveredFileIndex(null)}
                  onClick={() => setSelectedFileIndex(isSelected ? null : idx)}
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "0.35rem",
                    padding: "0.2rem 0.5rem",
                    fontSize: "0.68rem",
                    borderRadius: "3px",
                    border: isSelected
                      ? "1px solid #fff"
                      : isHovered
                        ? `1px solid ${color}`
                        : "1px solid var(--border-light, #302c24)",
                    backgroundColor:
                      isSelected || isHovered
                        ? "rgba(255,255,255,0.12)"
                        : "rgba(0,0,0,0.3)",
                    color: "#fff",
                    cursor: "pointer",
                  }}
                  title={`${fb.file.path} (Pieces ${fb.startPiece} - ${fb.endPiece})`}
                >
                  <span
                    style={{
                      width: "8px",
                      height: "8px",
                      borderRadius: "2px",
                      backgroundColor: color,
                    }}
                  />
                  <span
                    style={{
                      maxWidth: "160px",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {fileName}
                  </span>
                  <span
                    style={{
                      color: "var(--text-muted, #888)",
                      fontSize: "0.64rem",
                    }}
                  >
                    ({formatBytes(fb.file.size)})
                  </span>
                </button>
              );
            })}
          </div>
        </div>
      )}

      {/* Footer Legends */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          flexWrap: "wrap",
          fontSize: "0.72rem",
          color: "var(--text-muted, #9c9484)",
          gap: "0.5rem",
        }}
      >
        {/* Mode-specific Legend */}
        {viewMode === "status" && (
          <div style={{ display: "flex", gap: "0.75rem", flexWrap: "wrap" }}>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.3rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#27ae60",
                }}
              />
              Verified ({stats.verified})
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.3rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#3498db",
                }}
              />
              In-flight ({stats.inFlight})
            </span>
            {stats.corrupted > 0 && (
              <span
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "0.3rem",
                }}
              >
                <span
                  style={{
                    width: "8px",
                    height: "8px",
                    borderRadius: "2px",
                    backgroundColor: "#e74c3c",
                  }}
                />
                Corrupted ({stats.corrupted})
              </span>
            )}
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.3rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#282520",
                }}
              />
              Missing ({stats.missing})
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.3rem",
              }}
            >
              {stats.completeCount} / {totalPieces} Complete
            </span>
          </div>
        )}

        {viewMode === "rarity" && (
          <div
            style={{
              display: "flex",
              gap: "0.6rem",
              alignItems: "center",
              flexWrap: "wrap",
            }}
          >
            <span>Availability:</span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#282520",
                }}
              />{" "}
              0
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#e74c3c",
                }}
              />{" "}
              1 (Rare)
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#e67e22",
                }}
              />{" "}
              2-3
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#f1c40f",
                }}
              />{" "}
              4-5
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#82c91e",
                }}
              />{" "}
              6-9
            </span>
            <span
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "0.25rem",
              }}
            >
              <span
                style={{
                  width: "8px",
                  height: "8px",
                  borderRadius: "2px",
                  backgroundColor: "#27ae60",
                }}
              />{" "}
              10+
            </span>
          </div>
        )}

        {viewMode === "files" && (
          <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <span>
              File count: {fileBoundaries.length} file
              {fileBoundaries.length === 1 ? "" : "s"}
            </span>
          </div>
        )}

        {gridLayout.isBinned && (
          <span style={{ fontStyle: "italic", fontSize: "0.68rem" }}>
            (Binned: ~{Math.round(gridLayout.piecesPerCell)} pieces/cell)
          </span>
        )}
      </div>

      {/* Floating Interactive Tooltip */}
      {hoveredInfo && (
        <div
          style={{
            position: "fixed",
            left: Math.min(window.innerWidth - 280, hoveredInfo.clientX + 14),
            top: Math.min(window.innerHeight - 150, hoveredInfo.clientY + 14),
            zIndex: 9999,
            backgroundColor: "rgba(18, 17, 15, 0.95)",
            backdropFilter: "blur(6px)",
            border: "1px solid var(--border-light, #38332b)",
            borderRadius: "6px",
            padding: "0.6rem 0.8rem",
            color: "#ede8de",
            fontSize: "0.74rem",
            boxShadow: "0 8px 24px rgba(0, 0, 0, 0.6)",
            pointerEvents: "none",
            display: "flex",
            flexDirection: "column",
            gap: "0.3rem",
            minWidth: "220px",
            maxWidth: "320px",
          }}
        >
          {/* Piece Index & Byte range */}
          <div
            style={{
              fontWeight: 600,
              fontSize: "0.8rem",
              color: "var(--accent, #c8a84e)",
              borderBottom: "1px solid rgba(255, 255, 255, 0.1)",
              paddingBottom: "0.25rem",
            }}
          >
            Piece #{hoveredInfo.pieceIndex}
            {hoveredInfo.isRange ? ` - #${hoveredInfo.pieceEndIndex}` : ""} [
            {hoveredInfo.byteRange}]
          </div>

          {/* Status: Verified / In-flight / Missing / Corrupted */}
          <div style={{ display: "flex", justifyContent: "space-between" }}>
            <span style={{ color: "var(--text-muted, #9c9484)" }}>Status:</span>
            <span style={{ fontWeight: 600, color: hoveredInfo.statusColor }}>
              {hoveredInfo.statusText}
            </span>
          </div>

          {/* Swarm Availability */}
          <div style={{ display: "flex", justifyContent: "space-between" }}>
            <span style={{ color: "var(--text-muted, #9c9484)" }}>
              Swarm Availability:
            </span>
            <span style={{ fontWeight: 600 }}>
              {hoveredInfo.availability} peer
              {hoveredInfo.availability === 1 ? "" : "s"}
            </span>
          </div>

          {/* Containing file name(s) and offset */}
          {hoveredInfo.files.length > 0 && (
            <div
              style={{
                borderTop: "1px solid rgba(255, 255, 255, 0.08)",
                paddingTop: "0.3rem",
                marginTop: "0.15rem",
                display: "flex",
                flexDirection: "column",
                gap: "0.2rem",
              }}
            >
              <span
                style={{
                  color: "var(--text-muted, #9c9484)",
                  fontSize: "0.7rem",
                }}
              >
                Containing file{hoveredInfo.files.length > 1 ? "s" : ""}:
              </span>
              {hoveredInfo.files.map((f, idx) => (
                <div
                  key={idx}
                  style={{
                    fontSize: "0.7rem",
                    display: "flex",
                    flexDirection: "column",
                    backgroundColor: "rgba(255, 255, 255, 0.04)",
                    padding: "0.2rem 0.35rem",
                    borderRadius: "3px",
                  }}
                >
                  <span
                    style={{
                      fontWeight: 500,
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                    title={f.name}
                  >
                    📄 {f.name}
                  </span>
                  <span
                    style={{
                      color: "var(--text-muted, #888)",
                      fontSize: "0.66rem",
                    }}
                  >
                    Offset: {formatBytes(f.offset)} / {formatBytes(f.size)}
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default PieceMap;
