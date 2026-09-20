import React, { useState, useEffect } from "react";
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
  ColumnsIcon,
} from "../../components/icons/UIIcons";
import { ViewMode } from "./types";
import { ColumnCustomizerModal } from "./ColumnCustomizerModal";
import {
  ColumnCategory,
  PresetName,
  useColumnPreferences,
} from "./columnPreferences";

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
  onBulkMoveQueue?: (position: "top" | "up" | "down" | "bottom") => void;
  isFilterCollapsed?: boolean;
  onToggleFilter?: () => void;
  isQuickControlsOpen?: boolean;
  onToggleQuickControls?: () => void;
  visibleColumns?: Set<string>;
  onToggleColumn?: (key: string) => void;
  onResetColumns?: () => void;
  onResetSort?: () => void;
  onSelectAllColumns?: () => void;
  onDeselectAllColumns?: () => void;
  onApplyColumnPreset?: (preset: PresetName) => void;
  onToggleCategoryColumns?: (category: ColumnCategory, enable?: boolean) => void;
  isColumnCustomizerOpen?: boolean;
  onToggleColumnCustomizer?: () => void;
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
  onBulkMoveQueue,
  isFilterCollapsed = false,
  onToggleFilter,
  isQuickControlsOpen = false,
  onToggleQuickControls,
  visibleColumns,
  onToggleColumn,
  onResetColumns,
  onResetSort,
  onSelectAllColumns,
  onDeselectAllColumns,
  onApplyColumnPreset,
  onToggleCategoryColumns,
  isColumnCustomizerOpen,
  onToggleColumnCustomizer,
}: TorrentToolbarProps) {
  const [isInternalCustomizerOpen, setIsInternalCustomizerOpen] = useState(false);
  const defaultPrefs = useColumnPreferences();

  const isCustomizerOpen =
    isColumnCustomizerOpen !== undefined
      ? isColumnCustomizerOpen
      : isInternalCustomizerOpen;

  const handleOpenCustomizer = () => {
    if (onToggleColumnCustomizer) {
      onToggleColumnCustomizer();
    } else {
      setIsInternalCustomizerOpen(true);
    }
  };

  const handleCloseCustomizer = () => {
    if (onToggleColumnCustomizer) {
      onToggleColumnCustomizer();
    } else {
      setIsInternalCustomizerOpen(false);
    }
  };

  const activeVisibleColumns = visibleColumns ?? defaultPrefs.visibleColumns;
  const handleToggleColumn = onToggleColumn ?? defaultPrefs.toggleColumn;
  const handleResetDefaults = onResetColumns ?? defaultPrefs.resetToDefaults;
  const handleResetSort = onResetSort ?? defaultPrefs.resetSort;
  const handleSelectAll = onSelectAllColumns ?? defaultPrefs.selectAll;
  const handleDeselectAll = onDeselectAllColumns ?? defaultPrefs.deselectAll;
  const handleApplyPreset = onApplyColumnPreset ?? defaultPrefs.applyPreset;
  const handleToggleCategory = onToggleCategoryColumns ?? defaultPrefs.toggleCategory;
  const { t } = useTranslation();
  const [localFilter, setLocalFilter] = useState(filter);

  useEffect(() => {
    setLocalFilter(filter);
  }, [filter]);

  useEffect(() => {
    if (localFilter === filter) return;
    const timer = setTimeout(() => {
      onFilterChange(localFilter);
    }, 200);
    return () => clearTimeout(timer);
  }, [localFilter, filter, onFilterChange]);

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
            <span>
              {isFilterCollapsed
                ? `▶ ${t("torrents.toggleFilter", undefined, "Filter")}`
                : `◀ ${t("torrents.toggleFilter", undefined, "Filter")}`}
            </span>
          </button>
        )}
        <h1 className="page-heading">
          {t("torrents.title", undefined, "Torrents")} ({count})
        </h1>
        {selectedCount > 0 ? (
          <div className="bulk-actions">
            <span className="bulk-actions-count">
              {t(
                "torrents.selectedCount",
                { count: selectedCount },
                `${selectedCount} selected`,
              )}
            </span>
            <button
              className="btn btn-success"
              onClick={onBulkStart}
              disabled={bulkPending}
            >
              <PlayIcon size={13} /> {t("torrents.start", undefined, "Start")}
            </button>
            <button
              className="btn btn-outline"
              onClick={onBulkStop}
              disabled={bulkPending}
            >
              <StopIcon size={13} /> {t("torrents.stop", undefined, "Stop")}
            </button>
            <button
              className="btn btn-danger"
              onClick={onBulkDelete}
              disabled={bulkPending}
            >
              {t("common.delete", undefined, "Delete")}
            </button>
            {onBulkMoveQueue && (
              <div
                className="btn-group"
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "2px",
                  marginLeft: "4px",
                  borderLeft:
                    "1px solid var(--border, rgba(255, 255, 255, 0.15))",
                  paddingLeft: "6px",
                }}
              >
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("top")}
                  disabled={bulkPending}
                  title={t(
                    "torrents.contextMenu.top",
                    undefined,
                    "Move to Top",
                  )}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ⤒ {t("torrents.contextMenu.top", undefined, "Top")}
                </button>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("up")}
                  disabled={bulkPending}
                  title={t("torrents.contextMenu.up", undefined, "Move Up")}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ▲ {t("torrents.contextMenu.up", undefined, "Up")}
                </button>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("down")}
                  disabled={bulkPending}
                  title={t("torrents.contextMenu.down", undefined, "Move Down")}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ▼ {t("torrents.contextMenu.down", undefined, "Down")}
                </button>
                <button
                  type="button"
                  className="btn btn-outline"
                  onClick={() => onBulkMoveQueue("bottom")}
                  disabled={bulkPending}
                  title={t(
                    "torrents.contextMenu.bottom",
                    undefined,
                    "Move to Bottom",
                  )}
                  style={{ padding: "0.25rem 0.5rem", fontSize: "0.8rem" }}
                >
                  ⤓ {t("torrents.contextMenu.bottom", undefined, "Bottom")}
                </button>
              </div>
            )}
            <button
              className="btn btn-outline"
              onClick={onBulkClear}
              disabled={bulkPending}
            >
              {t("common.clear", undefined, "Clear")}
            </button>
          </div>
        ) : (
          <>
            <button className="btn btn-success" onClick={onAddTorrent}>
              <PlusIcon size={13} />{" "}
              {t("torrents.addTorrent", undefined, "Add Torrent")}
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
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "6px",
                }}
              >
                <SlidersIcon size={13} />
                <span>
                  {t("torrents.quickControls", undefined, "Quick Controls")}
                </span>
                <kbd className="quick-controls-kbd">Q</kbd>
              </button>
            )}
          </>
        )}
      </div>
      <div className="page-header-actions">
        <button className="btn btn-success" onClick={onStartAll}>
          <PlayIcon size={13} />{" "}
          {t("torrents.startAll", undefined, "Start All")}
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
            aria-label="Double upload speed limit"
            disabled={!seedingConfig}
          >
            &#9650;&#9650;
          </button>
          <button
            className="btn btn-small btn-success"
            onClick={() => adjustSpeed("maxUploadSpeedKbps", 0.5)}
            title="Halve upload speed limit"
            aria-label="Halve upload speed limit"
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
            aria-label="Double download speed limit"
            disabled={!seedingConfig}
          >
            &#9650;&#9650;
          </button>
          <button
            className="btn btn-small btn-danger"
            onClick={() => adjustSpeed("maxDownloadSpeedKbps", 0.5)}
            title="Halve download speed limit"
            aria-label="Halve download speed limit"
            disabled={!seedingConfig}
          >
            &#9660;&#9660;
          </button>
        </div>
        <input
          type="text"
          className="search-input"
          placeholder={t(
            "torrents.filterPlaceholder",
            undefined,
            "Filter torrents...",
          )}
          aria-label={t(
            "torrents.filterPlaceholder",
            undefined,
            "Filter torrents",
          )}
          value={localFilter}
          onChange={(e) => setLocalFilter(e.target.value)}
        />
        <div className="view-toggle">
          <button
            className={`view-toggle-btn${viewMode === "table" ? " active" : ""}`}
            onClick={() => onViewModeChange("table")}
            title="Table view"
            aria-pressed={viewMode === "table"}
          >
            <TableIcon size={13} />{" "}
            {t("torrents.tableView", undefined, "Table")}
          </button>
          <button
            className={`view-toggle-btn${viewMode === "grid" ? " active" : ""}`}
            onClick={() => onViewModeChange("grid")}
            title="Grid view"
            aria-pressed={viewMode === "grid"}
          >
            <GridIcon size={13} /> {t("torrents.gridView", undefined, "Grid")}
          </button>
        </div>
        {viewMode === "table" && (
          <button
            type="button"
            className={`btn btn-outline column-customizer-btn${isCustomizerOpen ? " active" : ""}`}
            onClick={handleOpenCustomizer}
            title={t("torrents.columns", undefined, "Columns")}
            aria-label={t("torrents.columns", undefined, "Columns")}
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: "6px",
              fontSize: "0.82rem",
            }}
          >
            <ColumnsIcon size={13} />
            <span>{t("torrents.columns", undefined, "Columns")}</span>
          </button>
        )}
      </div>
      <ColumnCustomizerModal
        isOpen={isCustomizerOpen}
        onClose={handleCloseCustomizer}
        visibleColumns={activeVisibleColumns}
        onToggleColumn={handleToggleColumn}
        onResetToDefaults={handleResetDefaults}
        onResetSort={handleResetSort}
        onSelectAll={handleSelectAll}
        onDeselectAll={handleDeselectAll}
        onApplyPreset={handleApplyPreset}
        onToggleCategory={handleToggleCategory}
      />
    </div>
  );
}
