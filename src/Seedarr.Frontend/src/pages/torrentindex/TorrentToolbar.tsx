import { SeedingConfig } from "../../api/types";
import { formatSpeed } from "../../utils/formatters";
import { useTranslation } from "../../i18n";
import {
  PlusIcon,
  PlayIcon,
  StopIcon,
  TableIcon,
  GridIcon,
  FilterIcon,
  SlidersIcon,
} from "../../components/icons/UIIcons";
import { ViewMode } from "./types";

interface TorrentToolbarProps {
  count: number;
  totalUploadSpeed: number;
  totalDownloadSpeed: number;
  seedingConfig: SeedingConfig | undefined;
  adjustSpeed: (
    field: "maxUploadSpeedKbps" | "maxDownloadSpeedKbps",
    factor: number,
  ) => void;
  filter: string;
  onFilterChange: (value: string) => void;
  viewMode: ViewMode;
  onViewModeChange: (mode: ViewMode) => void;
  onAddTorrent: () => void;
  onSearchIndexers?: () => void;
  onStartAll: () => void;
  onStopAll: () => void;
  selectedCount: number;
  bulkPending: boolean;
  onBulkStart: () => void;
  onBulkStop: () => void;
  onBulkDelete: () => void;
  onBulkClear: () => void;
  isFilterCollapsed?: boolean;
  onToggleFilter?: () => void;
  isQuickControlsOpen?: boolean;
  onToggleQuickControls?: () => void;
}

export function TorrentToolbar({
  count,
  totalUploadSpeed,
  totalDownloadSpeed,
  seedingConfig,
  adjustSpeed,
  filter,
  onFilterChange,
  viewMode,
  onViewModeChange,
  onAddTorrent,
  onSearchIndexers,
  onStartAll,
  onStopAll,
  selectedCount,
  bulkPending,
  onBulkStart,
  onBulkStop,
  onBulkDelete,
  onBulkClear,
  isFilterCollapsed = false,
  onToggleFilter,
  isQuickControlsOpen = false,
  onToggleQuickControls,
}: TorrentToolbarProps) {
  const { t } = useTranslation();

  return (
    <div className="page-header">
      <div className="page-header-group">
        {onToggleFilter && (
          <button
            type="button"
            className={`btn btn-small toggle-filter-btn${isFilterCollapsed ? " active" : ""}`}
            onClick={onToggleFilter}
            title={
              isFilterCollapsed ? "Show filter sidebar" : "Hide filter sidebar"
            }
            aria-label="Toggle filter sidebar"
            style={{ display: "inline-flex", alignItems: "center", gap: "5px" }}
          >
            <FilterIcon size={12} />
            <span>{isFilterCollapsed ? `▶ ${t("torrents.toggleFilter", undefined, "Filter")}` : `◀ ${t("torrents.toggleFilter", undefined, "Filter")}`}</span>
          </button>
        )}
        <h1 className="page-heading">{t("torrents.title", undefined, "Torrents")} ({count})</h1>
        <button className="btn btn-success" onClick={onAddTorrent}>
          <PlusIcon size={13} /> {t("torrents.addTorrent", undefined, "Add Torrent")}
        </button>
        {onSearchIndexers && (
          <button
            type="button"
            className="btn btn-outline"
            onClick={onSearchIndexers}
            style={{ fontSize: "0.82rem" }}
          >
            🔍 {t("modals.indexerSearch", undefined, "Search Indexers")}
          </button>
        )}
        {onToggleQuickControls && (
          <button
            type="button"
            className={`btn btn-small quick-controls-toggle-btn${isQuickControlsOpen ? " active" : ""}`}
            onClick={onToggleQuickControls}
            title={
              isQuickControlsOpen
                ? "Hide Quick Controls (Q)"
                : "Show Quick Controls (Q)"
            }
            aria-label="Toggle Quick Controls drawer"
            style={{ display: "inline-flex", alignItems: "center", gap: "6px" }}
          >
            <SlidersIcon size={13} />
            <span>{t("torrents.quickControls", undefined, "Quick Controls")}</span>
            <kbd className="quick-controls-kbd">Q</kbd>
          </button>
        )}
        {selectedCount > 0 && (
          <div className="bulk-actions">
            <span className="bulk-actions-count">{t("torrents.selectedCount", { count: selectedCount }, `${selectedCount} selected`)}</span>
            <button
              className="btn btn-small btn-success"
              onClick={onBulkStart}
              disabled={bulkPending}
            >
              <PlayIcon size={12} /> {t("torrents.start", undefined, "Start")}
            </button>
            <button
              className="btn btn-small"
              onClick={onBulkStop}
              disabled={bulkPending}
            >
              <StopIcon size={12} /> {t("torrents.stop", undefined, "Stop")}
            </button>
            <button
              className="btn btn-small btn-danger"
              onClick={onBulkDelete}
              disabled={bulkPending}
            >
              {t("common.delete", undefined, "Delete")}
            </button>
            <button
              className="btn btn-small"
              onClick={onBulkClear}
              disabled={bulkPending}
            >
              {t("common.clear", undefined, "Clear")}
            </button>
          </div>
        )}
      </div>
      <div className="page-header-actions">
        <button className="btn btn-success" onClick={onStartAll}>
          <PlayIcon size={13} /> {t("torrents.startAll", undefined, "Start All")}
        </button>
        <button className="btn btn-danger" onClick={onStopAll}>
          <StopIcon size={13} /> {t("torrents.stopAll", undefined, "Stop All")}
        </button>
        <div
          className="speed-controls"
          style={{ display: "flex", alignItems: "center", gap: "4px" }}
        >
          <span style={{ fontSize: "0.85em", opacity: 0.8 }}>
            UL: {formatSpeed(totalUploadSpeed)}
          </span>
          <button
            className="btn btn-small btn-success"
            onClick={() => adjustSpeed("maxUploadSpeedKbps", 2)}
            title="Double upload speed limit"
            disabled={!seedingConfig}
          >
            &#9650;&#9650;
          </button>
          <button
            className="btn btn-small btn-success"
            onClick={() => adjustSpeed("maxUploadSpeedKbps", 0.5)}
            title="Halve upload speed limit"
            disabled={!seedingConfig}
          >
            &#9660;&#9660;
          </button>
          <span style={{ fontSize: "0.85em", opacity: 0.8, marginLeft: "8px" }}>
            DL: {formatSpeed(totalDownloadSpeed)}
          </span>
          <button
            className="btn btn-small btn-danger"
            onClick={() => adjustSpeed("maxDownloadSpeedKbps", 2)}
            title="Double download speed limit"
            disabled={!seedingConfig}
          >
            &#9650;&#9650;
          </button>
          <button
            className="btn btn-small btn-danger"
            onClick={() => adjustSpeed("maxDownloadSpeedKbps", 0.5)}
            title="Halve download speed limit"
            disabled={!seedingConfig}
          >
            &#9660;&#9660;
          </button>
        </div>
        <input
          type="text"
          className="search-input"
          placeholder={t("torrents.filterPlaceholder", undefined, "Filter torrents...")}
          value={filter}
          onChange={(e) => onFilterChange(e.target.value)}
        />
        <div className="view-toggle">
          <button
            className={`view-toggle-btn${viewMode === "table" ? " active" : ""}`}
            onClick={() => onViewModeChange("table")}
            title="Table view"
          >
            <TableIcon size={13} /> {t("torrents.tableView", undefined, "Table")}
          </button>
          <button
            className={`view-toggle-btn${viewMode === "grid" ? " active" : ""}`}
            onClick={() => onViewModeChange("grid")}
            title="Grid view"
          >
            <GridIcon size={13} /> {t("torrents.gridView", undefined, "Grid")}
          </button>
        </div>
      </div>
    </div>
  );
}
